using System;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Évaluation WinAI d'une formation et commission plateforme qui en découle
    /// (grille réglable bornée de 10 à 60 %), avec commission plancher en cas
    /// d'échec ou de réponse vide. Service Python simulé par Moq.
    /// </summary>
    public class CourseCommissionEvaluationTests
    {
        private static (TestApplicationDbContext Db, CatalogCommissionService Commission, Mock<IFastApiClient> Api) Build()
        {
            var db = TestApplicationDbContext.Create();
            return (db, new CatalogCommissionService(db), new Mock<IFastApiClient>());
        }

        private static Course NewCourse() => new() { Id = 1, Title = "Algèbre", Instructor = null! };

        [Fact]
        public async Task Un_score_reussi_fixe_la_commission_dans_la_grille_par_defaut()
        {
            var (db, commission, api) = Build();
            api.Setup(a => a.PostAsync<CourseCommissionEvaluation.CourseEvaluationResponse>("/api/teacher/evaluate-course", It.IsAny<object>(), null))
                .ReturnsAsync(new CourseCommissionEvaluation.CourseEvaluationResponse { WinAiScore = 50m, WinAiJustification = "Solide" });
            var course = NewCourse();
            await CourseCommissionEvaluation.EvaluateAndStoreAsync(db, api.Object, commission, NullLogger.Instance, course);
            Assert.Equal(50m, course.WinAiScore);
            Assert.Equal("Solide", course.WinAiJustification);
            Assert.Equal(0.35m, course.PlatformCommissionRate);
            Assert.NotNull(course.WinAiScoreEvaluatedAt);
        }

        [Fact]
        public async Task Une_reponse_sans_justification_utilise_le_texte_de_repli()
        {
            var (db, commission, api) = Build();
            api.Setup(a => a.PostAsync<CourseCommissionEvaluation.CourseEvaluationResponse>(It.IsAny<string>(), It.IsAny<object>(), null))
                .ReturnsAsync(new CourseCommissionEvaluation.CourseEvaluationResponse { WinAiScore = 100m });
            var course = NewCourse();
            await CourseCommissionEvaluation.EvaluateAndStoreAsync(db, api.Object, commission, NullLogger.Instance, course);
            Assert.Equal(CourseCommissionEvaluation.FallbackJustification, course.WinAiJustification);
            Assert.Equal(0.60m, course.PlatformCommissionRate);
        }

        [Fact]
        public async Task Une_erreur_du_service_applique_la_commission_plancher_sans_bloquer()
        {
            var (db, commission, api) = Build();
            api.Setup(a => a.PostAsync<CourseCommissionEvaluation.CourseEvaluationResponse>(It.IsAny<string>(), It.IsAny<object>(), null))
                .ThrowsAsync(new InvalidOperationException("Python indisponible"));
            var course = NewCourse();
            course.WinAiScore = 90m;
            await CourseCommissionEvaluation.EvaluateAndStoreAsync(db, api.Object, commission, NullLogger.Instance, course);
            Assert.Null(course.WinAiScore);
            Assert.Equal(CourseCommissionEvaluation.FallbackJustification, course.WinAiJustification);
            Assert.Equal(0.10m, course.PlatformCommissionRate);
            Assert.NotNull(course.WinAiScoreEvaluatedAt);
        }

        [Fact]
        public async Task Une_reponse_nulle_donne_aussi_la_commission_plancher()
        {
            var (db, commission, api) = Build();
            api.Setup(a => a.PostAsync<CourseCommissionEvaluation.CourseEvaluationResponse>(It.IsAny<string>(), It.IsAny<object>(), null))
                .ReturnsAsync((CourseCommissionEvaluation.CourseEvaluationResponse?)null);
            var course = NewCourse();
            await CourseCommissionEvaluation.EvaluateAndStoreAsync(db, api.Object, commission, NullLogger.Instance, course);
            Assert.Null(course.WinAiScore);
            Assert.Equal(0.10m, course.PlatformCommissionRate);
        }

        [Fact]
        public async Task La_grille_modifiee_par_l_administrateur_est_bornee_et_ordonnee()
        {
            var (_, commission, _) = Build();
            var s = await commission.UpdateSettingsAsync(90m, 5m, adminUserId: 3);
            Assert.Equal(10m, s.MinRatePercent);
            Assert.Equal(60m, s.MaxRatePercent);
            Assert.Equal(3, s.UpdatedByUserId);
        }

        [Fact]
        public async Task Les_scores_hors_bornes_sont_ecretes()
        {
            var (_, commission, _) = Build();
            await commission.UpdateSettingsAsync(20m, 40m, 1);
            Assert.Equal(0.20m, await commission.RateForScoreAsync(-50m));
            Assert.Equal(0.40m, await commission.RateForScoreAsync(500m));
            Assert.Equal(0.30m, await commission.RateForScoreAsync(50m));
            Assert.Equal(0.20m, await commission.RateForScoreAsync(null));
        }
    }
}
