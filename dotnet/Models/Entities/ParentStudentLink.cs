using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Liaison parent-enfant — un parent peut avoir plusieurs enfants (étudiants).
/// Status: pending | accepted | rejected. Auparavant liée instantanément sans
/// consentement de l'élève (AddChild créait la ligne directement) — corrigé
/// pour exiger l'acceptation de l'élève, comme TeacherStudentLink.
/// </summary>
public class ParentStudentLink
{
    public int Id { get; set; }

    public int ParentId { get; set; }

    public int StudentId { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    [Required]
    public int InitiatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ParentId))]
    public User? Parent { get; set; }

    [ForeignKey(nameof(StudentId))]
    public User? Student { get; set; }

    [ForeignKey(nameof(InitiatedBy))]
    public User? Initiator { get; set; }
}
