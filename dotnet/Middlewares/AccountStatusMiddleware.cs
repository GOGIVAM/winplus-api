using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Backend.Data;

namespace Backend.Middlewares;

/// <summary>
/// Revalide l'état du compte à chaque requête authentifiée (Module 20).
///
/// L'état actif n'était vérifié qu'à la connexion et au rafraîchissement.
/// Suspendre ou supprimer un compte révoquait bien ses sessions en base, mais
/// le jeton d'accès Bearer déjà émis restait valide jusqu'à son expiration
/// naturelle : avec une durée de vie de 24 heures, un compte suspendu gardait
/// jusqu'à une journée d'accès complet.
///
/// Coût de la revalidation, mesuré en nombre de requêtes SQL plutôt qu'en
/// millisecondes (le coût réel dépend de la base de production) :
/// - sans cache, une lecture indexée sur la clé primaire de Users par requête
///   authentifiée, soit exactement une requête SQL supplémentaire ;
/// - avec le cache court appliqué ici, au plus une lecture par utilisateur et
///   par fenêtre de 30 secondes, quel que soit le nombre de requêtes.
///
/// La fenêtre de 30 secondes est le délai maximal de prise d'effet d'une
/// suspension, au lieu des 24 heures actuelles. Une suspension passe par
/// AdminUsersController, qui invalide explicitement l'entrée de cache du
/// compte visé pour un effet immédiat.
/// </summary>
public class AccountStatusMiddleware
{
    private static readonly TimeSpan CacheWindow = TimeSpan.FromSeconds(30);

    private readonly RequestDelegate _next;
    private readonly ILogger<AccountStatusMiddleware> _logger;

    public AccountStatusMiddleware(RequestDelegate next, ILogger<AccountStatusMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Clé de cache d'un compte, partagée avec l'administration.</summary>
    public static string CacheKey(int userId) => $"account-status:{userId}";

    /// <summary>
    /// À appeler dès qu'un compte est suspendu, réactivé ou supprimé, pour que
    /// la décision prenne effet à la requête suivante sans attendre la fenêtre
    /// de cache.
    /// </summary>
    public static void Invalidate(IMemoryCache cache, int userId) => cache.Remove(CacheKey(userId));

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db, IMemoryCache cache)
    {
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var raw = context.User.FindFirst("sub")?.Value
                   ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (int.TryParse(raw, out var userId))
            {
                var state = await GetStateAsync(db, cache, userId);
                var usable = state.Usable;

                // Lot 9.7a (18.1) : le rôle est porté par le jeton. Après un
                // changement de rôle par l'administration, un jeton émis avant
                // garderait l'ancien rôle (et ses droits) jusqu'à son expiration :
                // il est refusé dès que le rôle en base diffère, et le client se
                // reconnecte avec son nouveau rôle.
                var tokenRole = context.User.FindFirst("role")?.Value ?? context.User.FindFirst(ClaimTypes.Role)?.Value;
                if (usable && state.Role != null && !string.IsNullOrWhiteSpace(tokenRole)
                    && !string.Equals(tokenRole, state.Role, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Requête refusée : le rôle du compte {UserId} a changé ({TokenRole} -> {DbRole})", userId, tokenRole, state.Role);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "Votre rôle a changé. Veuillez vous reconnecter.",
                        code = "role_changed"
                    });
                    return;
                }

                if (!usable)
                {
                    _logger.LogWarning("Requête refusée : le compte {UserId} est suspendu ou supprimé", userId);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "Votre compte n'est plus actif. Veuillez contacter le support.",
                        code = "account_inactive"
                    });
                    return;
                }
            }
        }

        await _next(context);
    }

    /// <summary>État mis en cache : compte utilisable et rôle en base (null si compte inconnu).</summary>
    public sealed record AccountState(bool Usable, string? Role);

    private static async Task<AccountState> GetStateAsync(ApplicationDbContext db, IMemoryCache cache, int userId)
    {
        if (cache.TryGetValue<AccountState>(CacheKey(userId), out var cached) && cached != null)
            return cached;

        // IgnoreQueryFilters : un compte soft-deleted est invisible des
        // requêtes ordinaires, et son absence ne doit pas être confondue avec
        // un identifiant inexistant.
        var state = await db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.IsDeleted, u.Role })
            .FirstOrDefaultAsync();

        // Un identifiant inconnu n'est pas traité comme une suspension : le
        // jeton reste rejeté plus loin si nécessaire, et une base momentanément
        // incohérente ne doit pas déconnecter tout le monde.
        var result = new AccountState(state == null || (state.IsActive && !state.IsDeleted), state?.Role);

        cache.Set(CacheKey(userId), result, CacheWindow);
        return result;
    }
}

public static class AccountStatusMiddlewareExtensions
{
    public static IApplicationBuilder UseAccountStatusRevalidation(this IApplicationBuilder builder)
        => builder.UseMiddleware<AccountStatusMiddleware>();
}
