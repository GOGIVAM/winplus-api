using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Porte n°2 du réseau institution : un prof/tuteur affilié à une institution
/// voit ses élèves (lecture seule, via InstitutionStudents) mais ne peut pas
/// encore les contacter. Cette demande débloque le contact — approuvable par
/// l'élève, l'institution, OU le parent lié (le premier qui répond suffit).
/// À l'acceptation, un TeacherStudentLink "accepted" est créé.
/// Status: pending | accepted | rejected
/// </summary>
public class TeacherStudentAccessRequest
{
    public int Id { get; set; }

    [Required]
    public int TeacherId { get; set; }

    [Required]
    public int StudentId { get; set; }

    /// <summary>Institution via laquelle le prof voyait cet élève — contexte de la demande.</summary>
    [Required]
    public int InstitutionId { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    /// <summary>UserId de qui a répondu (élève, admin institution, ou parent lié) — null tant que pending.</summary>
    public int? RespondedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }

    [ForeignKey(nameof(TeacherId))]
    public User? Teacher { get; set; }

    [ForeignKey(nameof(StudentId))]
    public User? Student { get; set; }

    [ForeignKey(nameof(InstitutionId))]
    public Institution? Institution { get; set; }
}
