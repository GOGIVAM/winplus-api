using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Repositories;
using Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Données du tableau de bord parent : statistiques de l'enfant (taux
    /// d'achèvement, note moyenne arrondie), activités récentes, quiz non encore
    /// tentés, profil réservé au rôle parent, objectifs de tous statuts, paiements
    /// des 30 derniers jours, événements délégués au dépôt. Objets anonymes lus
    /// par réflexion.
    /// </summary>
    public class ParentServiceTests
    {
        private const int P = 1001, Child = 1002, Other = 1003;

        private static (TestApplicationDbContext Db, ParentService Svc, Mock<IEventRepository> Events) Build()
        {
            var db = TestApplicationDbContext.Create();
            var events = new Mock<IEventRepository>();
            return (db, new ParentService(db, events.Object, new Mock<ISubscriptionRepository>().Object, NullLogger<ParentService>.Instance), events);
        }

        private static object? Prop(object? o, string name) => o!.GetType().GetProperty(name)!.GetValue(o);

        [Fact]
        public async Task Sans_donnee_les_statistiques_de_l_enfant_valent_zero()
        {
            var (_, svc, _) = Build();
            object s = await svc.GetChildStatsAsync(P, Child);
            Assert.Equal(0, Prop(s, "enrollmentCount"));
            Assert.Equal(0m, Prop(s, "completionRate"));
            Assert.Equal(0m, Prop(s, "averageScore"));
        }

        [Fact]
        public async Task Le_taux_d_achevement_et_la_note_moyenne_sont_arrondis_a_deux_decimales()
        {
            var (db, svc, _) = Build();
            db.Enrollments.AddRange(
                new Enrollment { Id = 1, UserId = Child, SubjectId = 1, IsCompleted = true, User = null!, Subject = null! },
                new Enrollment { Id = 2, UserId = Child, SubjectId = 2, User = null!, Subject = null! },
                new Enrollment { Id = 3, UserId = Child, SubjectId = 3, User = null!, Subject = null! },
                new Enrollment { Id = 4, UserId = Other, SubjectId = 1, IsCompleted = true, User = null!, Subject = null! });
            db.LearningHistories.AddRange(
                new LearningHistory { UserId = Child, SubjectId = 1, EventType = "q", Score = 10m },
                new LearningHistory { UserId = Child, SubjectId = 1, EventType = "q", Score = 13m },
                new LearningHistory { UserId = Child, SubjectId = 1, EventType = "q" });
            db.SaveChanges();
            object s = await svc.GetChildStatsAsync(P, Child);
            Assert.Equal(3, Prop(s, "enrollmentCount"));
            Assert.Equal(33.33m, Prop(s, "completionRate"));
            Assert.Equal(7.67m, Prop(s, "averageScore"));
        }

        [Fact]
        public async Task Les_activites_sont_les_plus_recentes_d_abord_et_limitees()
        {
            var (db, svc, _) = Build();
            for (var i = 1; i <= 4; i++)
                db.LearningHistories.Add(new LearningHistory { Id = i, UserId = Child, SubjectId = 1, EventType = $"e{i}", CreatedAt = new DateTime(2026, 1, i), IsCompleted = i % 2 == 0 });
            db.LearningHistories.Add(new LearningHistory { Id = 9, UserId = Other, SubjectId = 1, EventType = "autre", CreatedAt = new DateTime(2026, 2, 1) });
            db.SaveChanges();
            var list = (await svc.GetChildActivitiesAsync(P, Child, 3)).Cast<object>().ToList();
            Assert.Equal(new[] { 4, 3, 2 }, list.Select(a => (int)Prop(a, "Id")!));
            Assert.Equal("completed", Prop(list[0], "status"));
            Assert.Equal("ongoing", Prop(list[1], "status"));
        }

        [Fact]
        public async Task Les_quiz_proposes_excluent_ceux_deja_tentes_non_publies_et_supprimes()
        {
            var (db, svc, _) = Build();
            db.Quizzes.AddRange(
                new Quiz { Id = 1, Title = "Tenté", Subject = "Maths", IsPublished = true },
                new Quiz { Id = 2, Title = "Libre", Subject = "Maths", IsPublished = true },
                new Quiz { Id = 3, Title = "Brouillon", Subject = "Maths", IsPublished = false },
                new Quiz { Id = 4, Title = "Supprimé", Subject = "Maths", IsPublished = true, IsDeleted = true });
            db.QuizAttempts.Add(new QuizAttempt { UserId = Child, QuizId = 1, Score = 50m, CompletedAt = new DateTime(2026, 1, 1) });
            db.SaveChanges();
            var list = (await svc.GetChildQuizzesAsync(P, Child)).Cast<object>().ToList();
            Assert.Equal(2, Assert.Single(list).GetType().GetProperty("Id")!.GetValue(list[0]));
        }

        [Fact]
        public async Task Le_profil_n_est_renvoye_que_pour_un_compte_de_role_parent()
        {
            var (db, svc, _) = Build();
            db.Users.Add(new User { Id = P, Email = "p@t.local", Role = "parent", FirstName = "Jean", LastName = "Mbarga" });
            db.Users.Add(new User { Id = Other, Email = "s@t.local", Role = "student" });
            db.SaveChanges();
            object? ok = await svc.GetParentProfileAsync(P);
            Assert.Equal("Jean Mbarga", Prop(ok, "name"));
            Assert.Null(await svc.GetParentProfileAsync(Other));
            Assert.Null(await svc.GetParentProfileAsync(999));
        }

        [Fact]
        public async Task Les_objectifs_de_l_enfant_incluent_tous_les_statuts_du_plus_recent_au_plus_ancien()
        {
            var (db, svc, _) = Build();
            db.Goals.AddRange(
                new Goal { Id = 1, UserId = Child, Title = "vieux", Status = GoalStatus.Completed, CreatedAt = new DateTime(2026, 1, 1) },
                new Goal { Id = 2, UserId = Child, Title = "propose", Status = GoalStatus.Pending, CreatedAt = new DateTime(2026, 3, 1), ProposedByUserId = P },
                new Goal { Id = 3, UserId = Other, Title = "autre", CreatedAt = new DateTime(2026, 4, 1) });
            db.SaveChanges();
            var list = (await svc.GetChildGoalsAsync(P, Child)).Cast<object>().ToList();
            Assert.Equal(new[] { "propose", "vieux" }, list.Select(g => (string)Prop(g, "title")!));
            Assert.Equal(P, Prop(list[0], "proposedByUserId"));
        }

        [Fact]
        public async Task Les_paiements_sont_ceux_des_trente_derniers_jours_du_parent()
        {
            var (db, svc, _) = Build();
            db.Orders.AddRange(
                new Order { Id = 1, OrderNumber = "O1", UserId = P, OrderDate = DateTime.UtcNow.AddDays(-5), User = null! },
                new Order { Id = 2, OrderNumber = "O2", UserId = P, OrderDate = DateTime.UtcNow.AddDays(-60), User = null! },
                new Order { Id = 3, OrderNumber = "O3", UserId = Other, OrderDate = DateTime.UtcNow.AddDays(-1), User = null! });
            db.SaveChanges();
            var list = (await svc.GetUpcomingPaymentsAsync(P)).Cast<object>().ToList();
            Assert.Equal(1, Assert.Single(list).GetType().GetProperty("Id")!.GetValue(list[0]));
        }

        [Fact]
        public async Task Les_evenements_viennent_du_depot_et_une_panne_donne_une_liste_vide()
        {
            var (_, svc, events) = Build();
            events.Setup(e => e.GetUpcomingAsync(5)).ReturnsAsync(new List<Event> { new() { Id = 1 } });
            Assert.Single(await svc.GetUpcomingEventsAsync(P, 5));
            events.Setup(e => e.GetUpcomingAsync(7)).ThrowsAsync(new InvalidOperationException());
            Assert.Empty(await svc.GetUpcomingEventsAsync(P, 7));
        }
    }
}
