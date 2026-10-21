using System.Text.RegularExpressions;
using Backend.Models.Entities;

namespace Backend.Services.Translation;

/// <summary>
/// Lot 10.7 : langue d'origine d'une épreuve ou d'un livre.
///
/// Le sous-système anglophone (GCE Board) publie ses épreuves en anglais :
/// elles ne se traduisent pas (décision D.4 de l'étude 10). Le dépôt peut
/// préciser la langue ; sinon elle est déduite du type d'examen et du niveau
/// (GCE O/A Level, FSLC, Common Entrance, HND, Form 1-5, Lower/Upper Sixth,
/// Class 1-6, University Year, Level 100-400, Master's/Bachelor's Degree).
/// Tout le reste est en français. Même règle dans SQL_ContentSourceLanguage.sql.
/// </summary>
public static class ContentSourceLanguage
{
    private static readonly Regex Anglophone = new(
        @"\bGCE\b|\bO[\s-]?Level\b|\bA[\s-]?Level\b|Ordinary Level|Advanced Level|\bFSLC\b|First School Leaving|Common Entrance|\bHND\b"
        + @"|\bForm\s?[1-5]\b|\b(Lower|Upper)\s+Sixth\b|\bClass\s?[1-6]\b|University Year|\bLevel\s?[1-4]00\b|Master's Degree|Bachelor's Degree",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"en" ou "fr" pour une valeur reconnue, sinon null.</summary>
    public static string? Normalize(string? value) => ContentLanguage.Parse(value);

    /// <summary>Langue déduite des libellés d'un contenu : "en" pour le sous-système anglophone, sinon "fr".</summary>
    public static string Infer(params string?[] labels) =>
        labels.Any(l => !string.IsNullOrWhiteSpace(l) && Anglophone.IsMatch(l)) ? ContentLanguage.English : ContentLanguage.French;

    public static string Infer(Subject s) => Infer(s.Level, s.Title, s.Category);

    public static string Infer(Exam e) => Infer(e.ExamType, e.Level, e.Title);

    /// <summary>Le contenu est-il écrit en anglais d'origine ?</summary>
    public static bool IsEnglish(object row) => row switch
    {
        Subject s => s.Language == ContentLanguage.English,
        Exam e => e.Language == ContentLanguage.English,
        Course c => ContentLanguage.Parse(c.Language) == ContentLanguage.English,
        _ => false,
    };

    /// <summary>
    /// À la création : langue précisée au dépôt (normalisée), sinon déduite.
    /// Appelé par <see cref="ContentTranslationSaveInterceptor"/> pour tout
    /// contenu ajouté, quel que soit l'écran qui l'enregistre.
    /// </summary>
    public static void ApplyOnCreate(object row)
    {
        switch (row)
        {
            case Subject s:
                s.Language = Normalize(s.Language) ?? Infer(s);
                break;
            case Exam e:
                e.Language = Normalize(e.Language) ?? Infer(e);
                break;
        }
    }
}
