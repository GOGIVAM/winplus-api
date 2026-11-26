using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Utils;

/// <summary>
/// Filtre des messageries (26/11/2026, demande de Georges) : refuse à l'envoi les coordonnées
/// (numéros de téléphone, adresses e-mail, liens WhatsApp, Telegram, réseaux sociaux) et les gros mots,
/// pour garder les échanges sur la plateforme et respectueux.
/// Appliqué à : messagerie privée (premier message, envoi, modèles de réponse), groupes, canal de
/// formation, forum (titre et contenu des sujets, réponses).
/// Refus : 400 {success:false, error, code, message}, message en français prêt à afficher.
///   code « coordonnees_interdites » ou « langage_inapproprie ».
/// Déterministe et sans appel externe : complète la modération IA de la messagerie privée, qui laisse
/// passer le message quand elle échoue.
/// </summary>
public static class ContentModeration
{
    public const string CodeContact = "coordonnees_interdites";
    public const string CodeLanguage = "langage_inapproprie";

    public const string MessageContact =
        "Pour votre sécurité, les numéros de téléphone, adresses e-mail et liens vers d'autres messageries ou réseaux sociaux ne sont pas autorisés. Continuez l'échange sur WinPlus.";
    public const string MessageLanguage =
        "Votre message contient des mots inappropriés. Merci de le reformuler avec respect.";

    public sealed record Refusal(string Code, string Message);

    private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    // Montant ou quantité (« 650 000 000 FCFA », « 222 000 000 habitants ») : pas un numéro.
    private const string NotAnAmount =
        @"(?!\s*(?:f\b|fcfa|cfa|xaf|francs?\b|€|eur(?:os?)?\b|\$|dollars?\b|km\b|m\b|m2\b|m²|kg\b|g\b|t\b|tonnes?\b|l\b|litres?\b|habitants|personnes|élèves|eleves|%))";

    // Numéro camerounais (mobile 6XXXXXXXX, fixe 22/23/24XXXXXXX), avec ou sans +237 / 00237,
    // séparateurs facultatifs entre les chiffres.
    private static readonly Regex CameroonPhone = new(
        @"(?<![\d])(?:(?:\+|00)?237[\s.\-/]*)?(?:6(?:[\s.\-/]*\d){8}|2[\s.\-/]*[234](?:[\s.\-/]*\d){7})(?![\d])" + NotAnAmount, Opts);

    // Tout numéro avec indicatif explicite (+ ou 00), 8 à 15 chiffres.
    private static readonly Regex InternationalPhone = new(
        @"(?<![\d])(?:\+|00)\d(?:[\s.\-/]*\d){7,14}(?![\d])" + NotAnAmount, Opts);

    private static readonly Regex Email = new(
        @"[a-z0-9._%+\-]+\s*(?:@|\(at\)|\[at\]|\s(?:at|arobase|arobaz)\s)\s*[a-z0-9\-]+\s*(?:\.|\(dot\)|\[dot\]|\s(?:dot|point)\s)\s*(?:com|fr|cm|net|org|info|io|me|co)\b", Opts);

    private static readonly Regex ExternalLink = new(
        @"\b(?:wa\.me|chat\.whatsapp\.com|api\.whatsapp\.com|t\.me|telegram\.(?:me|org)|facebook\.com|fb\.com|fb\.me|m\.me|messenger\.com|instagram\.com|instagr\.am|tiktok\.com|snapchat\.com|twitter\.com|x\.com/|linkedin\.com|signal\.me|discord\.(?:gg|com))", Opts);

    // Invitation explicite à continuer ailleurs, même sans numéro (« écris-moi sur WhatsApp »).
    private static readonly Regex OffPlatformInvite = new(
        @"\b(?:[eé]cri[st]?[\s\-]?moi|contacte[\s\-]?moi|appelle[\s\-]?moi|bip[\s\-]?moi|ajoute[\s\-]?moi|retrouve[\s\-]?moi|text[\s\-]?me|call[\s\-]?me|add[\s\-]?me|mon\s+(?:num(?:[ée]ro)?|t[ée]l(?:[ée]phone)?|whatsapp|telegram|insta|facebook|fb|snap)|my\s+(?:number|phone|whatsapp))\b[^\n]{0,40}\b(?:whatsapp|wha?tsap+|watsap+|wtsp|telegram|insta(?:gram)?|facebook|fb|messenger|snap(?:chat)?|tiktok|num(?:[ée]ro)?|t[ée]l(?:[ée]phone)?|number|phone|\d{3,})\b", Opts);

    // Gros mots et insultes (français, anglais, pidgin et camfranglais courants), comparés mot à mot
    // après normalisation (minuscules, sans accents, chiffres de substitution, lettres répétées).
    // Volontairement sans mots ambigus dans un cadre scolaire : « con », « tg » (tangente), « pd »,
    // « retard » (délai), « chienne », « tapette », « dick » (prénom)…
    private static readonly HashSet<string> BadWords = new(StringComparer.Ordinal)
    {
        // français
        "putain", "ptain", "merde", "merdeux", "merdique", "connard", "connards", "connasse", "connasses", "conne", "connes",
        "salope", "salopes", "salaud", "salauds", "encule", "encules", "enculee", "enculer", "pute", "putes", "batard", "batards",
        "batarde", "niquer", "nique", "niquez", "niktamer", "fdp", "ntm", "pede", "pedes", "enfoire", "enfoires", "enfoiree",
        "abruti", "abrutis", "abrutie", "cretin", "cretins", "cretine", "debile", "debiles", "imbecile", "imbeciles", "pouffiasse",
        "trouduc", "couillon", "couillons", "bouffon", "bouffons", "gouine", "branleur", "branleurs",
        "fils de pute", "ta gueule", "ferme ta gueule", "va te faire foutre", "nique ta mere", "ta mere la pute",
        // anglais
        "fuck", "fucking", "fucker", "fucked", "motherfucker", "shit", "bullshit", "bitch", "bitches", "asshole", "assholes",
        "bastard", "bastards", "dickhead", "cunt", "whore", "slut", "wanker", "nigger", "nigga",
        // pidgin
        "ashawo",
    };

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Premier refus applicable aux textes donnés, ou null si tout est accepté.</summary>
    public static Refusal? Check(params string?[] texts)
    {
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (HasContact(text)) return new Refusal(CodeContact, MessageContact);
            if (HasBadLanguage(text)) return new Refusal(CodeLanguage, MessageLanguage);
        }
        return null;
    }

    /// <summary>Réponse 400 de refus, au format commun des refus de l'API.</summary>
    public static ObjectResult ToResult(Refusal r) =>
        new(new { success = false, error = r.Message, code = r.Code, message = r.Message }) { StatusCode = 400 };

    public static bool HasContact(string text) =>
        CameroonPhone.IsMatch(text) || InternationalPhone.IsMatch(text) || Email.IsMatch(text)
        || ExternalLink.IsMatch(text) || OffPlatformInvite.IsMatch(text);

    public static bool HasBadLanguage(string text)
    {
        // Deux lectures : lettres répétées ramenées à deux (« connasse » garde ses doubles), puis à une
        // (« merdeeee » -> « merde ») mais seulement pour les mots sans lettre doublée, pour ne pas
        // confondre « conne » et « cône ».
        var twice = " " + Normalize(text, maxRepeat: 2) + " ";
        var once = " " + Normalize(text, maxRepeat: 1) + " ";
        foreach (var w in BadWords)
        {
            if (twice.Contains(" " + w + " ", StringComparison.Ordinal)) return true;
            if (!HasDoubledLetter(w) && once.Contains(" " + w + " ", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>Minuscules, sans accents, chiffres et symboles de substitution remplacés (f*ck, sal0pe, @ss),
    /// lettres répétées réduites (merdeeee), ponctuation remplacée par des espaces.</summary>
    private static bool HasDoubledLetter(string w)
    {
        for (int i = 1; i < w.Length; i++)
            if (w[i] == w[i - 1] && char.IsLetter(w[i])) return true;
        return false;
    }

    internal static string Normalize(string text, int maxRepeat = 2)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        char prev = '\0';
        int run = 0;
        foreach (var raw in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark) continue;
            var c = raw switch
            {
                '0' => 'o', '1' => 'i', '3' => 'e', '4' => 'a', '5' => 's', '7' => 't', '@' => 'a', '$' => 's', '€' => 'e',
                '*' => '\u0001', _ => raw,
            };
            if (c == '\u0001') continue; // « f*ck » -> « fck » ne doit pas masquer : on retire l'astérisque
            if (!char.IsLetter(c)) c = ' ';
            if (c == prev && c != ' ') { if (++run >= maxRepeat) continue; } else run = 0;
            if (c == ' ' && prev == ' ') continue;
            sb.Append(c);
            prev = c;
        }
        return Spaces.Replace(sb.ToString(), " ").Trim();
    }
}
