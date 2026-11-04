using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Déblocage progressif des sections d'une formation (drip content) :
    /// règle « immediate », « delay_days » (délai après l'inscription) et
    /// « min_score » (meilleur score sur les quiz de la section précédente).
    /// Le service lit l'horloge système : les dates sont donc prises très loin
    /// dans le passé ou le futur pour que le résultat ne dépende pas de l'heure.
    /// </summary>
    public class CourseAccessServiceTests
    {
        private const int User = 7001, Course = 7002;

        private static CourseSection Section(int id, string rule = "immediate", int? delay = null, int? min = null, params int[] quizIds)
        {
            var s = new CourseSection { Id = id, CourseId = Course, UnlockRule = rule, DelayDays = delay, MinScore = min };
            foreach (var q in quizIds) s.Lessons.Add(new CourseLesson { Id = id * 100 + q, QuizId = q });
            return s;
        }

        private static void Attempt(TestApplicationDbContext db, int quizId, decimal score)
        {
            db.QuizAttempts.Add(new QuizAttempt { UserId = User, QuizId = quizId, Score = score, CompletedAt = new DateTime(2026, 1, 1) });
            db.SaveChanges();
        }

        [Fact]
        public async Task Une_section_immediate_est_toujours_debloquee_sans_raison()
        {
            using var db = TestApplicationDbContext.Create();
            var res = await new CourseAccessService(db).ComputeSectionAccessAsync(User, Course, DateTime.UtcNow, new List<CourseSection> { Section(1) });
            Assert.True(res[1].Unlocked);
            Assert.Null(res[1].Reason);
        }

        [Fact]
        public async Task Un_delai_ecoule_debloque_la_section()
        {
            using var db = TestApplicationDbContext.Create();
            var res = await new CourseAccessService(db).ComputeSectionAccessAsync(User, Course, new DateTime(2000, 1, 1), new List<CourseSection> { Section(1, "delay_days", 5) });
            Assert.True(res[1].Unlocked);
            Assert.Null(res[1].Reason);
        }

        [Fact]
        public async Task Un_delai_non_ecoule_verrouille_la_section_avec_un_message_au_pluriel()
        {
            using var db = TestApplicationDbContext.Create();
            var res = await new CourseAccessService(db).ComputeSectionAccessAsync(User, Course, DateTime.UtcNow.AddDays(36500), new List<CourseSection> { Section(1, "delay_days", 5) });
            Assert.False(res[1].Unlocked);
            Assert.StartsWith("Disponible dans ", res[1].Reason);
            Assert.EndsWith(" jours", res[1].Reason);
        }

        [Fact]
        public async Task Une_regle_delai_sans_nombre_de_jours_retombe_sur_deblocage()
        {
            using var db = TestApplicationDbContext.Create();
            var res = await new CourseAccessService(db).ComputeSectionAccessAsync(User, Course, DateTime.UtcNow.AddDays(100), new List<CourseSection> { Section(1, "delay_days", null) });
            Assert.True(res[1].Unlocked);
        }

        [Fact]
        public async Task Le_score_minimum_sans_quiz_sur_la_section_precedente_ne_bloque_pas()
        {
            using var db = TestApplicationDbContext.Create();
            var sections = new List<CourseSection> { Section(1), Section(2, "min_score", min: 80) };
            var res = await new CourseAccessService(db).ComputeSectionAccessAsync(User, Course, DateTime.UtcNow, sections);
            Assert.True(res[2].Unlocked);
        }

        [Fact]
        public async Task Le_score_minimum_en_premiere_section_ne_bloque_pas()
        {
            using var db = TestApplicationDbContext.Create();
            var res = await new CourseAccessService(db).ComputeSectionAccessAsync(User, Course, DateTime.UtcNow, new List<CourseSection> { Section(1, "min_score", min: 80) });
            Assert.True(res[1].Unlocked);
        }

        [Fact]
        public async Task Le_score_minimum_sans_tentative_verrouille_la_section()
        {
            using var db = TestApplicationDbContext.Create();
            var sections = new List<CourseSection> { Section(1, quizIds: 11), Section(2, "min_score", min: 80) };
            var res = await new CourseAccessService(db).ComputeSectionAccessAsync(User, Course, DateTime.UtcNow, sections);
            Assert.False(res[2].Unlocked);
            Assert.Contains("80%", res[2].Reason);
        }

        [Fact]
        public async Task Le_meilleur_score_atteignant_le_seuil_debloque_et_le_seuil_est_inclusif()
        {
            using var db = TestApplicationDbContext.Create();
            Attempt(db, 11, 40m);
            Attempt(db, 11, 80m);
            var sections = new List<CourseSection> { Section(1, quizIds: 11), Section(2, "min_score", min: 80) };
            var res = await new CourseAccessService(db).ComputeSectionAccessAsync(User, Course, DateTime.UtcNow, sections);
            Assert.True(res[2].Unlocked);
        }

        [Fact]
        public async Task Un_score_sous_le_seuil_ou_celui_d_un_autre_quiz_ne_debloque_pas()
        {
            using var db = TestApplicationDbContext.Create();
            Attempt(db, 11, 79m);
            Attempt(db, 99, 100m);
            var sections = new List<CourseSection> { Section(1, quizIds: 11), Section(2, "min_score", min: 80) };
            var res = await new CourseAccessService(db).ComputeSectionAccessAsync(User, Course, DateTime.UtcNow, sections);
            Assert.False(res[2].Unlocked);
        }
    }
}
