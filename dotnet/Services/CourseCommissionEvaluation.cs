using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Lot 9.7b (F5) : évaluation WinAI d'une formation et commission de la
/// plateforme qui en découle, sur le modèle du catalogue
/// (SubjectsController.EvaluateAndStoreCommissionAsync, Module 7).
///
/// Appelée à la soumission (TeacherCourseController.Submit) et, si la
/// formation n'a jamais été évaluée, à l'approbation
/// (AdminCourseController.Approve). Le score (0 à 100) vient du service
/// Python (POST /api/teacher/evaluate-course) ; sa traduction en part
/// plateforme reste au .NET, par la grille de commission du catalogue réglable
/// par l'administrateur (CatalogCommissionService, 18.6), toujours bornée de
/// 10 à 60 % (6.2). Le taux est figé sur la formation : les ventes passées et
/// à venir de cette version gardent ce taux, une nouvelle soumission le
/// recalcule. Échec ou indisponibilité : commission plancher de la grille,
/// explicite dans la justification, jamais de blocage.
/// </summary>
public static class CourseCommissionEvaluation
{
    public const string FallbackJustification =
        "Évaluation WinAI indisponible au moment de la soumission : commission plancher appliquée par défaut.";

    public static async Task EvaluateAndStoreAsync(
        ApplicationDbContext db, IFastApiClient fastApi, ICatalogCommissionService commission, ILogger logger, Course course)
    {
        try
        {
            var sections = await db.CourseSections.AsNoTracking()
                .Where(s => s.CourseId == course.Id)
                .OrderBy(s => s.Position)
                .Select(s => new
                {
                    title = s.Title,
                    lessons = s.Lessons.Where(l => l.IsPublished).OrderBy(l => l.Position)
                        .Select(l => new { title = l.Title, lesson_type = l.LessonType, excerpt = l.ArticleContent })
                        .ToList(),
                })
                .ToListAsync();

            var request = new
            {
                title = course.Title,
                short_description = course.ShortDescription,
                description = course.Description,
                level = course.Level,
                category = course.Category,
                objectives = course.Objectives ?? new List<string>(),
                requirements = course.Requirements ?? new List<string>(),
                has_final_exam = course.FinalExamQuizId != null,
                sections = sections.Select(s => new
                {
                    s.title,
                    lessons = s.lessons.Select(l => new
                    {
                        l.title,
                        l.lesson_type,
                        excerpt = string.IsNullOrEmpty(l.excerpt) ? null : (l.excerpt.Length > 600 ? l.excerpt[..600] : l.excerpt),
                    }),
                }),
            };

            var result = await fastApi.PostAsync<CourseEvaluationResponse>("/api/teacher/evaluate-course", request);
            course.WinAiScore = result?.WinAiScore;
            course.WinAiJustification = result?.WinAiJustification ?? FallbackJustification;
            course.PlatformCommissionRate = await commission.RateForScoreAsync(result?.WinAiScore);
            course.WinAiScoreEvaluatedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Évaluation WinAI de la formation {CourseId} indisponible : commission plancher", course.Id);
            course.WinAiScore = null;
            course.WinAiJustification = FallbackJustification;
            course.PlatformCommissionRate = await commission.RateForScoreAsync(null);
            course.WinAiScoreEvaluatedAt = DateTime.UtcNow;
        }
    }

    public sealed class CourseEvaluationResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("winai_score")]
        public decimal? WinAiScore { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("winai_justification")]
        public string? WinAiJustification { get; set; }
    }
}
