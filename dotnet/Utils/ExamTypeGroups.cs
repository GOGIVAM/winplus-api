using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Backend.Utils;

/// <summary>
/// Regroupe les types d'examen saisis librement à l'upload sous le nom de
/// l'école ou de l'examen. En base, Exam.ExamType contient souvent tout
/// l'intitulé (« IUT/Concours d'entrée – épreuve de Mathématique(QCM) »,
/// « IUT- Épreuve de concours », « FMSB/Examen national… ») : comparer ce
/// texte à « iut » ne retrouvait que les épreuves saisies exactement « IUT ».
/// On garde donc ce qui précède le premier séparateur (« / », « – », « — »,
/// « ( » ou « - » suivi d'un espace) et on compare sous une forme normalisée.
/// </summary>
public static class ExamTypeGroups
{
    private static readonly Regex Separator = new(@"\s*(?:/|–|—|\()|\s*-\s", RegexOptions.Compiled);

    /// <summary>Identifiants courts des cartes de l'accueil qui ne sont pas le début du libellé saisi.</summary>
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["bac"] = new[] { "baccalaureat", "bac" },
        ["proba"] = new[] { "probatoire" },
    };

    /// <summary>Nom de l'école ou de l'examen : le début du libellé, avant le premier séparateur.</summary>
    public static string Head(string? examType)
    {
        if (string.IsNullOrWhiteSpace(examType)) return string.Empty;
        var text = examType.Trim();
        var m = Separator.Match(text);
        var head = m.Success && m.Index > 0 ? text[..m.Index].Trim() : text;
        return head.Length > 0 ? head : text;
    }

    /// <summary>Clé de comparaison : minuscules, sans accents, espaces remplacés par « - ».</summary>
    public static string Key(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsWhiteSpace(c) ? '-' : c);
        }
        return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), "-{2,}", "-").Replace('’', '\'');
    }

    /// <summary>Clé du groupe d'un type d'examen (celle que renvoie /api/categories/exams).</summary>
    public static string GroupKey(string? examType) => Key(Head(examType));

    /// <summary>Le type d'examen saisi appartient-il au groupe demandé (« iut », « bac », « fmsb »…) ?</summary>
    public static bool Matches(string? examType, string? requested)
    {
        var req = Key(requested);
        if (req.Length == 0 || string.IsNullOrWhiteSpace(examType)) return false;
        var group = GroupKey(examType);
        if (group == req || Key(examType) == req) return true;
        return Aliases.TryGetValue(req, out var names) && names.Contains(group);
    }
}
