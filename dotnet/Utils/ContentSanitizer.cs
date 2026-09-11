using System.Text.RegularExpressions;

namespace Backend.Utils;

/// <summary>
/// Censure automatique des numéros de téléphone dans la messagerie et le
/// forum — évite que des utilisateurs se donnent rendez-vous hors plateforme
/// (contournement des paiements/réservations WinPlus) en échangeant leur
/// numéro dans un message ou un post.
/// </summary>
public static class ContentSanitizer
{
    private const string Mask = "[numéro masqué]";

    // Numéro camerounais (6XXXXXXXX, avec ou sans indicatif +237/237, espaces
    // ou séparateurs facultatifs entre les groupes de chiffres).
    private static readonly Regex CameroonPhone = new(
        @"(?<![\d])(?:\+?237[\s.\-]?)?6\d(?:[\s.\-]?\d){7}(?![\d])",
        RegexOptions.Compiled);

    // Tout numéro international explicite (préfixé par +) — pas de repli sur
    // une simple suite de chiffres nus : un contenu pédagogique (grand
    // nombre, matricule, résultat de calcul...) ne doit pas être censuré par
    // erreur faute d'indicatif reconnaissable.
    private static readonly Regex InternationalPhone = new(
        @"(?<![\d])\+\d(?:[\s.\-]?\d){7,14}(?![\d])",
        RegexOptions.Compiled);

    public static string CensorPhoneNumbers(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var result = CameroonPhone.Replace(text, Mask);
        result = InternationalPhone.Replace(result, Mask);
        return result;
    }
}
