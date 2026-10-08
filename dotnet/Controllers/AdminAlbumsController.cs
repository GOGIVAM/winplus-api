using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Services;
using Backend.Extensions;

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

    // ── Module 33 (lot 7) : déclenchement hybride ───────────────────────────
    //
    // Le déclenchement manuel ci-dessus (POST /generate) reste utilisable
    // pour un test ponctuel. Ce qui suit ajoute le volet automatique : une
    // date de fin d'année scolaire configurable par un administrateur, un
    // service planifié qui génère l'aperçu pour TOUS les parents à cette
    // date, et une confirmation manuelle explicite avant toute diffusion
    // réelle (jamais automatique).

    public class SetAlbumScheduleRequest
    {
        /// <summary>Ex: "2024-2025". Normalisé par YearlyAlbumService.</summary>
        public string SchoolYear { get; set; } = string.Empty;
        /// <summary>Date (UTC) de déclenchement automatique de l'aperçu.</summary>
        public DateTime TriggerDate { get; set; }
    }

    /// <summary>Liste les paramétrages d'album (historique + à venir), les plus récents en premier.</summary>
    [HttpGet("schedule")]
    public async Task<IActionResult> GetSchedules()
    {
        var schedules = await _db.AlbumSchedules.AsNoTracking()
            .OrderByDescending(s => s.TriggerDate)
            .Select(s => new
            {
                s.Id,
                s.SchoolYear,
                s.TriggerDate,
                s.Status,
                s.GeneratedAt,
                s.DispatchedAt,
            })
            .ToListAsync();
        return Ok(schedules);
    }

    /// <summary>
    /// Crée ou met à jour la date de déclenchement pour une année scolaire.
    /// Ne modifie jamais un paramétrage déjà passé en PreviewGenerated/Dispatched :
    /// il faut créer une nouvelle ligne pour une nouvelle campagne plutôt que
    /// de réécrire l'historique d'une diffusion déjà effectuée.
    /// </summary>
    [HttpPost("schedule")]
    public async Task<IActionResult> SetSchedule([FromBody] SetAlbumScheduleRequest request)
    {
        if (YearlyAlbumService.NormalizeSchoolYear(request.SchoolYear) == null)
            return BadRequest(new { error = $"Année scolaire mal formée : « {request.SchoolYear} ». Format attendu : 2024-2025." });

        var existing = await _db.AlbumSchedules.FirstOrDefaultAsync(s => s.SchoolYear == request.SchoolYear);
        if (existing != null)
        {
            if (existing.Status != "Pending")
                return Conflict(new { error = $"Le paramétrage de {request.SchoolYear} est déjà « {existing.Status} » : il ne peut plus être modifié." });

            existing.TriggerDate = request.TriggerDate;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new { id = existing.Id, existing.SchoolYear, existing.TriggerDate, existing.Status });
        }

        var schedule = new Models.Entities.AlbumSchedule
        {
            SchoolYear = request.SchoolYear,
            TriggerDate = request.TriggerDate,
            Status = "Pending",
            CreatedByUserId = User.GetUserId(),
        };
        _db.AlbumSchedules.Add(schedule);
        await _db.SaveChangesAsync();
        return Ok(new { schedule.Id, schedule.SchoolYear, schedule.TriggerDate, schedule.Status });
    }

    /// <summary>
    /// Aperçu généré automatiquement pour une année scolaire, à consulter
    /// avant de confirmer (ou non) la diffusion réelle.
    /// </summary>
    [HttpGet("preview")]
    public async Task<IActionResult> GetPreview([FromQuery] string schoolYear)
    {
        var canonical = YearlyAlbumService.NormalizeSchoolYear(schoolYear);
        if (canonical == null)
            return BadRequest(new { error = $"Année scolaire mal formée : « {schoolYear} »." });
        var canonicalLabel = $"{canonical.Value.Start}-{canonical.Value.End}";

        var reports = await _db.ParentReports.AsNoTracking()
            .Where(r => r.ReportType == "AlbumAnnuel" && r.IsPreviewPending)
            .Include(r => r.Parent)
            .Include(r => r.Child)
            .ToListAsync();

        // Filtré en mémoire : SchoolYear vit dans le JSON Content, pas en colonne.
        var filtered = reports.Where(r =>
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(r.Content ?? "{}");
                return doc.RootElement.TryGetProperty("SchoolYear", out var y) && y.GetString() == canonicalLabel;
            }
            catch { return false; }
        }).ToList();

        var items = filtered.Select(r => new
        {
            r.Id,
            parentId = r.ParentId,
            parentName = r.Parent != null ? $"{r.Parent.FirstName} {r.Parent.LastName}".Trim() : null,
            childId = r.ChildId,
            childName = r.Child != null ? $"{r.Child.FirstName} {r.Child.LastName}".Trim() : null,
            r.CreatedAt,
        });

        return Ok(new { schoolYear = canonicalLabel, count = filtered.Count, items });
    }

    public class DispatchAlbumsRequest
    {
        public string SchoolYear { get; set; } = string.Empty;
    }

    /// <summary>
    /// Confirmation explicite de la diffusion réelle : les aperçus en attente
    /// deviennent visibles pour tous les parents concernés. Action manuelle
    /// obligatoire (décision §5.5.P), jamais automatique, et idempotente :
    /// une seconde confirmation sur une campagne déjà diffusée est refusée
    /// plutôt que de ré-envoyer.
    /// </summary>
    [HttpPost("dispatch")]
    public async Task<IActionResult> Dispatch([FromBody] DispatchAlbumsRequest request)
    {
        var canonical = YearlyAlbumService.NormalizeSchoolYear(request.SchoolYear);
        if (canonical == null)
            return BadRequest(new { error = $"Année scolaire mal formée : « {request.SchoolYear} »." });
        var canonicalLabel = $"{canonical.Value.Start}-{canonical.Value.End}";

        var schedule = await _db.AlbumSchedules.FirstOrDefaultAsync(s => s.SchoolYear == canonicalLabel);
        if (schedule == null || schedule.Status != "PreviewGenerated")
            return Conflict(new { error = "Aucun aperçu en attente de diffusion pour cette année scolaire (déjà diffusé, ou pas encore généré)." });

        var pending = await _db.ParentReports
            .Where(r => r.ReportType == "AlbumAnnuel" && r.IsPreviewPending)
            .ToListAsync();

        var toDispatch = pending.Where(r =>
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(r.Content ?? "{}");
                return doc.RootElement.TryGetProperty("SchoolYear", out var y) && y.GetString() == canonicalLabel;
            }
            catch { return false; }
        }).ToList();

        foreach (var r in toDispatch)
            r.IsPreviewPending = false;

        schedule.Status = "Dispatched";
        schedule.DispatchedAt = DateTime.UtcNow;
        schedule.DispatchedByUserId = User.GetUserId();
        schedule.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Admin {AdminId} a confirmé la diffusion de l'album {SchoolYear} à {Count} parent(s).",
            User.GetUserId(), canonicalLabel, toDispatch.Count);

        return Ok(new { schoolYear = canonicalLabel, dispatchedCount = toDispatch.Count });
    }
}
