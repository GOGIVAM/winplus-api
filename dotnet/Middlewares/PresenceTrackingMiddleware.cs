using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.Entities;

namespace Backend.Middlewares;

/// <summary>
/// Suit la présence des utilisateurs connectés.
///
/// À chaque requête authentifiée, la session courante est « touchée »
/// (LastActivityAt = maintenant). C'est cette date qui permet au dashboard
/// admin de savoir qui est réellement en ligne, sans donnée simulée.
///
/// Écriture limitée à une fois par minute et par session pour ne pas
/// transformer chaque appel API en écriture SQL.
/// </summary>
public class PresenceTrackingMiddleware
{
    /// <summary>Fenêtre d'anti-rebond des écritures.</summary>
    private static readonly TimeSpan WriteThrottle = TimeSpan.FromMinutes(1);

    /// <summary>Dernière écriture par session, en mémoire process.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> LastWrite = new();

    private readonly RequestDelegate _next;
    private readonly ILogger<PresenceTrackingMiddleware> _logger;

    public PresenceTrackingMiddleware(RequestDelegate next, ILogger<PresenceTrackingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db)
    {
        await _next(context);

        if (context.User?.Identity?.IsAuthenticated != true) return;

        var raw = context.User.FindFirst("sub")?.Value
               ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(raw, out var userId)) return;

        var now = DateTime.UtcNow;
        var ip = context.Connection.RemoteIpAddress?.ToString();
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var key = $"{userId}:{ip}:{userAgent}";

        if (LastWrite.TryGetValue(key, out var last) && now - last < WriteThrottle) return;
        LastWrite[key] = now;

        try
        {
            var session = await db.UserSessions
                .Where(s => s.UserId == userId && s.IsActive && s.UserAgent == userAgent)
                .OrderByDescending(s => s.LastActivityAt)
                .FirstOrDefaultAsync();

            if (session != null)
            {
                session.LastActivityAt = now;
                session.IpAddress = ip ?? session.IpAddress;
            }
            else
            {
                db.UserSessions.Add(new UserSession
                {
                    UserId = userId,
                    DeviceName = DescribeDevice(userAgent),
                    DeviceType = DetectPlatform(userAgent),
                    IpAddress = ip,
                    UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : Truncate(userAgent, 500),
                    CreatedAt = now,
                    LastActivityAt = now,
                    IsActive = true,
                });
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // La présence ne doit jamais faire échouer une requête métier.
            _logger.LogDebug(ex, "Presence tracking skipped for user {UserId}", userId);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private static string DetectPlatform(string ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return "Inconnu";
        if (ua.Contains("Android", StringComparison.OrdinalIgnoreCase)) return "Android";
        if (ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("iPad", StringComparison.OrdinalIgnoreCase)) return "iOS";
        if (ua.Contains("Windows", StringComparison.OrdinalIgnoreCase)) return "Windows";
        if (ua.Contains("Mac OS", StringComparison.OrdinalIgnoreCase)) return "Mac";
        if (ua.Contains("Linux", StringComparison.OrdinalIgnoreCase)) return "Linux";
        return "Inconnu";
    }

    private static string DescribeDevice(string ua)
    {
        var platform = DetectPlatform(ua);
        var browser =
            ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge" :
            ua.Contains("OPR/", StringComparison.OrdinalIgnoreCase) ? "Opera" :
            ua.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ? "Chrome" :
            ua.Contains("Firefox", StringComparison.OrdinalIgnoreCase) ? "Firefox" :
            ua.Contains("Safari", StringComparison.OrdinalIgnoreCase) ? "Safari" : null;

        return browser == null ? platform : $"{browser} sur {platform}";
    }
}

public static class PresenceTrackingMiddlewareExtensions
{
    /// <summary>À brancher après UseAuthentication / UseAuthorization.</summary>
    public static IApplicationBuilder UsePresenceTracking(this IApplicationBuilder app)
        => app.UseMiddleware<PresenceTrackingMiddleware>();
}
