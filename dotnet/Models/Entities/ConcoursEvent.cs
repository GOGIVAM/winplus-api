using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

public class ConcoursEvent
{
    public int Id { get; set; }

    /// Slug identifiant le concours (ens, polytechnique, fmsb, enam, ...)
    [Required, MaxLength(50)]
    public string Slug { get; set; } = string.Empty;

    /// <summary>Lot 10.8 : pays de l'examen ou du concours (ISO 3166-1 alpha-2).</summary>
    [Required, MaxLength(2)]
    public string Country { get; set; } = "CM";

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public int Year { get; set; }

    public DateTime? RegistrationStartDate { get; set; }

    public DateTime? RegistrationEndDate { get; set; }

    public DateTime? ExamDate { get; set; }

    public DateTime? ResultsDate { get; set; }

    [MaxLength(300)]
    public string? Location { get; set; }

    /// Frais d'inscription en XAF (null = non précisé)
    public int? EnrollmentFeeXaf { get; set; }

    /// URL du portail officiel d'inscription
    [MaxLength(500)]
    public string? OfficialRegistrationUrl { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>Conseils de réussite affichés sur la page du concours (Module 2, US-CAT-09).</summary>
    [MaxLength(2000)]
    public string? Tips { get; set; }

    /// <summary>FAQ du concours, JSON : [{"question":"...","answer":"..."}].</summary>
    public string? FaqJson { get; set; }

    // ── Lot 10.4 : contenus éditoriaux (écran admin « Contenus des concours ») ──
    // Texte simple ou Markdown léger (titres « ## », listes « - », gras « ** »).
    // Colonnes ajoutées par Migrations/SQL_ConcoursEventsEditorial.sql.

    /// <summary>Programme officiel des épreuves.</summary>
    public string? Programme { get; set; }

    /// <summary>Conditions d'admission (diplôme, âge, nationalité...).</summary>
    public string? AdmissionConditions { get; set; }

    /// <summary>Filières, spécialités ou divisions ouvertes.</summary>
    public string? Tracks { get; set; }

    /// <summary>Épreuves et coefficients.</summary>
    public string? Coefficients { get; set; }

    /// <summary>
    /// Source des informations officielles (programme, conditions, coefficients,
    /// dates) : une URL par ligne, la première étant la source principale.
    /// </summary>
    [MaxLength(1000)]
    public string? SourceUrl { get; set; }

    /// <summary>Date de consultation de la source.</summary>
    public DateTime? SourceCheckedAt { get; set; }

    // Versions anglaises facultatives (même structure, suffixe En). Vides :
    // la page anglaise affiche le texte français avec lang="fr".
    [MaxLength(200)]
    public string? NameEn { get; set; }

    [MaxLength(1000)]
    public string? NotesEn { get; set; }

    [MaxLength(2000)]
    public string? TipsEn { get; set; }

    /// <summary>FAQ anglaise, même format que <see cref="FaqJson"/>.</summary>
    public string? FaqJsonEn { get; set; }

    public string? ProgrammeEn { get; set; }

    public string? AdmissionConditionsEn { get; set; }

    public string? TracksEn { get; set; }

    public string? CoefficientsEn { get; set; }

    /// <summary>
    /// Origine des champs *En (SQL_ContentTranslations.sql) : JSON
    /// {"auto": {"NameEn": "<empreinte du français>"}, "human": {...}}.
    /// Un champ listé dans "auto" a été rempli par WinAI et sera refait si le
    /// français change ; un champ "human" n'est jamais écrasé.
    /// </summary>
    public string? TranslationMetaJson { get; set; }

    public bool IsPublished { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
