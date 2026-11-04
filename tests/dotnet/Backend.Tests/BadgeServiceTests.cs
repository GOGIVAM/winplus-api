using System;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Moteur de déblocage des badges : critères quiz, certificat et série de
    /// jours actifs, idempotence, badges inactifs ignorés. La série est calculée
    /// à partir de l'horloge système : les activités sont posées à des jours
    /// relatifs à aujourd'hui (midi UTC).
    /// </summary>
    public class BadgeServiceTests
    {
        private const int U = 8001;

        private static (TestApplicationDbContext Db, BadgeService Svc) Build(bool allActive = true)
        {
            var db = TestApplicationDbContext.Create();
            var i = 1;
            foreach (var code in new[] { BadgeCodes.FirstQuizPassed, BadgeCodes.TenQuizzesPassed, BadgeCodes.PerfectQuizScore,
                                         BadgeCodes.FirstCourseCompleted, BadgeCodes.Streak7, BadgeCodes.Streak30 })
                db.BadgeDefinitions.Add(new BadgeDefinition { Id = i++, Code = code, Title = code, IsActive = allActive });
            db.SaveChanges();
            return (db, new BadgeService(db, NullLogger<BadgeService>.Instance));
        }

        private static string[] Unlocked(TestApplicationDbContext db) =>
            (from ub in db.UserBadges join d in db.BadgeDefinitions on ub.BadgeDefinitionId equals d.Id where ub.UserId == U orderby d.Code select d.Code).ToArray();

        private static void Attempts(TestApplicationDbContext db, int count, decimal score, bool passed)
        {
            for (var k = 0; k < count; k++)
                db.QuizAttempts.Add(new QuizAttempt { UserId = U, QuizId = 1, Score = score, Passed = passed, AttemptNumber = k + 1, CompletedAt = new DateTime(2020, 1, 1) });
            db.SaveChanges();
        }

        [Fact]
        public async Task Sans_activite_aucun_badge_n_est_debloque()
        {
            var (db, svc) = Build();
            await svc.EvaluateForUserAsync(U);
            Assert.Empty(Unlocked(db));
        }

        [Fact]
        public async Task Sans_catalogue_actif_rien_n_est_debloque()
        {
            var (db, svc) = Build(allActive: false);
            Attempts(db, 1, 100m, true);
            await svc.EvaluateForUserAsync(U);
            Assert.Empty(Unlocked(db));
        }

        [Fact]
        public async Task Un_quiz_reussi_debloque_le_premier_badge_seulement()
        {
            var (db, svc) = Build();
            Attempts(db, 1, 70m, true);
            await svc.EvaluateForUserAsync(U);
            Assert.Equal(new[] { BadgeCodes.FirstQuizPassed }, Unlocked(db));
        }

        [Fact]
        public async Task Un_quiz_echoue_ne_compte_pas_comme_reussi()
        {
            var (db, svc) = Build();
            Attempts(db, 3, 30m, false);
            await svc.EvaluateForUserAsync(U);
            Assert.Empty(Unlocked(db));
        }

        [Fact]
        public async Task Dix_quiz_reussis_et_un_score_parfait_debloquent_trois_badges()
        {
            var (db, svc) = Build();
            Attempts(db, 10, 100m, true);
            await svc.EvaluateForUserAsync(U);
            Assert.Equal(new[] { BadgeCodes.FirstQuizPassed, BadgeCodes.PerfectQuizScore, BadgeCodes.TenQuizzesPassed }.OrderBy(c => c), Unlocked(db));
        }

        [Fact]
        public async Task Neuf_quiz_reussis_ne_donnent_pas_le_badge_des_dix()
        {
            var (db, svc) = Build();
            Attempts(db, 9, 80m, true);
            await svc.EvaluateForUserAsync(U);
            Assert.DoesNotContain(BadgeCodes.TenQuizzesPassed, Unlocked(db));
        }

        [Fact]
        public async Task Un_certificat_de_formation_debloque_le_badge_formation()
        {
            var (db, svc) = Build();
            db.CourseCertificates.Add(new CourseCertificate { CourseId = 1, UserId = U, VerificationCode = "ABC" });
            db.SaveChanges();
            await svc.EvaluateForUserAsync(U);
            Assert.Equal(new[] { BadgeCodes.FirstCourseCompleted }, Unlocked(db));
        }

        [Fact]
        public async Task L_evaluation_est_idempotente()
        {
            var (db, svc) = Build();
            Attempts(db, 1, 70m, true);
            await svc.EvaluateForUserAsync(U);
            await svc.EvaluateForUserAsync(U);
            Assert.Single(db.UserBadges.Where(b => b.UserId == U));
        }

        [Fact]
        public async Task Sept_jours_d_activite_consecutifs_dont_aujourd_hui_debloquent_la_serie_de_7()
        {
            var (db, svc) = Build();
            var today = DateTime.UtcNow.Date;
            for (var k = 0; k < 7; k++) db.StudySessions.Add(new StudySession { UserId = U, CreatedAt = today.AddDays(-k).AddHours(12) });
            db.SaveChanges();
            await svc.EvaluateForUserAsync(U);
            Assert.Equal(new[] { BadgeCodes.Streak7 }, Unlocked(db));
        }

        [Fact]
        public async Task Un_trou_dans_la_serie_la_ramene_sous_le_seuil()
        {
            var (db, svc) = Build();
            var today = DateTime.UtcNow.Date;
            foreach (var k in new[] { 0, 1, 2, 4, 5, 6, 7 }) db.StudySessions.Add(new StudySession { UserId = U, CreatedAt = today.AddDays(-k).AddHours(12) });
            db.SaveChanges();
            await svc.EvaluateForUserAsync(U);
            Assert.DoesNotContain(BadgeCodes.Streak7, Unlocked(db));
        }

        [Fact]
        public async Task La_serie_peut_se_terminer_hier_sans_etre_rompue()
        {
            var (db, svc) = Build();
            var today = DateTime.UtcNow.Date;
            for (var k = 1; k <= 7; k++) db.DownloadHistories.Add(new DownloadHistory { UserId = U, CreatedAt = today.AddDays(-k).AddHours(12) });
            db.SaveChanges();
            await svc.EvaluateForUserAsync(U);
            Assert.Contains(BadgeCodes.Streak7, Unlocked(db));
        }

        [Fact]
        public async Task Le_badge_d_un_utilisateur_n_est_pas_attribue_a_un_autre()
        {
            var (db, svc) = Build();
            Attempts(db, 1, 70m, true);
            await svc.EvaluateForUserAsync(U + 1);
            Assert.Empty(db.UserBadges);
        }
    }
}
