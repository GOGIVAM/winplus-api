using Backend.Data;
using Backend.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Backend.Controllers;

/// <summary>
/// Lot 10.4 : chiffres réels d'un examen ou d'un concours pour ses pages
/// publiques (/concours/:type et ses pages matière).
///
///   GET /api/exams/stats/{examType}
///
/// Anonyme, en lecture seule, sans donnée personnelle, mis en cache dix
/// minutes. Les types saisis librement sont regroupés comme le catalogue
/// (<see cref="ExamTypeGroups"/>) : « IUT/Concours d'entrée – … » compte dans
/// « iut ». Seules les épreuves publiées et rattachées à une fiche du
/// catalogue publiée sont comptées : chaque épreuve listée a une page
/// /subject/:id consultable.
/// </summary>
[ApiController]
[AllowAnonymous]
[Produces("application/json")]
public class ExamStatsController : ControllerBase
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ExamStatsController> _logger;

    public ExamStatsController(ApplicationDbContext db, IMemoryCache cache, ILogger<ExamStatsController> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public sealed record CategoryCount(string Name, int Count);

    public sealed record ExamSummary(
        int Id, int SubjectId, string Title, string Category, int Year,
        bool IsFree, decimal Price, bool HasCorrection, DateTime? CreatedAt);

    public sealed record ExamTypeStats(
        string ExamType, int TotalExams, int WithCorrection, int FreeExams,
        IReadOnlyList<CategoryCount> Categories, IReadOnlyList<int> Years,
        IReadOnlyList<ExamSummary> Exams, DateTime GeneratedAt);

    [HttpGet("api/exams/stats/{examType}")]
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Get(string examType)
    {
        var key = ExamTypeGroups.Key(examType);
        if (key.Length == 0 || key.Length > 60) return BadRequest(new { success = false, error = "Type d'examen invalide." });

        try
        {
            var stats = await _cache.GetOrCreateAsync($"exam-stats:{key}", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                return ComputeAsync(key);
            });
            return Ok(new { success = true, data = stats });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur GET /exams/stats/{Type}", examType);
            return Problem("Statistiques indisponibles.");
        }
    }

    internal async Task<ExamTypeStats> ComputeAsync(string key)
    {
        var rows = await _db.Exams.AsNoTracking()
            .Where(e => !e.IsDeleted && e.IsPublished && e.ExamType != null && e.SubjectId != null
                        && e.SubjectReference != null && !e.SubjectReference.IsDeleted && e.SubjectReference.IsPublished)
            .Select(e => new
            {
                e.Id,
                SubjectId = e.SubjectId!.Value,
                e.Title,
                e.ExamType,
                e.Category,
                e.Year,
                Price = e.SubjectReference!.Price,
                HasCorrection = e.CorrectionUrl != null && e.CorrectionUrl != "",
                e.CreatedAt,
            })
            .ToListAsync();

        var exams = rows
            .Where(r => ExamTypeGroups.Matches(r.ExamType, key))
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Select(r => new ExamSummary(r.Id, r.SubjectId, r.Title, (r.Category ?? string.Empty).Trim(), r.Year,
                r.Price == 0, r.Price, r.HasCorrection, r.CreatedAt))
            .ToList();

        var categories = exams
            .Where(e => e.Category.Length > 0)
            .GroupBy(e => e.Category)
            .Select(g => new CategoryCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count).ThenBy(c => c.Name)
            .ToList();

        return new ExamTypeStats(
            key,
            exams.Count,
            exams.Count(e => e.HasCorrection),
            exams.Count(e => e.IsFree),
            categories,
            exams.Select(e => e.Year).Where(y => y > 0).Distinct().OrderByDescending(y => y).ToList(),
            exams,
            DateTime.UtcNow);
    }
}
