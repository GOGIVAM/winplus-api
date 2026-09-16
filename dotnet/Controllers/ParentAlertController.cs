using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;

namespace Backend.Controllers;

/// <summary>
/// Historique consultable des alertes WinAI destinées à un parent (table ParentAlert).
/// Distinct de GET /api/parent/alerts (ParentController), qui lit l'ancienne table
/// Notification générique — legacy, non alimentée par le flux d'alertes actuel, non touchée ici
/// (ne jamais faire migrer ces alertes vers Notification ou DirectMessage).
/// </summary>
[ApiController]
[Route("api/parent-alerts")]
[Authorize]
public class ParentAlertController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ParentAlertController> _logger;

    public ParentAlertController(ApplicationDbContext db, ILogger<ParentAlertController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Liste des alertes d'un enfant, pour le parent connecté. Filtre optionnel is_read.</summary>
    [HttpGet("{childId:int}")]
    public async Task<IActionResult> GetAlerts(int childId, [FromQuery] bool? isRead)
    {
        var parentId = User.GetUserId();

        var linked = await _db.ParentStudentLinks
            .AnyAsync(l => l.ParentId == parentId && l.StudentId == childId && l.Status == "accepted");
        if (!linked)
            return StatusCode(403, new { error = "Accès refusé : cet enfant n'est pas lié à votre compte." });

        var query = _db.ParentAlerts.AsNoTracking()
            .Where(a => a.ParentId == parentId && a.ChildId == childId);

        if (isRead.HasValue)
            query = query.Where(a => a.IsRead == isRead.Value);

        var alerts = await query
            .OrderByDescending(a => a.DetectedAt)
            .Select(a => new
            {
                id = a.Id,
                childId = a.ChildId,
                type = a.Type,
                severity = a.Severity,
                content = a.Content,
                isRead = a.IsRead,
                detectedAt = a.DetectedAt,
                createdAt = a.CreatedAt,
            })
            .ToListAsync();

        return Ok(alerts);
    }

    /// <summary>Marque une alerte comme lue.</summary>
    [HttpPatch("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var parentId = User.GetUserId();

        var alert = await _db.ParentAlerts.FirstOrDefaultAsync(a => a.Id == id);
        if (alert == null) return NotFound();
        if (alert.ParentId != parentId) return StatusCode(403, new { error = "Accès refusé." });

        if (!alert.IsRead)
        {
            alert.IsRead = true;
            await _db.SaveChangesAsync();
        }

        return Ok(new { id = alert.Id, isRead = alert.IsRead });
    }
}
