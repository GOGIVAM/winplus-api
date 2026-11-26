using System.Text.RegularExpressions;

namespace Backend.Utils;

/// <summary>
/// Libellés scolaires saisis sans accent (« Troisieme », « Premiere », « Baccalaureat C », « entree ENSET »),
/// relevés dans la tournée du mobile du 26/11/2026 : ramenés à la forme accentuée utilisée par l'application
/// (classes en chiffres « 3ème », comme les listes du site et du mobile). Appliqué à l'enregistrement du
/// niveau et de l'examen visé ; la migration SQL_SchoolLabelsAccents.sql corrige les valeurs déjà en base.
/// </summary>
public static class SchoolLabels
{
    private static readonly (Regex Pattern, string Replacement)[] Rules =
    {
        (Word("sixieme|sixième"), "6ème"),
        (Word("cinquieme|cinquième"), "5ème"),
        (Word("quatrieme|quatrième"), "4ème"),
        (Word("troisieme|troisième"), "3ème"),
        (Word("([3-6])eme"), "$1ème"),
        (Word("premiere"), "Première"),
        (Word("baccalaureat"), "Baccalauréat"),
        (Word("entree"), "entrée"),
        (Word("ecole"), "École"),
        (Word("genie"), "Génie"),
        (Word("medecine"), "Médecine"),
        (Word("superieure?"), "Supérieure"),
    };

    private static Regex Word(string alternatives) =>
        new($@"(?<![\p{{L}}\d])(?:{alternatives})(?![\p{{L}}])", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Forme accentuée des mots scolaires connus ; le reste de la saisie est gardé tel quel.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var s = value.Trim();
        foreach (var (pattern, replacement) in Rules)
            s = pattern.Replace(s, m =>
            {
                var r = m.Result(replacement);
                // « entrée », « École »… : casse du mot saisi pour la première lettre.
                if (r.Length > 0 && char.IsLetter(r[0]) && char.IsLetter(m.Value[0]))
                    r = (char.IsUpper(m.Value[0]) ? char.ToUpperInvariant(r[0]) : char.ToLowerInvariant(r[0])) + r[1..];
                return r;
            });
        return s;
    }
}
