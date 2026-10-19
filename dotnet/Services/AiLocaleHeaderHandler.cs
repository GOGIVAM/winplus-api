using Backend.Data;
using Backend.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Lot 10.6 : transmet la langue du profil (User.Locale, "fr" ou "en") au
/// service Python dans l'en-tête X-User-Locale. Python s'en sert pour rédiger
/// les réponses WinAI hors chat dans cette langue (services/language.py).
///
/// Le handler est posé sur tous les clients HTTP, mais n'agit que sur les
/// requêtes adressées au service IA (même hôte que AIService:BaseUrl ou
/// FastApi:BaseUrl) : Resend, NotchPay, GA4 ou Meta ne le reçoivent jamais.
/// Un en-tête déjà posé par l'appelant (cas des tâches de fond, sans requête
/// HTTP entrante) est conservé. Sans utilisateur connu, rien n'est ajouté et
/// Python retombe sur le français.
/// </summary>
public sealed class AiLocaleHeaderHandler : DelegatingHandler
{
    public const string HeaderName = "X-User-Locale";
    private const string ItemKey = "winplus.userLocale";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly HashSet<string> _aiHosts;

    public AiLocaleHeaderHandler(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor;
        _aiHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { "AIService:BaseUrl", "FastApi:BaseUrl" })
        {
            if (Uri.TryCreate(configuration[key], UriKind.Absolute, out var uri)) _aiHosts.Add(uri.Authority);
        }
        if (_aiHosts.Count == 0) _aiHosts.Add("localhost:8000");
    }

    /// <summary>"en" pour toute valeur anglaise, sinon "fr".</summary>
    public static string Normalize(string? locale) =>
        !string.IsNullOrWhiteSpace(locale) && locale.Trim().StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "fr";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } uri && uri.IsAbsoluteUri && _aiHosts.Contains(uri.Authority)
            && !request.Headers.Contains(HeaderName))
        {
            var locale = await ResolveCurrentUserLocaleAsync(cancellationToken);
            if (locale != null) request.Headers.TryAddWithoutValidation(HeaderName, locale);
        }
        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string?> ResolveCurrentUserLocaleAsync(CancellationToken ct)
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx?.User?.Identity?.IsAuthenticated != true) return null;
        if (ctx.Items.TryGetValue(ItemKey, out var cached)) return cached as string;

        string? locale = null;
        try
        {
            var userId = ctx.User.GetUserId();
            var db = ctx.RequestServices.GetService<ApplicationDbContext>();
            if (db != null)
            {
                var stored = await db.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => u.Locale)
                    .FirstOrDefaultAsync(ct);
                if (stored != null) locale = Normalize(stored);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Jeton de service ou sans identifiant utilisateur : pas de langue.
        }

        ctx.Items[ItemKey] = locale;
        return locale;
    }
}
