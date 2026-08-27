using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Historique des téléchargements d'épreuves.
/// Une ligne par téléchargement réussi : sert aux statistiques
/// hebdomadaires, à l'historique de l'élève et aux rapports parents.
/// </summary>
public class DownloadHistory
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public int SubjectId { get; set; }

    /// <summary>Épreuve (Exam) effectivement servie, si connue.</summary>
    public int? ExamId { get; set; }

    /// <summary>Nom du fichier proposé au téléchargement.</summary>
    [MaxLength(300)]
    public string? FileName { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }

    [ForeignKey(nameof(SubjectId))]
    public virtual Subject? Subject { get; set; }
}
