using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Attribution d'un tuteur (lot 9.3, suivi 17.4) : un parent attribue un tuteur
/// à un ou plusieurs de ses enfants, une institution à un groupe ou une classe
/// de ses élèves. Relation suivie : le tuteur l'accepte ou la refuse, elle
/// reste active jusqu'à ce que l'une des parties y mette fin.
///
/// L'attribution facilite la réservation (tuteur proposé en premier, élèves
/// présélectionnés) mais ne réserve jamais rien automatiquement. Point
/// d'extension 9.4 : un engagement mensuel pourra être souscrit à partir d'une
/// attribution active.
/// </summary>
public class TutorAssignment
{
    public int Id { get; set; }

    public int TutorProfileId { get; set; }

    /// <summary>Compte qui a créé l'attribution (parent, ou compte institution).</summary>
    public int AssignedByUserId { get; set; }

    /// <summary>parent | institution.</summary>
    [MaxLength(20)]
    public string AssignerType { get; set; } = TutorPayerTypes.Parent;

    /// <summary>Institution qui attribue (AssignerType = institution) : tous ses comptes la suivent.</summary>
    public int? InstitutionId { get; set; }

    /// <summary>Classe ou groupe de l'institution visé (InstitutionStudents.GroupName), pour l'affichage.</summary>
    [MaxLength(100)]
    public string? GroupName { get; set; }

    [MaxLength(100)]
    public string? Subject { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    /// <summary>pending (en attente du tuteur) | active | declined | ended (voir <see cref="TutorAssignmentStatuses"/>).</summary>
    [MaxLength(20)]
    public string Status { get; set; } = TutorAssignmentStatuses.Pending;

    [MaxLength(300)]
    public string? ResponseNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? EndedByUserId { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }

    [ForeignKey(nameof(AssignedByUserId))]
    public User? AssignedBy { get; set; }

    public ICollection<TutorAssignmentBeneficiary> Beneficiaries { get; set; } = new List<TutorAssignmentBeneficiary>();
}

/// <summary>Élève bénéficiaire d'une attribution.</summary>
public class TutorAssignmentBeneficiary
{
    public int Id { get; set; }
    public int TutorAssignmentId { get; set; }
    public int StudentUserId { get; set; }

    [ForeignKey(nameof(TutorAssignmentId))]
    public TutorAssignment? TutorAssignment { get; set; }

    [ForeignKey(nameof(StudentUserId))]
    public User? Student { get; set; }
}

public static class TutorAssignmentStatuses
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Declined = "declined";
    public const string Ended = "ended";
}
