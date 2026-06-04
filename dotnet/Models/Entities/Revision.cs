using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Revision Entity - Fiches de révision et exercices ciblés
/// </summary>
[Table("Revisions")]
public class Revision
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Titre de la révision (ex: "Révision: Intégrales - Niveau avancé")
    /// </summary>
    [Required]
    [StringLength(255)]
    public string Title { get; set; } = null!;

    /// <summary>
    /// Description de la révision
    /// </summary>
    [StringLength(2000)]
    public string? Description { get; set; }

    /// <summary>
    /// Matière couverte
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Subject { get; set; } = null!;

    /// <summary>
    /// Thème/Chapitre (ex: "Intégrales", "Trigonométrie")
    /// </summary>
    [StringLength(100)]
    public string? Topic { get; set; }

    /// <summary>
    /// ID du Subject associé
    /// </summary>
    public int? SubjectId { get; set; }

    /// <summary>
    /// ID de l'Exam associé (optionnel)
    /// </summary>
    public int? ExamId { get; set; }

    /// <summary>
    /// Type de révision (Video, PDF, Exercises, Theory, Summary, etc.)
    /// </summary>
    [Required]
    [StringLength(50)]
    public string Type { get; set; } = "Theory"; // Theory, Exercises, MixedContent

    /// <summary>
    /// Niveau de difficulté
    /// </summary>
    public int? Difficulty { get; set; } = 2; // 1-5

    /// <summary>
    /// Contenu principal (Markdown ou HTML)
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// URL vers vidéo (optionnel)
    /// </summary>
    [StringLength(500)]
    public string? VideoUrl { get; set; }

    /// <summary>
    /// URL vers document PDF (optionnel)
    /// </summary>
    [StringLength(500)]
    public string? DocumentUrl { get; set; }

    /// <summary>
    /// Durée estimée en minutes
    /// </summary>
    public int? DurationMinutes { get; set; }

    /// <summary>
    /// Qui la révision a créée/suggérée
    /// </summary>
    public int? CreatedByUserId { get; set; }

    /// <summary>
    /// Statut (Assigned, InProgress, Completed)
    /// </summary>
    [StringLength(50)]
    public string Status { get; set; } = "Available";

    /// <summary>
    /// Est assignée automatiquement basée sur score faible?
    /// </summary>
    public bool IsAutoAssigned { get; set; } = false;

    /// <summary>
    /// Score minimum qui déclenche cette révision (ex: 60 = si score < 60%)
    /// </summary>
    public int? TriggeredByScoreThreshold { get; set; }

    /// <summary>
    /// Score après la révision (pour tracker l'amélioration)
    /// </summary>
    [Column(TypeName = "numeric(5,2)")]
    public decimal? ImprovedScore { get; set; }

    /// <summary>
    /// Est publiée?
    /// </summary>
    public bool IsPublished { get; set; } = true;

    /// <summary>
    /// Est supprimée (soft delete)
    /// </summary>
    public bool IsDeleted { get; set; } = false;

    /// <summary>
    /// Nombre de fois consultée
    /// </summary>
    public int Views { get; set; } = 0;

    /// <summary>
    /// Nombre de fois complétée
    /// </summary>
    public int Completions { get; set; } = 0;

    /// <summary>
    /// Tags/Keywords
    /// </summary>
    [StringLength(1000)]
    public string? Tags { get; set; } // JSON: ["Préparation", "ExamenFinal"]

    // Timestamps
    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? PublishedAt { get; set; }

    // Relations
    [ForeignKey(nameof(SubjectId))]
    public virtual Subject? Subject_Reference { get; set; }

    [ForeignKey(nameof(ExamId))]
    public virtual Exam? Exam_Reference { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public virtual User? CreatedByUser { get; set; }

    public virtual ICollection<RevisionEnrollment> UserEnrollments { get; set; } = new List<RevisionEnrollment>();
}

/// <summary>
/// RevisionEnrollment Entity - Suivi de la progression utilisateur dans une révision
/// </summary>
[Table("RevisionEnrollments")]
public class RevisionEnrollment
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Utilisateur assigné à cette révision
    /// </summary>
    [Required]
    public int UserId { get; set; }

    /// <summary>
    /// Révision assignée
    /// </summary>
    [Required]
    public int RevisionId { get; set; }

    /// <summary>
    /// LearningHistory d'origine qui a déclenché cette révision (optionnel)
    /// </summary>
    public int? AssociatedLearningHistoryId { get; set; }

    /// <summary>
    /// Score original qui a déclenché la révision
    /// </summary>
    [Column(TypeName = "numeric(5,2)")]
    public decimal? OriginalScore { get; set; }

    /// <summary>
    /// Statut (Assigned, InProgress, Completed)
    /// </summary>
    [StringLength(50)]
    public string Status { get; set; } = "Assigned";

    /// <summary>
    /// Pourcentage complété (0-100)
    /// </summary>
    [Column(TypeName = "numeric(5,2)")]
    public decimal ProgressPercentage { get; set; } = 0;

    /// <summary>
    /// Est complétée?
    /// </summary>
    public bool IsCompleted { get; set; } = false;

    /// <summary>
    /// Score après révision
    /// </summary>
    [Column(TypeName = "numeric(5,2)")]
    public decimal? FinalScore { get; set; }

    /// <summary>
    /// Amélioration en points (FinalScore - OriginalScore)
    /// </summary>
    [Column(TypeName = "numeric(5,2)")]
    public decimal? ScoreImprovement { get; set; }

    // Timestamps
    [Required]
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    // Relations
    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }

    [ForeignKey(nameof(RevisionId))]
    public virtual Revision? Revision { get; set; }

    [ForeignKey(nameof(AssociatedLearningHistoryId))]
    public virtual LearningHistory? AssociatedLearningHistory { get; set; }
}
