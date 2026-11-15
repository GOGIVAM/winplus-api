using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.Entities;

namespace Backend.Middlewares;

/// <summary>
/// Écriture « une seule fois » pour la file d'attente hors ligne du mobile
/// (lot 3). Le téléphone joint à chaque action rejouée un en-tête
/// Idempotency-Key unique. Si la même clé revient pour le même compte (la
/// première réponse s'est perdue avec le réseau), la première réponse est
/// renvoyée telle quelle, avec l'en-tête Idempotent-Replayed, et l'action
/// n'est pas exécutée une seconde fois.
///
/// Sans en-tête, rien ne change : le site et les autres appels ne sont pas
/// concernés. Seules les réponses définitives sont retenues : une erreur
/// serveur (5xx), une session refusée (401) ou une limite de débit (429)
/// laissent l'action rejouable.
/// </summary>
public class ClientIdempotencyMiddleware
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    /// <summary>Corps de réponse gardé au plus (au-delà, seul le statut l'est).</summary>
    private const int MaxStoredBody = 64 * 1024;

    /// <summary>Durée de conservation des clés.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private readonly RequestDelegate _next;
    private readonly ILogger<ClientIdempotencyMiddleware> _logger;

    public ClientIdempotencyMiddleware(RequestDelegate next, ILogger<ClientIdempotencyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IServiceScopeFactory scopeFactory)
    {
        var method = context.Request.Method;
        var isWrite = HttpMethods.IsPost(method) || HttpMethods.IsPut(method)
                      || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);
        var key = context.Request.Headers[HeaderName].ToString().Trim();
        if (!isWrite || key.Length == 0 || key.Length > 100 || !TryGetUserId(context, out var userId))
        {
            await _next(context);
            return;
        }

        // Déjà traitée : la première réponse, sans rien rejouer.
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var done = await db.ClientRequestKeys.AsNoTracking()
                .FirstOrDefaultAsync(k => k.UserId == userId && k.Key == key);
            if (done != null)
            {
                context.Response.StatusCode = done.StatusCode;
                context.Response.Headers[ReplayedHeader] = "true";
                if (done.ResponseBody != null)
                {
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsync(done.ResponseBody);
                }
                return;
            }
        }

        // Première fois : exécution normale, réponse recopiée pour la garder.
        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = original;
        }
        buffer.Position = 0;
        await buffer.CopyToAsync(original);

        var status = context.Response.StatusCode;
        if (status >= 500 || status == StatusCodes.Status401Unauthorized || status == StatusCodes.Status429TooManyRequests)
            return;

        string? body = null;
        if (buffer.Length > 0 && buffer.Length <= MaxStoredBody)
            body = Encoding.UTF8.GetString(buffer.ToArray());

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ClientRequestKeys.Add(new ClientRequestKey
            {
                UserId = userId,
                Key = key,
                Method = method,
                Path = Truncate(context.Request.Path.Value ?? string.Empty, 255),
                StatusCode = status,
                ResponseBody = body,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            // Ménage occasionnel des clés anciennes, sans tâche dédiée.
            if (Random.Shared.Next(200) == 0)
            {
                var limit = DateTime.UtcNow - Retention;
                await db.ClientRequestKeys.Where(k => k.CreatedAt < limit).ExecuteDeleteAsync();
            }
        }
        catch (Exception ex)
        {
            // Clé enregistrée au même instant par une requête jumelle, ou base
            // indisponible : la réponse est déjà partie, seule la protection
            // contre un futur doublon manque.
            _logger.LogWarning(ex, "Clé d'idempotence non enregistrée pour l'utilisateur {UserId}", userId);
        }
    }

    private static bool TryGetUserId(HttpContext context, out int userId)
    {
        userId = 0;
        if (context.User?.Identity?.IsAuthenticated != true) return false;
        var raw = context.User.FindFirst("sub")?.Value
                  ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(raw, out userId);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

public static class ClientIdempotencyMiddlewareExtensions
{
    public static IApplicationBuilder UseClientIdempotency(this IApplicationBuilder app)
        => app.UseMiddleware<ClientIdempotencyMiddleware>();
}
