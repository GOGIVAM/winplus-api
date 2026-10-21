using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Backend.Services.Translation;

/// <summary>Désactive la localisation des contenus pour une action (écrans d'édition).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class NoContentLocalizationAttribute : Attribute { }

/// <summary>
/// Sert les contenus de la base dans la langue demandée, sans toucher aux
/// contrôleurs ni aux noms de champs : toute réponse JSON d'une lecture (GET)
/// passe ici quand la langue est l'anglais (<see cref="ContentLanguage"/>).
///
///  - un texte de contenu (titre, description, chapitre, leçon, objectif…)
///    égal au français d'un contenu traduit est remplacé par son anglais ;
///  - un libellé de taxonomie (matière, type d'examen, niveau, série, session,
///    difficulté…) est remplacé sous les clés qui en portent ;
///  - l'objet qui porte un contenu reçoit <c>contentLang</c> : "en" s'il est
///    entièrement traduit, "fr" s'il reste un texte français (repli) ; le front
///    pose alors lang="fr" sur le bloc.
///
/// Non localisés : écritures, espace d'administration (toujours en français),
/// écrans d'édition du professeur et du profil, routes de traduction elles-mêmes,
/// et actions marquées <see cref="NoContentLocalizationAttribute"/>.
/// </summary>
public sealed class ContentLocalizationFilter : IAsyncResultFilter
{
    /// <summary>Clés dont la valeur est un libellé de taxonomie → type de taxonomie (null : tous types).</summary>
    public static readonly Dictionary<string, string?> TaxonomyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["category"] = "category", ["categoryName"] = "category", ["subject"] = "category", ["subjectName"] = "category",
        ["subjectCategory"] = "category", ["matiere"] = "category", ["discipline"] = "category",
        ["examType"] = "examType", ["exam"] = "examType", ["examName"] = "examType", ["concours"] = "examType",
        ["level"] = "level", ["levelName"] = "level", ["subjectLevel"] = "level", ["classLevel"] = "level", ["gradeLevel"] = "level",
        ["series"] = "series", ["serie"] = "series",
        ["cycle"] = "cycle", ["subsystem"] = "subsystem", ["sousSysteme"] = "subsystem",
        // difficulty (facile, moyen, difficile) est un code traduit par l'interface : non remplacé.
        ["session"] = "session",
        ["statusLabel"] = "status",
        // Listes d'options : libellé affiché seulement (jamais value/id/slug).
        ["name"] = null, ["label"] = null, ["displayName"] = null, ["title"] = null,
    };

    private static readonly string[] ExcludedPrefixes =
    {
        "/api/admin", "/api/teacher", "/api/auth", "/api/users", "/api/me", "/api/tutor-profile",
        "/api/content-translations", "/api/payments", "/api/wallet", "/api/withdrawals",
    };

    /// <summary>Exceptions aux préfixes exclus : lectures, sans formulaire d'édition derrière.</summary>
    private static readonly string[] AllowedUnderExcluded = { "/api/teacher/bank" };

    private readonly IContentTranslationIndex _index;
    private readonly JsonSerializerOptions _json;

    public ContentLocalizationFilter(IContentTranslationIndex index, IOptions<Microsoft.AspNetCore.Mvc.JsonOptions> mvcJson)
    {
        _index = index;
        _json = mvcJson.Value.JsonSerializerOptions;
    }

    public static bool IsLocalizablePath(PathString path)
    {
        var p = path.Value ?? "";
        if (AllowedUnderExcluded.Any(a => p.StartsWith(a, StringComparison.OrdinalIgnoreCase))) return true;
        return !ExcludedPrefixes.Any(x => p.StartsWith(x, StringComparison.OrdinalIgnoreCase));
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (context.Result is ObjectResult { Value: not null } result
            && HttpMethods.IsGet(http.Request.Method)
            && (result.StatusCode is null or >= 200 and < 300)
            && IsLocalizablePath(http.Request.Path)
            && !context.ActionDescriptor.EndpointMetadata.OfType<NoContentLocalizationAttribute>().Any()
            && result.Value is not string && result.Value is not byte[])
        {
            if (!ContentLanguage.IsExplicit(http)) http.Response.Headers.Append("Vary", "Accept-Language");
            var lang = await ContentLanguage.ResolveAsync(http, http.RequestAborted);
            if (lang == ContentLanguage.English)
            {
                var snapshot = await _index.GetAsync(http.RequestAborted);
                var node = result.Value as JsonNode ?? JsonSerializer.SerializeToNode(result.Value, result.Value.GetType(), _json);
                if (node != null)
                {
                    Localize(node, snapshot);
                    result.Value = node;
                    result.DeclaredType = typeof(JsonNode);
                }
                http.Response.Headers.ContentLanguage = "en";
            }
        }
        await next();
    }

    /// <summary>Traduit en place un arbre JSON (exposé pour les tests).</summary>
    public static void Localize(JsonNode node, ContentTranslationSnapshot snapshot, string? parentKey = null)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                bool known = false, untranslated = false;
                foreach (var (key, child) in obj.ToList())
                {
                    if (child is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
                    {
                        var replaced = Replace(key, s, snapshot, ref known, ref untranslated, out var isTaxonomy);
                        if (replaced == null) continue;
                        obj[key] = replaced;
                        // Libellé de taxonomie : la valeur française reste disponible
                        // (categoryFr, examTypeFr…) pour les filtres et les adresses
                        // des pages (/concours/bac/mathematiques), qui restent en français.
                        var frKey = key + "Fr";
                        if (isTaxonomy && !obj.ContainsKey(frKey)) obj[frKey] = s;
                    }
                    else if (child != null)
                    {
                        Localize(child, snapshot, key);
                    }
                }
                if (known && !obj.ContainsKey("contentLang")) obj["contentLang"] = untranslated ? "fr" : "en";
                break;
            }
            case JsonArray arr:
            {
                for (var i = 0; i < arr.Count; i++)
                {
                    var item = arr[i];
                    if (item is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
                    {
                        bool k = false, u = false;
                        var replaced = Replace(parentKey ?? "", s, snapshot, ref k, ref u, out _);
                        if (replaced != null) arr[i] = replaced;
                    }
                    else if (item != null)
                    {
                        Localize(item, snapshot, parentKey);
                    }
                }
                break;
            }
        }
    }

    private static string? Replace(string key, string value, ContentTranslationSnapshot snapshot, ref bool known, ref bool untranslated,
        out bool isTaxonomy)
    {
        isTaxonomy = false;
        if (key.Equals("contentLang", StringComparison.OrdinalIgnoreCase) || key.EndsWith("Fr", StringComparison.Ordinal)) return null;
        if (snapshot.TryText(value, out var en))
        {
            known = true;
            return en;
        }
        var isTaxonomyKey = TaxonomyKeys.TryGetValue(key, out var kind) || TaxonomyKeys.TryGetValue(Singular(key), out kind);
        if (isTaxonomyKey && snapshot.TryTaxonomy(kind, value, out var label))
        {
            isTaxonomy = true;
            return label;
        }
        if (snapshot.IsKnownFrench(value))
        {
            known = true;
            untranslated = true;
        }
        return null;
    }

    /// <summary>"categories" → "category", "levels" → "level" (listes de libellés).</summary>
    private static string Singular(string key) =>
        key.EndsWith("ies", StringComparison.OrdinalIgnoreCase) ? key[..^3] + "y"
        : key.EndsWith("s", StringComparison.OrdinalIgnoreCase) && !key.EndsWith("ss", StringComparison.OrdinalIgnoreCase) ? key[..^1] : key;
}
