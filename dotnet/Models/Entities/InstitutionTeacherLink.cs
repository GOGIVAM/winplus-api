using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Affiliation prof/tuteur ↔ institution — même forme que TeacherStudentLink :
/// demande dans les deux sens (InitiatedBy), acceptation, révocation par
/// n'importe laquelle des deux parties (suppression de la ligne).
/// Status: pending | accepted | rejected
/// </summary>
public class InstitutionTeacherLink
{
    public int Id { get; set; }

    [Required]
    public int InstitutionId { get; set; }

    [Required]
    public int TeacherId { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    /// <summary>UserId de qui a initié — le compte institution (User.InstitutionId) ou le prof.</summary>
    [Required]
    public int InitiatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(InstitutionId))]
    public Institution? Institution { get; set; }

    [ForeignKey(nameof(TeacherId))]
    public User? Teacher { get; set; }

    [ForeignKey(nameof(InitiatedBy))]
    public User? Initiator { get; set; }
}
