using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Services;

namespace Backend.Controllers;

public class GenerateAlbumsRequest
{
    /// <summary>Ex: "2024-2025", "2024/2025", "24-25"  normalisé par YearlyAlbumService.</summary>
    public string SchoolYear { get; set; } = string.Empty;

    /// <summary>true : aperçu pour un seul parent test, rien n'est écrit en base.</summary>
    public bool DryRun { get; set; } = true;
}

/// <summary>
/// Déclenchement admin de l'album de fin d'année (YearlyAlbumService).
/// Première édition volontairement manuelle (pas de BackgroundService
/// planifié comme WeeklyParentReportService/MonthlyPortfolioService) :
/// l'admin choisit l'année scolaire et vérifie un aperçu avant d'écrire pour
/// tous les parents.
///
/// POST /api/admin/albums/generate
/// </summary>
[ApiController]
[Route("api/admin/albums")]
[Authorize(Policy = "AdminOnly")]
public class AdminAlbumsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IYearlyAlbumService _albumService;
    private readonly ILogger<AdminAlbumsController> _logger;

    public AdminAlbumsController(ApplicationDbContext db, IYearlyAlbumService albumService, ILogger<AdminAlbumsController> logger)
    {
        _db = db;
        _albumService = albumService;
        _logger = logger;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateAlbumsRequest request, CancellationToken ct)
    {
        if (YearlyAlbumService.NormalizeSchoolYear(request.SchoolYear) == null)
            return BadRequest(new { error = $"Année scolaire mal formée : « {request.SchoolYear} ». Format attendu : 2024-2025." });

        // Parents éligibles : au moins un enfant actuellement lié (accepted).
        var eligibleParentIds = await _db.ParentStudentLinks.AsNoTracking()
            .Where(l => l.Status == "accepted")
            .Select(l => l.ParentId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(ct);

        if (eligibleParentIds.Count == 0)
            return Ok(new { parentsProcessed = 0, successCount = 0, edgeCases = Array.Empty<object>(), preview = (object?)null });

        if (request.DryRun)
        {
            // "1 parent test" : le premier parent éligible, déterministe plutôt
            // qu'un choix arbitraire à chaque appel.
            var testParentId = eligibleParentIds[0];
            var result = await _albumService.GenerateAlbumForParentAsync(testParentId, request.SchoolYear, persist: false, ct);

            return Ok(new
            {
                parentsProcessed = 1,
                successCount = result.Children.Count(c => c.Success) > 0 ? 1 : 0,
                edgeCases = result.Children.Where(c => !c.Success)
                    .Select(c => new { parentId = testParentId, childId = c.ChildId, childName = c.ChildName, reason = c.SkipReason }),
                preview = new
                {
                    parentId = testParentId,
                    children = result.Children.Select(c => new
                    {
                        c.ChildId,
                        c.ChildName,
                        c.Success,
                        c.SkipReason,
                        content = c.Content,
                    }),
                },
            });
        }

        var successCount = 0;
        var edgeCases = new List<object>();

        foreach (var parentId in eligibleParentIds)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var result = await _albumService.GenerateAlbumForParentAsync(parentId, request.SchoolYear, persist: true, ct);
                if (result.Children.Any(c => c.Success)) successCount++;

                foreach (var child in result.Children.Where(c => !c.Success))
                    edgeCases.Add(new { parentId, childId = child.ChildId, childName = child.ChildName, reason = child.SkipReason });

                foreach (var child in result.Children.Where(c => c.Success && c.Content != null &&
                    c.Content.TopContents.Count == 0 && c.Content.IntensityWeeks.Count == 0))
                    edgeCases.Add(new { parentId, childId = child.ChildId, childName = child.ChildName, reason = "Aucune activité enregistrée sur la période." });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Album generation failed for parent {ParentId}", parentId);
                edgeCases.Add(new { parentId, childId = (int?)null, childName = (string?)null, reason = "Erreur lors du calcul pour ce parent." });
            }
        }

        _logger.LogInformation("Yearly albums generated: {Success}/{Total} parents, {EdgeCases} edge case(s).",
            successCount, eligibleParentIds.Count, edgeCases.Count);

        return Ok(new
        {
            parentsProcessed = eligibleParentIds.Count,
            successCount,
            edgeCases,
            preview = (object?)null,
        });
    }
}
