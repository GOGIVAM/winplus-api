using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public interface ICourseAccessService
{
    /// <summary>
    /// Calcule l'accès élève à chaque section d'une formation (drip content,
    /// prompt_prof.md Module 5B). Voir implémentation pour le détail des règles.
    /// </summary>
    Task<Dictionary<int, (bool Unlocked, string? Reason)>> ComputeSectionAccessAsync(
        int userId, int courseId, DateTime enrolledAt, List<CourseSection> orderedSections);
}

public class CourseAccessService : ICourseAccessService
{
    private readonly ApplicationDbContext _db;

    public CourseAccessService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Dictionary<int, (bool Unlocked, string? Reason)>> ComputeSectionAccessAsync(
        int userId, int courseId, DateTime enrolledAt, List<CourseSection> orderedSections)
    {
        var result = new Dictionary<int, (bool, string?)>();

        for (var i = 0; i < orderedSections.Count; i++)
        {
            var section = orderedSections[i];
            switch (section.UnlockRule)
            {
                case "delay_days" when section.DelayDays is int days:
                {
                    var availableAt = enrolledAt.AddDays(days);
                    var unlocked = DateTime.UtcNow >= availableAt;
                    var remaining = Math.Max(0, (int)Math.Ceiling((availableAt - DateTime.UtcNow).TotalDays));
                    result[section.Id] = (unlocked, unlocked ? null : $"Disponible dans {remaining} jour{(remaining > 1 ? "s" : "")}");
                    break;
                }
                case "min_score" when section.MinScore is int minScore:
                {
                    var previous = i > 0 ? orderedSections[i - 1] : null;
                    var quizIds = previous?.Lessons.Where(l => l.QuizId.HasValue).Select(l => l.QuizId!.Value).ToList() ?? new List<int>();
                    if (quizIds.Count == 0)
                    {
                        result[section.Id] = (true, null);
                        break;
                    }
                    var bestScore = await _db.QuizAttempts.AsNoTracking()
                        .Where(a => a.UserId == userId && quizIds.Contains(a.QuizId))
                        .Select(a => (decimal?)a.Score)
                        .MaxAsync();
                    var unlocked = bestScore.HasValue && bestScore.Value >= minScore;
                    result[section.Id] = (unlocked, unlocked ? null : $"Complète le quiz précédent avec ≥ {minScore}%");
                    break;
                }
                default:
                    result[section.Id] = (true, null);
                    break;
            }
        }

        return result;
    }
}
