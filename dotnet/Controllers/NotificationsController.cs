using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using Backend.Data;
using Backend.Extensions;

namespace Backend.Controllers;

[ApiController]
[Route("api/notifications")]
[Produces("application/json")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<NotificationsController> _logger;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _configuration;

    public NotificationsController(
        ApplicationDbContext db,
        ILogger<NotificationsController> logger,
        IHttpClientFactory http,
        IConfiguration configuration)
    {
        _db = db;
        _logger = logger;
        _http = http;
        _configuration = configuration;
    }

    /// <summary>
    /// Proxy SSE ntfy → client. Le frontend s'authentifie avec son JWT WinPlus ;
    /// le backend utilise ses propres credentials ntfy. Ainsi aucun token ntfy
    /// ne transite côté client.
    /// </summary>
    [HttpGet("sse")]
    [Produces("text/event-stream")]
    public async Task StreamSSE(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var ntfyBase = _configuration["Ntfy:BaseUrl"] ?? "https://ntfy.winplus.cm";
        var ntfyToken = _configuration["Ntfy:AuthToken"];
        var sseUrl = $"{ntfyBase}/winplus-user-{userId}/sse";

        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        using var req = new HttpRequestMessage(HttpMethod.Get, sseUrl);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (!string.IsNullOrEmpty(ntfyToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ntfyToken);

        try
        {
            using var client = _http.CreateClient();
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Response.StatusCode = (int)resp.StatusCode;
                return;
            }
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            await stream.CopyToAsync(Response.Body, ct);
        }
        catch (OperationCanceledException) { /* déconnexion normale du client */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSE proxy error for user {UserId}", userId);
        }
    }

    /// <summary>
    /// Notifications paginées de l'utilisateur, non lues en premier
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetNotifications([FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        try
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 20;
            if (limit > 100) limit = 100;

            var userId = User.GetUserId();

            var query = _db.Notifications.Where(n => n.UserId == userId);
            var total = await query.CountAsync();
            var unread = await query.CountAsync(n => !n.IsRead);

            var notifications = await query
                .OrderBy(n => n.IsRead)
                .ThenByDescending(n => n.CreatedAt)
                .Skip((page - 1) * limit)
                .Take(limit)
                .Select(n => new
                {
                    n.Id,
                    n.Title,
                    n.Message,
                    n.Type,
                    n.IsRead,
                    n.CreatedAt,
                    n.ReadAt,
                    n.RelatedEntityType,
                    n.RelatedEntityId
                })
                .ToListAsync();

            return Ok(new
            {
                data = notifications,
                unread,
                total,
                page,
                limit,
                totalPages = (int)Math.Ceiling(total / (double)limit),
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting notifications");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Marque une notification comme lue
    /// </summary>
    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        try
        {
            var userId = User.GetUserId();
            var notification = await _db.Notifications
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);

            if (notification == null)
                return NotFound(new { success = false, error = "Notification not found" });

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking notification {Id} as read", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Marque toutes les notifications de l'utilisateur comme lues
    /// </summary>
    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        try
        {
            var userId = User.GetUserId();
            var now = DateTime.UtcNow;

            await _db.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.IsRead, true)
                    .SetProperty(n => n.ReadAt, now));

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking all notifications as read");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
