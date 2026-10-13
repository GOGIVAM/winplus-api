using Backend.Extensions;
using Backend.Services;

namespace Backend.Middlewares;

/// <summary>
/// Mode maintenance (lot 9.7a, suivi 18.2), réglable dans l'admin
/// (Paramètres, <see cref="PlatformSettingDefinitions.MaintenanceMode"/>).
///
/// Activé, toute requête de l'API reçoit une réponse 503
/// <c>{ code: "maintenance" }</c>, sauf :
/// <list type="bullet">
///   <item>les requêtes d'un administrateur authentifié (rôle lu dans le
///   jeton, déjà revalidé contre la base par AccountStatusMiddleware) ;</item>
///   <item>l'authentification (<c>/api/auth/*</c>) : un administrateur doit
///   pouvoir se connecter pour désactiver la maintenance ;</item>
///   <item>l'état public de la plateforme (<c>/api/platform/status</c>), lu par
///   le site pour afficher la page de maintenance, et les sondes de santé ;</item>
///   <item>les webhooks de paiement : un encaissement confirmé pendant la
///   maintenance doit être enregistré, jamais perdu.</item>
/// </list>
/// </summary>
public class MaintenanceMiddleware
{
    public const string DefaultMessage = "WinPlus est en maintenance. Nous revenons très vite.";

    private static readonly string[] AlwaysAllowedPrefixes =
    {
        "/api/auth/",
        "/api/platform/status",
        "/api/health",
        "/api/payments/webhook",
        "/api/webhooks",
    };

    private readonly RequestDelegate _next;

    public MaintenanceMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IPlatformSettings settings)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            || AlwaysAllowedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            || !settings.GetBool(PlatformSettingDefinitions.MaintenanceMode)
            || (context.User?.Identity?.IsAuthenticated == true && context.User.IsAdmin()))
        {
            await _next(context);
            return;
        }

        var message = settings.GetString(PlatformSettingDefinitions.MaintenanceMessage);
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers["Retry-After"] = "300";
        await context.Response.WriteAsJsonAsync(new
        {
            code = "maintenance",
            error = string.IsNullOrWhiteSpace(message) ? DefaultMessage : message,
        });
    }
}

public static class MaintenanceMiddlewareExtensions
{
    public static IApplicationBuilder UseMaintenanceMode(this IApplicationBuilder builder)
        => builder.UseMiddleware<MaintenanceMiddleware>();
}
