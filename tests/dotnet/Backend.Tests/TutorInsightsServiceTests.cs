using System;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Suivi des élèves par un tuteur : historique des séances terminées
    /// seulement, fiche de révision unique par couple tuteur/élève (mise à jour
    /// plutôt que doublon), données du rapport mensuel (avis visibles dans le
    /// mois, moyenne, matières distinctes), dernier rapport de coaching.
    /// </summary>
    public class TutorInsightsServiceTests
    {
        private const int Tutor = 1, Kid = 2, Other = 3;

        private static (TestApplicationDbContext Db, TutorInsightsService Svc) Build()
        {
            var db = TestApplicationDbContext.Create();
            db.Users.AddRange(
                new User { Id = Tutor, Email = "t@t.local", Role = "student" },
                new User { Id = Kid, Email = "k@t.local", Role = "student", FirstName = "Awa", LastName = "Ndiaye" },
                new User { Id = Other, Email = "o@t.local", Role = "student" });
            db.TutorProfiles.AddRange(new TutorProfile { Id = 10, UserId = Tutor }, new TutorProfile { Id = 11, UserId = Other });
            db.SaveChanges();
            return (db, new TutorInsightsService(db));
        }

        private static TutorBooking B(int id, int profile, int student, string status, string? subject, DateTime date, string? summary = null, DateTime? completedAt = null) =>
            new() { Id = id, TutorProfileId = profile, StudentUserId = student, Status = status, Subject = subject, SessionDate = DateOnly.FromDateTime(date), SummaryText = summary, CompletedAt = completedAt };

        [Fact]
        public async Task La_liste_des_eleves_ne_compte_que_les_seances_terminees_du_tuteur()
        {
            var (db, svc) = Build();
            db.TutorBookings.AddRange(
                B(1, 10, Kid, "completed", "Maths", new DateTime(2026, 1, 1)),
                B(2, 10, Kid, "completed", "Physique", new DateTime(2026, 2, 1)),
                B(3, 10, Kid, "cancelled", "Chimie", new DateTime(2026, 3, 1)),
                B(4, 11, Kid, "completed", "Autre tuteur", new DateTime(2026, 4, 1)));
            db.SaveChanges();
            var list = await svc.GetStudentsWithHistoryAsync(Tutor);
            var item = Assert.Single(list);
            Assert.Equal(2, item.GetType().GetProperty("sessionsCount")!.GetValue(item));
            Assert.Equal("Physique", item.GetType().GetProperty("lastSubject")!.GetValue(item));
            Assert.Equal("Awa Ndiaye", item.GetType().GetProperty("studentName")!.GetValue(item));
        }

        [Fact]
        public async Task L_historique_d_un_eleve_renvoie_les_resumes_non_vides_du_plus_recent_au_plus_ancien()
        {
            var (db, svc) = Build();
            db.TutorBookings.AddRange(
                B(1, 10, Kid, "completed", "Maths", new DateTime(2026, 1, 1), "premier"),
                B(2, 10, Kid, "completed", "Physique", new DateTime(2026, 2, 1), "second"),
                B(3, 10, Kid, "completed", "Chimie", new DateTime(2026, 3, 1), "  "));
            db.SaveChanges();
            var (name, subject, summaries) = await svc.GetStudentHistoryAsync(Tutor, Kid);
            Assert.Equal("Awa Ndiaye", name);
            Assert.Equal("Chimie", subject);
            Assert.Equal(new[] { "second", "premier" }, summaries);
        }

        [Fact]
        public async Task Sans_seance_terminee_l_historique_est_vide_avec_le_nom_par_defaut()
        {
            var (_, svc) = Build();
            var (name, subject, summaries) = await svc.GetStudentHistoryAsync(Tutor, Kid);
            Assert.Equal("Élève", name);
            Assert.Null(subject);
            Assert.Empty(summaries);
        }

        [Fact]
        public async Task La_fiche_de_revision_est_creee_puis_mise_a_jour_sans_doublon()
        {
            var (db, svc) = Build();
            Assert.Null(await svc.GetRevisionSheetAsync(Tutor, Kid));
            await svc.SaveRevisionSheetAsync(Tutor, Kid, "Maths", "v1");
            var second = await svc.SaveRevisionSheetAsync(Tutor, Kid, "Physique", "v2");
            Assert.Single(db.TutorRevisionSheets);
            Assert.Equal("v2", second.Content);
            Assert.Equal("Physique", second.Subject);
            Assert.NotNull(second.UpdatedAt);
            Assert.NotNull(await svc.GetRevisionSheetAsync(Tutor, Kid));
            Assert.Null(await svc.GetRevisionSheetAsync(Other, Kid));
        }

        [Fact]
        public async Task Les_donnees_du_rapport_mensuel_ne_gardent_que_le_mois_et_les_avis_visibles()
        {
            var (db, svc) = Build();
            var start = new DateTime(2026, 3, 1);
            var end = new DateTime(2026, 4, 1);
            db.TutorBookings.AddRange(
                B(1, 10, Kid, "completed", "Maths", new DateTime(2026, 3, 5), completedAt: new DateTime(2026, 3, 5)),
                B(2, 10, Kid, "completed", "Maths", new DateTime(2026, 3, 9), completedAt: new DateTime(2026, 3, 9)),
                B(3, 10, Kid, "completed", "Physique", new DateTime(2026, 3, 12), completedAt: new DateTime(2026, 3, 12)),
                B(4, 10, Kid, "completed", "Hors mois", new DateTime(2026, 4, 1), completedAt: new DateTime(2026, 4, 1)),
                B(5, 10, Kid, "cancelled", "Annulée", new DateTime(2026, 3, 20), completedAt: new DateTime(2026, 3, 20)));
            db.TutorReviews.AddRange(
                new TutorReview { TutorBookingId = 1, TutorProfileId = 10, StudentUserId = Kid, Rating = 5, Comment = "Super", CreatedAt = new DateTime(2026, 3, 6) },
                new TutorReview { TutorBookingId = 2, TutorProfileId = 10, StudentUserId = Kid, Rating = 3, Comment = null, CreatedAt = new DateTime(2026, 3, 10) },
                new TutorReview { TutorBookingId = 3, TutorProfileId = 10, StudentUserId = Kid, Rating = 1, Comment = "Masqué", IsHiddenByModeration = true, CreatedAt = new DateTime(2026, 3, 11) },
                new TutorReview { TutorBookingId = 4, TutorProfileId = 10, StudentUserId = Kid, Rating = 1, Comment = "Hors mois", CreatedAt = new DateTime(2026, 4, 2) });
            db.SaveChanges();
            var data = await svc.GetCoachingSourceDataAsync(Tutor, start, end);
            Assert.Equal("mars 2026", data.MonthLabel);
            Assert.Equal(new[] { "5/5  Super" }, data.Reviews);
            Assert.Equal(new[] { "Maths", "Physique" }, data.Subjects.OrderBy(s => s));
            Assert.Equal(3, data.SessionsCount);
            Assert.Equal(4.0, data.AverageRating);
        }

        [Fact]
        public async Task Sans_avis_la_moyenne_est_absente()
        {
            var (_, svc) = Build();
            var data = await svc.GetCoachingSourceDataAsync(Tutor, new DateTime(2026, 3, 1), new DateTime(2026, 4, 1));
            Assert.Null(data.AverageRating);
            Assert.Equal(0, data.SessionsCount);
        }

        [Fact]
        public async Task Le_dernier_rapport_de_coaching_est_le_plus_recent_du_tuteur()
        {
            var (db, svc) = Build();
            Assert.Null(await svc.GetLatestCoachingReportAsync(Tutor));
            db.TutorCoachingReports.AddRange(
                new TutorCoachingReport { TutorUserId = Tutor, MonthLabel = "janvier", Content = "a", GeneratedAt = new DateTime(2026, 2, 1) },
                new TutorCoachingReport { TutorUserId = Tutor, MonthLabel = "février", Content = "b", GeneratedAt = new DateTime(2026, 3, 1) },
                new TutorCoachingReport { TutorUserId = Other, MonthLabel = "mars", Content = "c", GeneratedAt = new DateTime(2026, 4, 1) });
            db.SaveChanges();
            Assert.Equal("février", (await svc.GetLatestCoachingReportAsync(Tutor))!.MonthLabel);
            var saved = await svc.SaveCoachingReportAsync(Tutor, "mars", "d");
            Assert.True(saved.Id > 0);
        }
    }
}
