using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Extensions;

namespace Backend.Controllers;

[ApiController]
[Route("api/announcements")]
[Produces("application/json")]
public class AnnouncementController : ControllerBase
{
    private readonly IAnnouncementService _announcementService;
    private readonly ILogger<AnnouncementController> _logger;
    private readonly IAdminAuditService _audit;

    /// <summary>Types d'affichage acceptés (lot 9.7a, 18.7).</summary>
    private static readonly string[] AllowedTypes = { "info", "success", "warn", "critical" };

    /// <summary>Rôles ciblables (lot 9.7a, 18.7).</summary>
    private static readonly string[] AllowedRoles = { "student", "teacher", "parent", "institution" };

    public AnnouncementController(IAnnouncementService announcementService, ILogger<AnnouncementController> logger, IAdminAuditService audit)
    {
        _announcementService = announcementService ?? throw new ArgumentNullException(nameof(announcementService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _audit = audit;
    }

    /// <summary>Rôles visés normalisés (« student,parent ») ; null = tout le monde.</summary>
    private static string? NormalizeTargets(IEnumerable<string>? roles)
    {
        var list = (roles ?? Enumerable.Empty<string>())
            .Select(r => (r ?? "").Trim().ToLowerInvariant())
            .Where(r => AllowedRoles.Contains(r))
            .Distinct()
            .ToList();
        return list.Count == 0 || list.Count == AllowedRoles.Length ? null : string.Join(",", list);
    }

    /// <summary>
    /// Lot 9.7a (18.7) : une annonce ciblée n'est servie qu'aux rôles visés
    /// (le rôle est lu dans le jeton, jamais fourni par le client) ; un
    /// visiteur anonyme ne voit que les annonces destinées à tout le monde.
    /// Un administrateur voit tout.
    /// </summary>
    public static bool IsVisibleTo(Announcement a, string? role)
    {
        if (string.IsNullOrWhiteSpace(a.TargetRoles)) return true;
        if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.IsNullOrWhiteSpace(role)) return false;
        return a.TargetRoles.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(role.Trim().ToLowerInvariant());
    }

    [HttpGet]
    public async Task<IActionResult> GetPublished([FromQuery] int limit = 4)
    {
        try
        {
            limit = Math.Clamp(limit, 1, 50);
            var role = User?.Identity?.IsAuthenticated == true ? User.GetUserRole() : null;
            // On lit plus large que la limite : les annonces ciblées sur d'autres
            // rôles sont filtrées ensuite, sans réduire le nombre affiché.
            var announcements = (await _announcementService.GetPublishedAnnouncementsAsync(200))
                .Where(a => IsVisibleTo(a, role))
                .Take(limit)
                .ToList();
            return Ok(new { data = announcements, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting published announcements");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var announcement = await _announcementService.GetAnnouncementByIdAsync(id);
            if (announcement == null)
                return NotFound(new { success = false, error = "Announcement not found" });

            return Ok(new { data = announcement, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting announcement {AnnouncementId}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    // ── Admin endpoints ───────────────────────────────────────────────────────

    [HttpGet("admin/all")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var announcements = await _announcementService.GetAllAnnouncementsAsync();
            var list = announcements.ToList();
            return Ok(new { data = list, count = list.Count, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all announcements");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] AnnouncementRequest request)
    {
        try
        {
            var adminId = User.GetUserId();
            var entity = new Announcement
            {
                Title = request.Title,
                Content = request.Content,
                Priority = request.Priority,
                ExpiresAt = request.ExpiresAt.HasValue ? DateTime.SpecifyKind(request.ExpiresAt.Value, DateTimeKind.Utc) : null,
                IsPublished = false,
                CreatedBy = adminId,
                Type = AllowedTypes.Contains(request.Type ?? "") ? request.Type! : "info",
                TargetRoles = NormalizeTargets(request.TargetRoles),
            };

            var created = await _announcementService.CreateAnnouncementAsync(entity);
            await this.AuditAsync(_audit, AdminAuditActions.AnnouncementCreated, "Announcement", created.Id, created.Title,
                after: new { created.Type, created.TargetRoles, created.Priority, created.ExpiresAt });
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, new { data = created, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating announcement");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(int id, [FromBody] AnnouncementRequest request)
    {
        try
        {
            var existing = await _announcementService.GetAnnouncementByIdAsync(id);
            if (existing == null)
                return NotFound(new { success = false, error = "Announcement not found" });

            var before = new { existing.Title, existing.Type, existing.TargetRoles, existing.Priority, existing.ExpiresAt };
            existing.Title = request.Title;
            existing.Content = request.Content;
            existing.Priority = request.Priority;
            existing.ExpiresAt = request.ExpiresAt.HasValue ? DateTime.SpecifyKind(request.ExpiresAt.Value, DateTimeKind.Utc) : null;
            if (request.Type != null) existing.Type = AllowedTypes.Contains(request.Type) ? request.Type : "info";
            if (request.TargetRoles != null) existing.TargetRoles = NormalizeTargets(request.TargetRoles);

            var updated = await _announcementService.UpdateAnnouncementAsync(existing);
            await this.AuditAsync(_audit, AdminAuditActions.AnnouncementUpdated, "Announcement", id, existing.Title,
                before, new { existing.Title, existing.Type, existing.TargetRoles, existing.Priority, existing.ExpiresAt });
            return Ok(new { data = updated, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating announcement {AnnouncementId}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var result = await _announcementService.DeleteAnnouncementAsync(id);
            if (!result)
                return NotFound(new { success = false, error = "Announcement not found" });
            await this.AuditAsync(_audit, AdminAuditActions.AnnouncementDeleted, "Announcement", id);

            return Ok(new { success = true, message = "Announcement deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting announcement {AnnouncementId}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPost("{id:int}/publish")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Publish(int id)
    {
        try
        {
            var result = await _announcementService.PublishAnnouncementAsync(id);
            if (!result)
                return NotFound(new { success = false, error = "Announcement not found" });
            await this.AuditAsync(_audit, AdminAuditActions.AnnouncementPublished, "Announcement", id);

            return Ok(new { success = true, message = "Announcement published" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing announcement {AnnouncementId}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}

/// <summary>Lot 9.7a (18.7) : <c>Type</c> et <c>TargetRoles</c> enregistrés et appliqués.</summary>
public record AnnouncementRequest(
    string Title,
    string? Content,
    int Priority,
    DateTime? ExpiresAt,
    string? Type = null,
    List<string>? TargetRoles = null
);
