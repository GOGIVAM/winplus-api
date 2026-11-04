using System;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Models.Entities;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Règles d'achèvement d'une leçon : temps de visionnage crédité plafonné
    /// (lecture accélérée 2x plus une marge), vidéo regardée à 80 %, quiz réussi,
    /// temps minimal de lecture pour les autres leçons. Horloge passée en
    /// argument : toutes les dates sont fixes.
    /// </summary>
    public class LessonCompletionRulesTests
    {
        private static readonly DateTime T0 = new(2026, 5, 1, 10, 0, 0);
        private const int U = 1;

        [Theory]
        [InlineData(100, 5000, 10, 140)]   // plafond : 100 + 10*2 + 20
        [InlineData(100, 110, 600, 110)]   // déclaré sous le plafond
        [InlineData(100, 50, 60, 100)]     // jamais de recul
        [InlineData(0, -5, 60, 0)]         // déclaré négatif
        [InlineData(0, 40, -30, 20)]       // horloge à rebours : temps écoulé nul, marge de 20 s seulement
        public void Le_temps_credite_est_plafonne_et_ne_recule_jamais(int previous, int declared, int elapsedSeconds, int expected)
        {
            var credited = LessonCompletionRules.CreditedWatchTime(previous, declared, T0, T0.AddSeconds(elapsedSeconds));
            Assert.Equal(expected, credited);
        }

        private static CourseLesson Lesson(string type, int? duration = null, int? quiz = null) =>
            new() { LessonType = type, VideoDurationSec = duration, QuizId = quiz, Section = null! };

        private static LessonProgress Prog(int watch = 0, double openedSecondsAgo = 0) =>
            new() { UserId = U, CreatedAt = T0.AddSeconds(-openedSecondsAgo), WatchTimeSec = watch };

        [Fact]
        public async Task Une_video_avec_duree_exige_80_pour_cent_de_visionnage()
        {
            using var db = TestApplicationDbContext.Create();
            var (refusal, _) = await LessonCompletionRules.CheckAsync(db, U, Lesson("video", 100), Prog(watch: 79), T0);
            Assert.NotNull(refusal);
            var (ok, _) = await LessonCompletionRules.CheckAsync(db, U, Lesson("video", 100), Prog(watch: 80), T0);
            Assert.Null(ok);
        }

        [Fact]
        public async Task Une_video_sans_duree_exige_trente_secondes_depuis_l_ouverture()
        {
            using var db = TestApplicationDbContext.Create();
            Assert.NotNull((await LessonCompletionRules.CheckAsync(db, U, Lesson("Video"), Prog(openedSecondsAgo: 29), T0)).Refusal);
            Assert.Null((await LessonCompletionRules.CheckAsync(db, U, Lesson(" VIDEO "), Prog(openedSecondsAgo: 30), T0)).Refusal);
        }

        [Fact]
        public async Task Une_lecture_exige_vingt_secondes()
        {
            using var db = TestApplicationDbContext.Create();
            Assert.NotNull((await LessonCompletionRules.CheckAsync(db, U, Lesson("article"), Prog(openedSecondsAgo: 19), T0)).Refusal);
            Assert.Null((await LessonCompletionRules.CheckAsync(db, U, Lesson("article"), Prog(openedSecondsAgo: 20), T0)).Refusal);
        }

        [Fact]
        public async Task Une_lecon_quiz_sans_tentative_est_refusee()
        {
            using var db = TestApplicationDbContext.Create();
            db.Quizzes.Add(new Quiz { Id = 5, Title = "Q", Subject = "M", PassingScore = 70 });
            db.SaveChanges();
            var (refusal, score) = await LessonCompletionRules.CheckAsync(db, U, Lesson("quiz", quiz: 5), Prog(), T0);
            Assert.NotNull(refusal);
            Assert.Null(score);
        }

        [Fact]
        public async Task Une_lecon_quiz_sous_le_seuil_est_refusee_avec_le_meilleur_score()
        {
            using var db = TestApplicationDbContext.Create();
            db.Quizzes.Add(new Quiz { Id = 5, Title = "Q", Subject = "M", PassingScore = 70 });
            db.QuizAttempts.AddRange(
                new QuizAttempt { UserId = U, QuizId = 5, Score = 40m, CompletedAt = T0 },
                new QuizAttempt { UserId = U, QuizId = 5, Score = 69m, CompletedAt = T0 });
            db.SaveChanges();
            var (refusal, score) = await LessonCompletionRules.CheckAsync(db, U, Lesson("quiz", quiz: 5), Prog(), T0);
            Assert.Contains("69", refusal);
            Assert.Equal(69, score);
        }

        [Fact]
        public async Task Une_lecon_quiz_reussie_est_acceptee_au_seuil_ou_par_drapeau_et_ignore_les_autres_eleves()
        {
            using var db = TestApplicationDbContext.Create();
            db.Quizzes.Add(new Quiz { Id = 5, Title = "Q", Subject = "M", PassingScore = 70 });
            db.QuizAttempts.Add(new QuizAttempt { UserId = U, QuizId = 5, Score = 70m, CompletedAt = T0 });
            db.QuizAttempts.Add(new QuizAttempt { UserId = 2, QuizId = 5, Score = 100m, Passed = true, CompletedAt = T0 });
            db.SaveChanges();
            var (refusal, score) = await LessonCompletionRules.CheckAsync(db, U, Lesson("quiz", quiz: 5), Prog(), T0);
            Assert.Null(refusal);
            Assert.Equal(70, score);
            var (r2, _) = await LessonCompletionRules.CheckAsync(db, 3, Lesson("quiz", quiz: 5), Prog(), T0);
            Assert.NotNull(r2);
        }
    }
}
