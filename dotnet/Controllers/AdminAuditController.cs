using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;

namespace Backend.Controllers;

/// <summary>
/// Écran « Journal d'audit » (lot 9.7a, suivi 18.3) : actions sensibles de
/// l'administration, filtrables par auteur, action, cible et période,
/// paginées. Remplace l'ancien écran, qui ne montrait que des commandes et
/// des inscriptions sous le nom de journal d'audit.
///
///   GET /api/admin/audit-logs?actor=&amp;action=&amp;targetType=&amp;targetId=&amp;from=&amp;to=&amp;page=&amp;pageSize=
///   GET /api/admin/audit-logs/facets   actions et types de cible présents (filtres)
/// </summary>
[ApiController]
[Route("api/admin/audit-logs")]
[Authorize(Policy = "AdminOnly")]
[Produces("application/json")]
public class AdminAuditController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public AdminAuditController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? actor,
        [FromQuery] string? action,
        [FromQuery] string? targetType,
        [FromQuery] string? targetId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.AdminAuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(actor))
        {
            var term = actor.Trim().ToLower();
            query = int.TryParse(term, out var actorId)
                ? query.Where(a => a.ActorUserId == actorId)
                : query.Where(a => a.ActorEmail != null && a.ActorEmail.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(action))
            query = action.EndsWith('.')
                ? query.Where(a => a.Action.StartsWith(action))
                : query.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(targetType)) query = query.Where(a => a.TargetType == targetType);
        if (!string.IsNullOrWhiteSpace(targetId))
        {
            var t = targetId.Trim();
            query = query.Where(a => a.TargetId == t || (a.TargetLabel != null && a.TargetLabel.ToLower().Contains(t.ToLower())));
        }
        if (from.HasValue) query = query.Where(a => a.CreatedAt >= DateTime.SpecifyKind(from.Value, DateTimeKind.Utc));
        if (to.HasValue) query = query.Where(a => a.CreatedAt < DateTime.SpecifyKind(to.Value, DateTimeKind.Utc).AddDays(1));

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new
            {
                id = a.Id,
                actorUserId = a.ActorUserId,
                actorEmail = a.ActorEmail,
                action = a.Action,
                targetType = a.TargetType,
                targetId = a.TargetId,
                targetLabel = a.TargetLabel,
                before = a.Before,
                after = a.After,
                reason = a.Reason,
                ipAddress = a.IpAddress,
                createdAt = a.CreatedAt,
            })
            .ToListAsync();

        return Ok(new { success = true, data = new { items, total, page, pageSize } });
    }

    [HttpGet("facets")]
    public async Task<IActionResult> Facets()
    {
        var actions = await _db.AdminAuditLogs.AsNoTracking().Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();
        var targetTypes = await _db.AdminAuditLogs.AsNoTracking().Where(a => a.TargetType != null)
            .Select(a => a.TargetType!).Distinct().OrderBy(t => t).ToListAsync();
        return Ok(new { success = true, data = new { actions, targetTypes } });
    }
}
