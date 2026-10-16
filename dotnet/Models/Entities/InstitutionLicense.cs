using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Lot 9.7b, étape 4 (étude 9.7b section 2.3, décisions 18.11 et 18.21) :
/// licence d'utilisation d'un établissement, par siège élève, sur l'année
/// scolaire (1er septembre au 31 juillet) ou un trimestre. Prix TTC par élève
/// et par an selon le volume (6 000 / 4 800 / 3 600 XAF, minimum 20 sièges),
/// ou négocié par l'administration ; un siège enseignant pour 15 élèves
/// (3 au minimum). Le prix est figé à la souscription.
/// </summary>
public class InstitutionLicense
{
    public int Id { get; set; }
    public int InstitutionId { get; set; }

    /// <summary><see cref="LicenseTiers"/> : school, establishment, network, negotiated, trial.</summary>
    [MaxLength(20)]
    public string Tier { get; set; } = LicenseTiers.School;

    /// <summary><see cref="LicenseStatuses"/> : trialing, active, past_due, expired, cancelled.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = LicenseStatuses.Active;

    /// <summary>year (année scolaire) ou quarter.</summary>
    [MaxLength(20)]
    public string Term { get; set; } = LicenseTerms.SchoolYear;

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime? GraceUntil { get; set; }

    public int StudentSeats { get; set; }
    public int TeacherSeats { get; set; }

    [Column(TypeName = "numeric(12,0)")]
    public decimal PricePerSeatTtc { get; set; }

    [Column(TypeName = "numeric(14,0)")]
    public decimal TotalPriceTtc { get; set; }

    public bool IsNegotiated { get; set; }
    public bool AutoRenew { get; set; }
    public bool CancelAtPeriodEnd { get; set; }

    /// <summary>Budget WinAI de l'établissement pour ses propres fonctions IA (tokens, sur la durée de la licence).</summary>
    public long AiBudgetTokens { get; set; }

    public int? PromoCodeId { get; set; }
    public int? CreatedByUserId { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>Dernier rappel de renouvellement (préavis de 30 jours).</summary>
    public DateTime? RenewalNoticeSentAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Institution? Institution { get; set; }
    public ICollection<InstitutionSeat> Seats { get; set; } = new List<InstitutionSeat>();
}

/// <summary>Siège attribué nominativement à un élève (rattachement accepté) ou à un enseignant affilié.</summary>
public class InstitutionSeat
{
    public int Id { get; set; }
    public int LicenseId { get; set; }

    /// <summary>student ou teacher.</summary>
    [MaxLength(20)]
    public string Kind { get; set; } = SeatKinds.Student;

    public int UserId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReleasedAt { get; set; }
    public int? AssignedByUserId { get; set; }

    public InstitutionLicense? License { get; set; }
}

public static class LicenseTiers
{
    public const string School = "school";
    public const string Establishment = "establishment";
    public const string Network = "network";
    public const string Negotiated = "negotiated";
    public const string Trial = "trial";
}

public static class LicenseStatuses
{
    public const string Trialing = "trialing";
    public const string Active = "active";
    public const string PastDue = "past_due";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";
}

public static class LicenseTerms
{
    public const string SchoolYear = "year";
    public const string Quarter = "quarter";
}

public static class SeatKinds
{
    public const string Student = "student";
    public const string Teacher = "teacher";
}
