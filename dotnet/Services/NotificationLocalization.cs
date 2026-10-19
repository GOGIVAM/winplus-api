using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Lot 10.6 : texte de notification dans les deux langues du site. Le
/// français reste le texte historique ; l'anglais est choisi pour un compte
/// dont <c>User.Locale</c> vaut « en ».
/// Pas de conversion implicite depuis une chaîne : chaque texte destiné à un
/// utilisateur doit être écrit explicitement dans les deux langues.
/// </summary>
public readonly record struct LocalizedText(string Fr, string En)
{
    /// <summary>Texte dans la langue donnée (« en », « en-GB »... sinon français).</summary>
    public string For(string? locale) => NotificationLocales.IsEnglish(locale) ? En : Fr;

    /// <summary>Concatène deux textes bilingues.</summary>
    public static LocalizedText operator +(LocalizedText a, LocalizedText b) => new(a.Fr + b.Fr, a.En + b.En);
}

/// <summary>Langue du destinataire d'une notification (User.Locale).</summary>
public static class NotificationLocales
{
    public const string French = "fr";
    public const string English = "en";

    public static bool IsEnglish(string? locale) => AiLocaleHeaderHandler.Normalize(locale) == English;

    /// <summary>
    /// Langue du compte : « fr » ou « en ». Français si le compte est inconnu,
    /// sans destinataire, ou si la lecture échoue (une notification ne doit
    /// jamais faire échouer l'opération métier).
    /// </summary>
    public static async Task<string> ForUserAsync(ApplicationDbContext? db, int? userId, CancellationToken ct = default)
    {
        if (db == null || userId is not int id) return French;
        try
        {
            var stored = await db.Users.AsNoTracking()
                .Where(u => u.Id == id)
                .Select(u => u.Locale)
                .FirstOrDefaultAsync(ct);
            return AiLocaleHeaderHandler.Normalize(stored);
        }
        catch
        {
            return French;
        }
    }
}

/// <summary>Envoi d'une notification dans la langue du destinataire.</summary>
public static class NtfyLocalizedExtensions
{
    /// <summary>
    /// Comme <see cref="INtfyService.PublishAsync"/>, avec un titre et un
    /// message bilingues : la langue est lue sur le compte <paramref name="userId"/>
    /// (français sans destinataire identifié).
    /// </summary>
    public static async Task PublishLocalizedAsync(this INtfyService ntfy, ApplicationDbContext? db,
        string topic, LocalizedText title, LocalizedText message,
        string priority = "default",
        string[]? tags = null,
        int? userId = null,
        string type = "General",
        string? relatedEntityType = null,
        int? relatedEntityId = null)
    {
        var locale = await NotificationLocales.ForUserAsync(db, userId);
        await ntfy.PublishAsync(topic, title.For(locale), message.For(locale), priority, tags, userId, type,
            relatedEntityType, relatedEntityId);
    }
}
