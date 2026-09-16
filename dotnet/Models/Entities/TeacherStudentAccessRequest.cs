using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Porte n°2 du réseau institution : un prof/tuteur affilié à une institution
/// voit ses élèves (lecture seule, via InstitutionStudents) mais ne peut pas
/// encore les contacter. Cette demande débloque le contact, via une hiérarchie
/// à deux étapes (corrigé après une première version "premier qui répond
/// gagne", sans hiérarchie en cas de désaccord — voir parent_decisions_session.md) :
///
///   Étape 1 (Stage="institution") : l'institution valide un fait administratif
///   (l'enseignant lui est bien affilié) — pas un consentement. Un refus ici est
///   immédiat et définitif ; l'élève et le parent ne sont jamais notifiés d'une
///   demande qui n'a pas passé ce filtre.
///
///   Étape 2 (Stage="consent") : seuls l'élève OU un parent lié (accepted)
///   peuvent désormais répondre — l'institution ne peut plus agir. Le premier
///   des deux qui répond est définitif : un refus bloque l'autre (même non
///   encore répondu), une acceptation rend la seconde réponse sans effet.
///
/// À l'acceptation finale, un TeacherStudentLink "accepted" est créé.
/// Status: pending | accepted | rejected (le Stage précise à quelle étape le
/// statut "pending" se trouve).
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

    /// <summary>"institution" (étape 1, filtre administratif) ou "consent" (étape 2, élève/parent).</summary>
    [Required]
    [MaxLength(20)]
    public string Stage { get; set; } = "institution";

    /// <summary>UserId de l'admin institution qui a validé l'étape 1 — null tant que non franchie.</summary>
    public int? InstitutionApprovedBy { get; set; }
    public DateTime? InstitutionRespondedAt { get; set; }

    /// <summary>UserId de qui a donné le consentement final (élève ou parent lié) — null tant que pending.</summary>
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
