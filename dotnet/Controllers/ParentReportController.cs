using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;

namespace Backend.Controllers;

public record PublishParentReportRequest(int ChildId, string Content);

/// <summary>
/// Historique consultable des rapports destinés à un parent (table ParentReport) :
/// hebdomadaire automatique, à la demande d'un enseignant, capsule hebdomadaire, album annuel.
///
/// Remplace l'ancien ReportsController — l'action de création vivait déjà sur
/// POST /api/reports/parent-report (contrat déjà utilisé par le frontend, conservé ici via
/// une route absolue) et ne faisait que journaliser en attendant cette table.
///
/// Distinct de DirectMessage/Notification : ne jamais y faire migrer ces rapports.
/// </summary>
[ApiController]
[Route("api/parent-reports")]
[Authorize]
public class ParentReportController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ParentReportController> _logger;

    public ParentReportController(ApplicationDbContext db, ILogger<ParentReportController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Liste des rapports d'un enfant, pour le parent connecté.
    /// Route en "/child/{childId}" plutôt que "/{childId}" pour ne pas entrer en collision
    /// avec GET /api/parent-reports/{id} (détail) — les deux routes partageraient sinon le
    /// même schéma d'URL pour un paramètre de sens différent.
    /// </summary>
    [HttpGet("child/{childId:int}")]
    public async Task<IActionResult> GetReportsForChild(int childId, [FromQuery] string? reportType)
    {
        var parentId = User.GetUserId();

        var linked = await _db.ParentStudentLinks
            .AnyAsync(l => l.ParentId == parentId && l.StudentId == childId && l.Status == "accepted");
        if (!linked)
            return StatusCode(403, new { error = "Accès refusé : cet enfant n'est pas lié à votre compte." });

        var query = _db.ParentReports.AsNoTracking()
            .Where(r => r.ParentId == parentId && r.ChildId == childId);

        if (!string.IsNullOrWhiteSpace(reportType))
            query = query.Where(r => r.ReportType == reportType);

        var reports = await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                id = r.Id,
                childId = r.ChildId,
                reportType = r.ReportType,
                capsuleText = r.CapsuleText,
                emitterType = r.EmitterType,
                isRead = r.IsRead,
                createdAt = r.CreatedAt,
            })
            .ToListAsync();

        return Ok(reports);
    }

    /// <summary>Détail d'un rapport (contenu complet).</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetReport(int id)
    {
        var parentId = User.GetUserId();

        var report = await _db.ParentReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (report == null) return NotFound();
        if (report.ParentId != parentId) return StatusCode(403, new { error = "Accès refusé." });

        if (!report.IsRead)
        {
            await _db.ParentReports.Where(r => r.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsRead, true));
        }

        return Ok(new
        {
            id = report.Id,
            childId = report.ChildId,
            reportType = report.ReportType,
            content = report.Content,
            capsuleText = report.CapsuleText,
            emitterType = report.EmitterType,
            emitterId = report.EmitterId,
            isRead = true,
            createdAt = report.CreatedAt,
        });
    }

    /// <summary>
    /// Publie un rapport WinAI généré par un enseignant à destination des parents liés à un
    /// élève. Route absolue conservée telle qu'utilisée par le frontend (US-MSG-10) — ne passe
    /// jamais par DirectMessage.
    /// </summary>
    [HttpPost("/api/reports/parent-report")]
    [Authorize(Roles = "teacher")]
    public async Task<IActionResult> PublishParentReport([FromBody] PublishParentReportRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Content))
            return BadRequest(new { error = "Le contenu du rapport est vide." });

        var emitterId = User.GetUserId();

        var parentIds = await _db.ParentStudentLinks
            .Where(l => l.StudentId == req.ChildId && l.Status == "accepted")
            .Select(l => l.ParentId)
            .ToListAsync();

        if (parentIds.Count == 0)
        {
            _logger.LogWarning(
                "Rapport parent non publié : aucun parent lié et accepté pour l'élève {ChildId} (émetteur {EmitterId}).",
                req.ChildId, emitterId);
            return NotFound(new { error = "Aucun parent lié à cet élève." });
        }

        var reports = parentIds.Select(parentId => new Models.Entities.ParentReport
        {
            ParentId = parentId,
            ChildId = req.ChildId,
            ReportType = "ALaDemande",
            Content = req.Content,
            EmitterType = "Teacher",
            EmitterId = emitterId,
        }).ToList();

        _db.ParentReports.AddRange(reports);
        await _db.SaveChangesAsync();

        return Ok(new { published = true, parentCount = reports.Count });
    }
}
