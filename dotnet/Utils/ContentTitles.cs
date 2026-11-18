using System.Text.RegularExpressions;

namespace Backend.Utils;

/// <summary>
/// Titres d'épreuves saisis comme des noms de fichier (« Bac_D_Cameroun_2022 ») :
/// les _ deviennent des espaces et les espaces répétés sont réduits, à chaque
/// enregistrement. Les titres déjà en base sont repris par
/// Migrations/SQL_SubjectTitlesAndOAuthAccounts.sql.
/// </summary>
public static class ContentTitles
{
    private static readonly Regex Underscores = new("_+", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s{2,}", RegexOptions.Compiled);

    public static string Clean(string title)
        => Spaces.Replace(Underscores.Replace(title, " "), " ").Trim();
}
