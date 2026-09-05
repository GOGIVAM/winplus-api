using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Moyenne scolaire réelle d'un élève pour une année donnée (bulletin
/// d'établissement, pas une moyenne calculée depuis les quiz WinPlus). Une
/// seule ligne par (StudentId, SchoolYear) : l'élève ou son parent peut la
/// créer, l'autre peut ensuite la corriger.
/// </summary>
public class AcademicRecord
{
    public int Id { get; set; }

    public int StudentId { get; set; }

    /// <summary>Année scolaire, ex: "2025-2026".</summary>
    [Required]
    [StringLength(20)]
    public string SchoolYear { get; set; } = null!;

    /// <summary>Moyenne sur 20 (convention utilisée dans tout le reste de l'app).</summary>
    [Column(TypeName = "numeric(4,2)")]
    public decimal AverageGrade { get; set; }

    /// <summary>Qui a saisi/corrigé en dernier cette ligne  l'élève ou un de ses parents liés.</summary>
    public int RecordedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(StudentId))]
    public User? Student { get; set; }
}
