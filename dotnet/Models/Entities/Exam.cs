using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Backend.Models.Entities;

[Table("Exams")]
public class Exam : ITranslatableContent
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(255)]
    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    [Required]
    [StringLength(100)]
    public string ExamType { get; set; } = null!;

    [Column("Category")]
    [Required]
    [StringLength(100)]
    public string Category { get; set; } = null!;

    [Required]
    public int Year { get; set; }

    [StringLength(50)]
    public string? Session { get; set; }

    [StringLength(100)]
    public string? Level { get; set; }

    [Column("Duration")]
    public int? DurationMinutes { get; set; }

    // Module 44 (décision §11.4) : l'adresse du fichier de l'épreuve et de
    // son corrigé ne doit sortir dans aucune réponse d'API, même si l'entité
    // venait à être sérialisée telle quelle par une route oubliée. Les
    // écrans d'administration la projettent explicitement (AdminExamsController).
    [Column("DocumentUrl")]
    [StringLength(500)]
    [JsonIgnore]
    public string? DocumentUrl { get; set; }

    [StringLength(500)]
    [JsonIgnore]
    public string? CorrectionUrl { get; set; }

    /// <summary>
    /// Image représentative de l'épreuve (page de garde, photo du sujet).
    /// Optionnelle : sans elle, le catalogue garde son motif coloré.
    /// Colonne ajoutée par Migrations/sql/20260829_admin_fixes.sql.
    /// </summary>
    [StringLength(500)]
    public string? ThumbnailUrl { get; set; }

    [StringLength(50)]
    public string? Difficulty { get; set; }

    [Column("DownloadCount")]
    public int DownloadCount { get; set; } = 0;

    public bool IsPublished { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
    public int? SubjectId { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    [StringLength(255)]
    public string? TitleEn { get; set; }
    public string? DescriptionEn { get; set; }

    /// <summary>
    /// Lot 10.7 : langue d'origine du contenu ("fr" ou "en", null = français).
    /// Une épreuve GCE est écrite en anglais : elle n'est jamais traduite et
    /// garde lang="en" sur les pages françaises (SQL_ContentSourceLanguage.sql).
    /// </summary>
    [StringLength(5)]
    public string? Language { get; set; }

    /// <summary>
    /// Lot 10.8 : pays du contenu (ISO 3166-1 alpha-2). Tout le catalogue est
    /// camerounais pour l'instant ; un pays pilote CEMAC aura ses propres
    /// épreuves (SQL_CountryDimension.sql).
    /// </summary>
    [StringLength(2)]
    public string Country { get; set; } = "CM";

    // ── Version anglaise (SQL_ContentTranslations.sql) ──
    // Remplie par la traduction automatique WinAI (Origin = "auto") ou saisie
    // à la main (Origin = "human", jamais écrasée par la traduction automatique).
    public string? TranslationOrigin { get; set; }
    public string? TranslationState { get; set; }
    public DateTime? TranslatedAt { get; set; }
    public string? TranslationSourceHash { get; set; }

    // Relations
    [ForeignKey(nameof(SubjectId))]
    public virtual Subject? SubjectReference { get; set; }

    // Navigation properties
    public virtual ICollection<Quiz>? Quizzes { get; set; } = new List<Quiz>();
    public virtual ICollection<Revision>? Revisions { get; set; } = new List<Revision>();
}
