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

        // En-têtes et premier octet partent tout de suite : sans cela, si ntfy
        // tarde à répondre, le reverse proxy ne reçoit rien avant son délai et
        // renvoie 504 au lieu d'un flux ouvert.
        var writeLock = new SemaphoreSlim(1, 1);
        async Task WriteAsync(string text)
        {
            await writeLock.WaitAsync(ct);
            try
            {
                await Response.WriteAsync(text, ct);
                await Response.Body.FlushAsync(ct);
            }
            finally { writeLock.Release(); }
        }

        try
        {
            await WriteAsync(": connected\n\n");

            using var req = new HttpRequestMessage(HttpMethod.Get, sseUrl);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (!string.IsNullOrEmpty(ntfyToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ntfyToken);

            // Flux long : pas de délai global, mais 10 s max pour joindre ntfy.
            using var client = _http.CreateClient();
            client.Timeout = Timeout.InfiniteTimeSpan;
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(10));
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, connectCts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("ntfy SSE returned {Status} for user {UserId}", (int)resp.StatusCode, userId);
                await WriteAsync($"event: error\ndata: ntfy {(int)resp.StatusCode}\n\n");
                return;
            }

            // Battement toutes les 20 s : garde la connexion ouverte à travers
            // nginx/ALB même quand aucune notification n'arrive.
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var heartbeat = Task.Run(async () =>
            {
                try
                {
                    while (!heartbeatCts.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(20), heartbeatCts.Token);
                        await WriteAsync(": ping\n\n");
                    }
                }
                catch (OperationCanceledException) { }
            }, CancellationToken.None);

            try
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                var buffer = new byte[4096];
                int read;
                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await writeLock.WaitAsync(ct);
                    try
                    {
                        await Response.Body.WriteAsync(buffer.AsMemory(0, read), ct);
                        await Response.Body.FlushAsync(ct);
                    }
                    finally { writeLock.Release(); }
                }
            }
            finally
            {
                heartbeatCts.Cancel();
                await heartbeat;
            }
        }
        catch (OperationCanceledException) { /* déconnexion du client ou ntfy injoignable (10 s) */ }
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
