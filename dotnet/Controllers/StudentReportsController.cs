using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;

namespace Backend.Controllers;

/// <summary>
/// Rapport de progression de l'élève (S7-3) et historique filtrable (S7-7).
/// Tout est calculé depuis QuizAttempts, StudySessions, DownloadHistories
/// et DailyScores : aucune valeur d'exemple.
///
/// GET /api/student/reports?period=30
/// GET /api/student/download-history?period=90
/// </summary>
[ApiController]
[Route("api/student")]
[Authorize]
public class StudentReportsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<StudentReportsController> _logger;

    public StudentReportsController(ApplicationDbContext db, ILogger<StudentReportsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private static int NormalizePeriod(int period) =>
        period is 7 or 30 or 90 or 365 ? period : 30;

    [HttpGet("reports")]
    public async Task<IActionResult> GetReport([FromQuery] int period = 30)
    {
        try
        {
            var days = NormalizePeriod(period);
            var userId = User.GetUserId();
            var since = DateTime.UtcNow.Date.AddDays(-days);

            // ── Quiz terminés sur la période, joints à leur matière ──
            var attempts = await _db.QuizAttempts
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.IsCompleted && a.CompletedAt >= since)
                .Join(_db.Quizzes.AsNoTracking(), a => a.QuizId, q => q.Id,
                      (a, q) => new { a.Score, a.CompletedAt, q.Subject })
                .ToListAsync();

            var subjectScores = attempts
                .Where(a => !string.IsNullOrWhiteSpace(a.Subject))
                .GroupBy(a => a.Subject)
                .Select(g => new
                {
                    subject = g.Key,
                    score   = (int)Math.Round(g.Average(x => (double)x.Score)),
                    quizzes = g.Count()
                })
                .OrderByDescending(x => x.score)
                .ToList();

            var avgScore = attempts.Count == 0
                ? (int?)null
                : (int)Math.Round(attempts.Average(a => (double)a.Score));

            // ── Progression hebdomadaire : moyenne réelle par semaine ──
            var weeks = Math.Max(1, (int)Math.Ceiling(days / 7d));
            var weekly = new List<object>();
            for (var w = weeks - 1; w >= 0; w--)
            {
                var start = DateTime.UtcNow.Date.AddDays(-7 * (w + 1));
                var end   = DateTime.UtcNow.Date.AddDays(-7 * w);
                var slice = attempts.Where(a => a.CompletedAt >= start && a.CompletedAt < end).ToList();
                weekly.Add(new
                {
                    weekStart = start,
                    score     = slice.Count == 0 ? (int?)null : (int)Math.Round(slice.Average(a => (double)a.Score)),
                    quizzes   = slice.Count
                });
            }

            var minutes = await _db.StudySessions
                .Where(s => s.UserId == userId && s.CreatedAt >= since)
                .SumAsync(s => (int?)s.Duration) ?? 0;

            var downloads = await _db.DownloadHistories
                .CountAsync(d => d.UserId == userId && d.CreatedAt >= since);

            var successRate = attempts.Count == 0
                ? (int?)null
                : (int)Math.Round(attempts.Count(a => a.Score >= 50) * 100d / attempts.Count);

            return Ok(new
            {
                data = new
                {
                    periodDays     = days,
                    avgScore,
                    quizTotal      = attempts.Count,
                    quizSuccessRate = successRate,
                    studyHours     = Math.Round(minutes / 60d, 1),
                    downloads,
                    subjectScores,
                    weeklyScores   = weekly,
                    bestSubject    = subjectScores.FirstOrDefault()?.subject,
                    weakestSubject = subjectScores.LastOrDefault()?.subject
                },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building student report");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Historique de téléchargements filtré par période (S7-7).</summary>
    [HttpGet("download-history")]
    public async Task<IActionResult> GetDownloadHistory([FromQuery] int period = 30, [FromQuery] int limit = 100)
    {
        try
        {
            var days = NormalizePeriod(period);
            if (limit < 1 || limit > 500) limit = 100;

            var userId = User.GetUserId();
            var since = DateTime.UtcNow.Date.AddDays(-days);

            var items = await _db.DownloadHistories
                .AsNoTracking()
                .Where(d => d.UserId == userId && d.CreatedAt >= since)
                .OrderByDescending(d => d.CreatedAt)
                .Take(limit)
                .Select(d => new
                {
                    d.Id,
                    d.SubjectId,
                    title     = d.Subject != null ? d.Subject.Title : null,
                    category  = d.Subject != null ? d.Subject.Category : null,
                    price     = d.Subject != null ? (decimal?)d.Subject.Price : null,
                    d.CreatedAt
                })
                .ToListAsync();

            var total = await _db.DownloadHistories
                .CountAsync(d => d.UserId == userId && d.CreatedAt >= since);

            return Ok(new { data = items, total, periodDays = days, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting download history");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
