using Backend.Data;
using Backend.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Translation;

/// <summary>
/// Langue dans laquelle une requête attend les contenus de la base.
///
/// Ordre de priorité :
///  1. paramètre <c>?lang=fr|en</c> : envoyé par le web, qui connaît la langue
///     de la page affichée (une page publique /en/ consultée par un compte
///     resté en français, par exemple) ;
///  2. utilisateur connecté : <c>User.Locale</c> (cas du mobile) ;
///  3. en-tête <c>Accept-Language</c> (visiteur anonyme) ;
///  4. français.
/// </summary>
public static class ContentLanguage
{
    public const string French = "fr";
    public const string English = "en";
    public const string QueryParameter = "lang";
    private const string ItemKey = "winplus.contentLang";

    /// <summary>"en" ou "fr" pour une valeur reconnue, sinon null.</summary>
    public static string? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return English;
        if (v.StartsWith("fr", StringComparison.OrdinalIgnoreCase)) return French;
        return null;
    }

    /// <summary>Première langue reconnue (fr ou en) d'un en-tête Accept-Language, poids q respectés.</summary>
    public static string? FromAcceptLanguage(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        var ranked = header.Split(',')
            .Select((part, index) =>
            {
                var pieces = part.Split(';');
                var q = 1.0;
                foreach (var p in pieces.Skip(1))
                {
                    var kv = p.Trim();
                    if (kv.StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                        && double.TryParse(kv[2..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                        q = parsed;
                }
                return (lang: Parse(pieces[0]), q, index);
            })
            .Where(x => x.lang != null && x.q > 0)
            .OrderByDescending(x => x.q).ThenBy(x => x.index)
            .FirstOrDefault();
        return ranked.lang;
    }

    /// <summary>Langue de la requête en cours (mise en cache pour la durée de la requête).</summary>
    public static async Task<string> ResolveAsync(HttpContext ctx, CancellationToken ct = default)
    {
        if (ctx.Items.TryGetValue(ItemKey, out var cached) && cached is string s) return s;
        var lang = await ResolveUncachedAsync(ctx, ct);
        ctx.Items[ItemKey] = lang;
        return lang;
    }

    /// <summary>La langue vient-elle d'un choix explicite (?lang=) ? Sinon la réponse varie selon l'en-tête.</summary>
    public static bool IsExplicit(HttpContext ctx) => Parse(ctx.Request.Query[QueryParameter].FirstOrDefault()) != null;

    private static async Task<string> ResolveUncachedAsync(HttpContext ctx, CancellationToken ct)
    {
        var explicitLang = Parse(ctx.Request.Query[QueryParameter].FirstOrDefault());
        if (explicitLang != null) return explicitLang;

        if (ctx.User?.Identity?.IsAuthenticated == true)
        {
            try
            {
                var userId = ctx.User.GetUserId();
                var db = ctx.RequestServices.GetService<ApplicationDbContext>();
                if (db != null)
                {
                    var stored = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Locale).FirstOrDefaultAsync(ct);
                    if (stored != null) return AiLocaleHeaderHandler.Normalize(stored);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Jeton sans identifiant utilisateur : on passe à l'en-tête.
            }
        }

        return FromAcceptLanguage(ctx.Request.Headers.AcceptLanguage.ToString()) ?? French;
    }
}
