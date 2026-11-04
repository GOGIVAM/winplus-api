using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Alertes de décrochage aux parents : inactivité de plus de 7 jours chez un
    /// élève qui avait déjà travaillé (jamais pour un compte sans activité),
    /// gravité selon la durée, lien parent accepté exigé, déduplication par
    /// période d'inactivité (relancer ne recrée rien), rappel unique à l'élève.
    /// Les activités sont placées à des durées relatives à l'horloge système avec
    /// de larges marges. Notifications simulées.
    /// </summary>
    public class InstitutionDropoutAlertServiceTests
    {
        private const int Parent = 1, Child = 2;

        private static (TestApplicationDbContext Db, InstitutionDropoutAlertService Svc, Mock<INtfyService> Ntfy) Build(string linkStatus = "accepted")
        {
            var db = TestApplicationDbContext.Create();
            db.Users.AddRange(
                new User { Id = Parent, Email = "p@t.l", Role = "parent" },
                new User { Id = Child, Email = "c@t.l", Role = "student", FirstName = "Awa", CreatedAt = DateTime.UtcNow.AddDays(-200) });
            db.ParentStudentLinks.Add(new ParentStudentLink { ParentId = Parent, StudentId = Child, Status = linkStatus });
            db.SaveChanges();
            var ntfy = new Mock<INtfyService>();
            var services = new ServiceCollection();
            services.AddSingleton<ApplicationDbContext>(db);
            services.AddSingleton(ntfy.Object);
            var provider = services.BuildServiceProvider();
            var svc = new InstitutionDropoutAlertService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<InstitutionDropoutAlertService>.Instance);
            return (db, svc, ntfy);
        }

        private static void StudiedDaysAgo(TestApplicationDbContext db, int days) =>
            db.QuizAttempts.Add(new QuizAttempt { UserId = Child, QuizId = 1, Score = 50m, CompletedAt = DateTime.UtcNow.AddDays(-days), StartedAt = DateTime.UtcNow.AddDays(-days), IsCompleted = true });

        [Fact]
        public async Task Sans_lien_parent_accepte_aucune_alerte()
        {
            var (db, svc, _) = Build("pending");
            StudiedDaysAgo(db, 20);
            db.SaveChanges();
            await svc.RunAsync(CancellationToken.None);
            Assert.Empty(db.ParentAlerts);
        }

        [Fact]
        public async Task Un_eleve_qui_n_a_jamais_travaille_ne_declenche_pas_d_alerte()
        {
            var (db, svc, _) = Build();
            await svc.RunAsync(CancellationToken.None);
            Assert.Empty(db.ParentAlerts);
        }

        [Fact]
        public async Task Une_activite_recente_ne_declenche_pas_d_alerte()
        {
            var (db, svc, _) = Build();
            StudiedDaysAgo(db, 3);
            db.SaveChanges();
            await svc.RunAsync(CancellationToken.None);
            Assert.Empty(db.ParentAlerts);
        }

        [Fact]
        public async Task Dix_jours_d_inactivite_creent_une_alerte_moyenne_et_notifient_parent_et_eleve()
        {
            var (db, svc, ntfy) = Build();
            StudiedDaysAgo(db, 10);
            db.SaveChanges();
            await svc.RunAsync(CancellationToken.None);
            var alert = Assert.Single(db.ParentAlerts);
            Assert.Equal("InactiviteProlongee", alert.Type);
            Assert.Equal("Medium", alert.Severity);
            Assert.Equal(Parent, alert.ParentId);
            Assert.Equal(Child, alert.ChildId);
            Assert.Contains("Awa", alert.Content);
            Assert.StartsWith("inactive:", alert.DedupKey);
            ntfy.Verify(n => n.PublishAsync($"winplus-user-{Parent}", It.IsAny<string>(), It.IsAny<string>(), "default", It.IsAny<string[]?>(), Parent, "ParentAdvisor", "Child", Child), Times.Once);
            ntfy.Verify(n => n.PublishAsync($"winplus-user-{Child}", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]?>(), Child, "StudyReminder", It.IsAny<string?>(), It.IsAny<int?>()), Times.Once);
        }

        [Fact]
        public async Task Vingt_jours_d_inactivite_donnent_une_gravite_haute_et_une_priorite_elevee()
        {
            var (db, svc, ntfy) = Build();
            StudiedDaysAgo(db, 20);
            db.SaveChanges();
            await svc.RunAsync(CancellationToken.None);
            Assert.Equal("High", db.ParentAlerts.Single().Severity);
            ntfy.Verify(n => n.PublishAsync($"winplus-user-{Parent}", It.IsAny<string>(), It.IsAny<string>(), "high", It.IsAny<string[]?>(), Parent, "ParentAdvisor", "Child", Child), Times.Once);
        }

        [Fact]
        public async Task Relancer_la_passe_ne_recree_ni_alerte_ni_notification()
        {
            var (db, svc, ntfy) = Build();
            StudiedDaysAgo(db, 10);
            db.SaveChanges();
            await svc.RunAsync(CancellationToken.None);
            await svc.RunAsync(CancellationToken.None);
            Assert.Single(db.ParentAlerts);
            ntfy.Verify(n => n.PublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]?>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Exactly(2));
        }

        [Fact]
        public async Task La_derniere_activite_est_la_plus_recente_entre_quiz_et_session_d_etude()
        {
            var (db, svc, _) = Build();
            StudiedDaysAgo(db, 30);
            db.StudySessions.Add(new StudySession { UserId = Child, SubjectId = 1, Duration = 10, CreatedAt = DateTime.UtcNow.AddDays(-2) });
            db.SaveChanges();
            await svc.RunAsync(CancellationToken.None);
            Assert.Empty(db.ParentAlerts);
        }

        [Fact]
        public async Task Une_panne_de_notification_n_annule_pas_l_alerte_enregistree()
        {
            var (db, svc, ntfy) = Build();
            ntfy.Setup(n => n.PublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]?>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()))
                .ThrowsAsync(new InvalidOperationException("ntfy"));
            StudiedDaysAgo(db, 10);
            db.SaveChanges();
            await svc.RunAsync(CancellationToken.None);
            Assert.Single(db.ParentAlerts);
        }
    }
}
