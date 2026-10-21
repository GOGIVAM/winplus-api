namespace Backend.Models.Entities;

/// <summary>
/// Contenu de la base qui porte une version anglaise (épreuve, livre,
/// formation, chapitre, leçon). Schéma : Migrations/SQL_ContentTranslations.sql.
///
/// Cycle de vie (<see cref="TranslationStates"/>) :
///  - null      : jamais demandé (contenu antérieur au lot, rattrapé par
///                l'endpoint admin POST /api/admin/translations/translate-missing) ;
///  - pending   : le français a été créé ou modifié, traduction à faire ;
///  - failed    : la dernière tentative a échoué (WinAI indisponible, quota),
///                reprise par la tâche planifiée ;
///  - ok        : version anglaise à jour du français ;
///  - review    : traduction humaine dont le français a changé depuis (« à revoir »).
/// </summary>
public interface ITranslatableContent
{
    int Id { get; }
    /// <summary>"auto" (WinAI) ou "human" (saisie ou corrigée à la main).</summary>
    string? TranslationOrigin { get; set; }
    string? TranslationState { get; set; }
    DateTime? TranslatedAt { get; set; }
    /// <summary>Empreinte SHA-256 des champs français au moment de la traduction.</summary>
    string? TranslationSourceHash { get; set; }
}

public static class TranslationOrigins
{
    public const string Auto = "auto";
    public const string Human = "human";
    /// <summary>Lot 10.7 : contenu écrit en anglais d'origine, rien à traduire.</summary>
    public const string Source = "source";
}

public static class TranslationStates
{
    public const string Pending = "pending";
    public const string Failed = "failed";
    public const string Ok = "ok";
    public const string Review = "review";
}

/// <summary>
/// Libellé fixe de taxonomie (matière, type d'examen, niveau, série, cycle,
/// sous-système, statut, session, difficulté) et sa version anglaise.
/// Pré-rempli par SQL_ContentTranslations.sql ; toute valeur nouvelle
/// rencontrée à l'enregistrement d'un contenu est ajoutée par la traduction
/// automatique (Origin = "auto", NeedsReview = true).
/// </summary>
public class ContentTranslation
{
    public int Id { get; set; }
    /// <summary>category | examType | level | series | cycle | subsystem | status | session | difficulty</summary>
    public string Kind { get; set; } = "category";
    public string ValueFr { get; set; } = string.Empty;
    public string ValueEn { get; set; } = string.Empty;
    public string Origin { get; set; } = TranslationOrigins.Human;
    /// <summary>Traduction automatique pas encore relue par un administrateur.</summary>
    public bool NeedsReview { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Cache des traductions automatiques : un texte français déjà traduit n'est
/// jamais renvoyé à WinAI (doublons entre épreuve et livre, reprises, retraductions).
/// </summary>
public class TranslationCacheEntry
{
    public int Id { get; set; }
    /// <summary>SHA-256 hexadécimal de (version du glossaire + texte source).</summary>
    public string SourceHash { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public string TargetLang { get; set; } = "en";
    /// <summary>Tokens consommés (part du lot) : coût imputé à la plateforme.</summary>
    public int TokensUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
