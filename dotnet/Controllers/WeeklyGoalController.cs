using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;

namespace Backend.Controllers;

// ── DTOs ───────────────────────────────────────────────────────────
public class WeeklyGoalRequest
{
    public int? StudyHoursTarget { get; set; }
    public int? QuizTarget { get; set; }
    public int? DownloadsTarget { get; set; }
}

public class WeeklyGoalResponse
{
    public int? StudyHoursTarget { get; set; }
    public int? QuizTarget { get; set; }
    public int? DownloadsTarget { get; set; }

    /// <summary>Réalisé de la semaine (calculé).</summary>
    public double StudyHoursDone { get; set; }
    public int QuizDone { get; set; }
    public int DownloadsDone { get; set; }

    public DateTime WeekStart { get; set; }
}

/// <summary>
/// Objectif hebdomadaire de l'élève.
/// GET    /api/users/me/weekly-goal   → 204 si aucun objectif défini
/// PUT    /api/users/me/weekly-goal
/// DELETE /api/users/me/weekly-goal
/// </summary>
[ApiController]
[Route("api/users/me/weekly-goal")]
[Authorize]
public class WeeklyGoalController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<WeeklyGoalController> _logger;

    public WeeklyGoalController(ApplicationDbContext db, ILogger<WeeklyGoalController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Lundi 00:00 UTC de la semaine en cours.</summary>
    private static DateTime CurrentWeekStart()
    {
        var today = DateTime.UtcNow.Date;
        var delta = ((int)today.DayOfWeek + 6) % 7;   // lundi = 0
        return DateTime.SpecifyKind(today.AddDays(-delta), DateTimeKind.Utc);
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        try
        {
            var userId = User.GetUserId();
            var weekStart = CurrentWeekStart();

            var goal = await _db.WeeklyGoals
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.UserId == userId && g.WeekStart == weekStart);

            // Aucun objectif : 204. Le frontend affiche son état vide,
            // sans jamais inventer de chiffres.
            if (goal == null) return NoContent();

            return Ok(await BuildResponseAsync(goal, userId, weekStart));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting weekly goal");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] WeeklyGoalRequest request)
    {
        try
        {
            if (request.StudyHoursTarget == null && request.QuizTarget == null && request.DownloadsTarget == null)
                return BadRequest(new { error = "Renseignez au moins une cible." });

            foreach (var value in new[] { request.StudyHoursTarget, request.QuizTarget, request.DownloadsTarget })
                if (value is < 0 or > 200)
                    return BadRequest(new { error = "Chaque cible doit être comprise entre 0 et 200." });

            var userId = User.GetUserId();
            var weekStart = CurrentWeekStart();

            var goal = await _db.WeeklyGoals
                .FirstOrDefaultAsync(g => g.UserId == userId && g.WeekStart == weekStart);

            if (goal == null)
            {
                goal = new WeeklyGoal
                {
                    UserId = userId,
                    WeekStart = weekStart,
                    CreatedAt = DateTime.UtcNow
                };
                _db.WeeklyGoals.Add(goal);
            }

            goal.StudyHoursTarget = request.StudyHoursTarget;
            goal.QuizTarget = request.QuizTarget;
            goal.DownloadsTarget = request.DownloadsTarget;
            goal.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(await BuildResponseAsync(goal, userId, weekStart));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving weekly goal");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpDelete]
    public async Task<IActionResult> Delete()
    {
        try
        {
            var userId = User.GetUserId();
            var weekStart = CurrentWeekStart();

            var goal = await _db.WeeklyGoals
                .FirstOrDefaultAsync(g => g.UserId == userId && g.WeekStart == weekStart);

            if (goal != null)
            {
                _db.WeeklyGoals.Remove(goal);
                await _db.SaveChangesAsync();
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting weekly goal");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Réalisé de la semaine, lu dans les tables d'activité :
    /// StudySessions (Duration en minutes), QuizAttempts (CompletedAt)
    /// et DownloadHistory (CreatedAt).
    /// </summary>
    private async Task<WeeklyGoalResponse> BuildResponseAsync(WeeklyGoal goal, int userId, DateTime weekStart)
    {
        var weekEnd = weekStart.AddDays(7);

        var minutes = await _db.StudySessions
            .Where(s => s.UserId == userId && s.CreatedAt >= weekStart && s.CreatedAt < weekEnd)
            .SumAsync(s => (int?)s.Duration) ?? 0;

        var quizDone = await _db.QuizAttempts
            .CountAsync(q => q.UserId == userId
                          && q.IsCompleted
                          && q.CompletedAt >= weekStart
                          && q.CompletedAt < weekEnd);

        var downloadsDone = await _db.DownloadHistories
            .CountAsync(d => d.UserId == userId
                          && d.CreatedAt >= weekStart
                          && d.CreatedAt < weekEnd);

        return new WeeklyGoalResponse
        {
            StudyHoursTarget = goal.StudyHoursTarget,
            QuizTarget = goal.QuizTarget,
            DownloadsTarget = goal.DownloadsTarget,
            StudyHoursDone = Math.Round(minutes / 60d, 1),
            QuizDone = quizDone,
            DownloadsDone = downloadsDone,
            WeekStart = weekStart
        };
    }
}
