using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Valeurs possibles de Goal.Status. Pas de type enum Postgres natif (aucune
/// entité de ce backend n'en utilise  convention ici est une chaîne
/// contrainte côté code, comme ParentStudentLink.Status ou
/// TeacherStudentAccessRequest.Status).
/// </summary>
public static class GoalStatus
{
    public const string Pending = "Pending";     // proposé par un parent, en attente de réponse de l'élève
    public const string Accepted = "Accepted";   // réservé pour un futur flux à deux étapes ; le flux actuel passe directement à Active
    public const string Refused = "Refused";     // proposition refusée par l'élève
    public const string Active = "Active";       // objectif en cours (créé par l'élève, ou accepté/modifié depuis une proposition)
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}

/// <summary>
/// Objectif d'apprentissage d'un élève. Deux origines possibles :
///   - créé directement par l'élève (ProposedByUserId = null, Status = Active dès la création) ;
///   - proposé par un parent lié (ProposedByUserId = parentId, Status = Pending),
///     que l'élève accepte, modifie (et active du même geste) ou refuse.
/// Voir GoalsController pour le workflow de proposition.
/// </summary>
public class Goal
{
    public int Id { get; set; }

    /// <summary>Propriétaire de l'objectif : toujours l'élève, jamais le parent proposant.</summary>
    public int UserId { get; set; }

    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Type { get; set; } // academic, personal, skill, etc.
    public int? Progress { get; set; } = 0; // 0-100%

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = GoalStatus.Active;

    /// <summary>Parent à l'origine de la proposition ; null si créé directement par l'élève.</summary>
    public int? ProposedByUserId { get; set; }

    public DateTime TargetDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(ProposedByUserId))]
    public User? ProposedByUser { get; set; }
}
