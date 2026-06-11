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

    public AnnouncementController(IAnnouncementService announcementService, ILogger<AnnouncementController> logger)
    {
        _announcementService = announcementService ?? throw new ArgumentNullException(nameof(announcementService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet]
    public async Task<IActionResult> GetPublished([FromQuery] int limit = 4)
    {
        try
        {
            var announcements = await _announcementService.GetPublishedAnnouncementsAsync(limit);
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
                ExpiresAt = request.ExpiresAt,
                IsPublished = false,
                CreatedBy = adminId,
            };

            var created = await _announcementService.CreateAnnouncementAsync(entity);
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

            existing.Title = request.Title;
            existing.Content = request.Content;
            existing.Priority = request.Priority;
            existing.ExpiresAt = request.ExpiresAt;

            var updated = await _announcementService.UpdateAnnouncementAsync(existing);
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

            return Ok(new { success = true, message = "Announcement published" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing announcement {AnnouncementId}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}

public record AnnouncementRequest(
    string Title,
    string? Content,
    int Priority,
    DateTime? ExpiresAt
);
