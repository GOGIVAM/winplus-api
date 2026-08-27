using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Objectif hebdomadaire défini par l'élève.
/// Une ligne par utilisateur et par semaine (lundi = clé de semaine).
/// </summary>
public class WeeklyGoal
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    /// <summary>Lundi (00:00 UTC) de la semaine concernée.</summary>
    [Required]
    [Column(TypeName = "timestamp with time zone")]
    public DateTime WeekStart { get; set; }

    /// <summary>Heures d'étude visées sur la semaine.</summary>
    [Range(0, 200)]
    public int? StudyHoursTarget { get; set; }

    /// <summary>Nombre de quiz visés.</summary>
    [Range(0, 200)]
    public int? QuizTarget { get; set; }

    /// <summary>Nombre d'épreuves à télécharger.</summary>
    [Range(0, 200)]
    public int? DownloadsTarget { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }
}
