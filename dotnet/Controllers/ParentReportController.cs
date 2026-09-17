using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using System.Linq;

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

    /// <summary>
    /// Capsule hebdomadaire la plus récente d'un enfant (dashboard uniquement,
    /// jamais l'onglet Rapports — voir ReportsTabPanel.tsx côté frontend, qui
    /// filtre déjà ReportType="CapsuleHebdo"). Pré-générée chaque lundi par
    /// WeeklyParentReportService, jamais recalculée à la demande ici.
    /// Route avant "{id:int}" : "capsule" n'est de toute façon pas un entier,
    /// mais placée ici pour rester lisible avec les autres routes nommées.
    /// </summary>
    [HttpGet("capsule/{childId:int}")]
    public async Task<IActionResult> GetLatestCapsule(int childId)
    {
        var parentId = User.GetUserId();

        var linked = await _db.ParentStudentLinks
            .AnyAsync(l => l.ParentId == parentId && l.StudentId == childId && l.Status == "accepted");
        if (!linked)
            return StatusCode(403, new { error = "Accès refusé : cet enfant n'est pas lié à votre compte." });

        var capsule = await _db.ParentReports.AsNoTracking()
            .Where(r => r.ParentId == parentId && r.ChildId == childId && r.ReportType == "CapsuleHebdo")
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { id = r.Id, capsuleText = r.CapsuleText, createdAt = r.CreatedAt })
            .FirstOrDefaultAsync();

        // 404 volontaire plutôt qu'un objet vide : le frontend doit pouvoir
        // distinguer "pas encore de capsule" (premier lundi pas encore passé)
        // d'une vraie erreur, sans avoir à inspecter le corps de la réponse.
        if (capsule == null) return NotFound();

        return Ok(capsule);
    }

    /// <summary>
    /// Dernier portefeuille de compétences calculé pour un enfant (Régularité /
    /// Autonomie / Curiosité), généré mensuellement par MonthlyPortfolioService.
    /// Route "{childId:int}/portefeuille" : "portefeuille" n'est pas un entier,
    /// aucune collision possible avec GET "{id:int}" (détail par id de rapport).
    /// </summary>
    [HttpGet("{childId:int}/portefeuille")]
    public async Task<IActionResult> GetPortfolio(int childId)
    {
        var parentId = User.GetUserId();

        var linked = await _db.ParentStudentLinks
            .AnyAsync(l => l.ParentId == parentId && l.StudentId == childId && l.Status == "accepted");
        if (!linked)
            return StatusCode(403, new { error = "Accès refusé : cet enfant n'est pas lié à votre compte." });

        var report = await _db.ParentReports.AsNoTracking()
            .Where(r => r.ParentId == parentId && r.ChildId == childId && r.ReportType == "Portefeuille")
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        // 404 volontaire (comme la capsule) plutôt qu'un objet vide : distingue
        // "pas encore de portefeuille calculé" (premier passage mensuel pas
        // encore effectué) d'une vraie erreur.
        if (report?.Content == null) return NotFound();

        string? regularite = null, autonomie = null, curiosite = null;
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(report.Content);
            var root = doc.RootElement;
            regularite = root.TryGetProperty("Regularite", out var r1) ? r1.GetString() : null;
            autonomie = root.TryGetProperty("Autonomie", out var r2) ? r2.GetString() : null;
            curiosite = root.TryGetProperty("Curiosite", out var r3) ? r3.GetString() : null;
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "Portefeuille du rapport {Id} illisible", report.Id);
        }

        return Ok(new
        {
            childId,
            regularite,
            autonomie,
            curiosite,
            updatedAt = report.CreatedAt,
        });
    }

    /// <summary>
    /// Dernier album de fin d'année d'un enfant (YearlyAlbumService, généré
    /// manuellement via POST /api/admin/albums/generate). Route
    /// "{childId:int}/album" : "album" n'est pas un entier, aucune collision
    /// possible avec GET "{id:int}".
    /// </summary>
    [HttpGet("{childId:int}/album")]
    public async Task<IActionResult> GetAlbum(int childId)
    {
        var parentId = User.GetUserId();

        var linked = await _db.ParentStudentLinks
            .AnyAsync(l => l.ParentId == parentId && l.StudentId == childId && l.Status == "accepted");
        if (!linked)
            return StatusCode(403, new { error = "Accès refusé : cet enfant n'est pas lié à votre compte." });

        var report = await _db.ParentReports.AsNoTracking()
            .Where(r => r.ParentId == parentId && r.ChildId == childId && r.ReportType == "AlbumAnnuel")
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        // 404 volontaire (comme la capsule / le portefeuille) plutôt qu'un objet
        // vide : distingue "pas encore d'album généré" d'une vraie erreur.
        if (report?.Content == null) return NotFound();

        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(report.Content);
            var root = doc.RootElement;

            string? Get(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
            List<string> GetList(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Array
                ? v.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
                : new List<string>();

            return Ok(new
            {
                childId,
                schoolYear = Get("SchoolYear"),
                subjectsWorked = Get("SubjectsWorked"),
                progression = Get("Progression"),
                topContents = GetList("TopContents"),
                goalsSummary = Get("GoalsSummary"),
                intensityWeeks = GetList("IntensityWeeks"),
                bulletin = Get("Bulletin"),
                updatedAt = report.CreatedAt,
            });
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "Album du rapport {Id} illisible", report.Id);
            return StatusCode(500, new { error = "Album illisible." });
        }
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
