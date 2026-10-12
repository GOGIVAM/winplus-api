using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Engagement mensuel de tutorat (lot 9.4, suivi 17.7) : les mêmes créneaux
/// hebdomadaires (<see cref="Slots"/>) réservés 4 semaines consécutives à
/// partir de <see cref="StartDate"/>, payés d'avance en une fois.
///
/// Modèle retenu : l'offre reste portée par le tuteur
/// (<see cref="TutorPackage"/> Kind = monthly : activation et remise) ;
/// l'engagement est l'achat du payeur. Il ne porte que ce qui concerne le
/// mois entier (créneaux, payeur, paiement unique, acceptation du tuteur,
/// renouvellement). Chaque séance générée est une <see cref="TutorBooking"/>
/// ordinaire (ObjectType = monthly_session, <see cref="TutorBooking.TutorEngagementId"/>)
/// avec son propre prix figé (sa part du total) : réalisation, contestation,
/// libération et remboursement restent séance par séance, avec exactement le
/// code des séances uniques (17.8).
/// </summary>
public class TutorEngagement
{
    public int Id { get; set; }

    public int TutorProfileId { get; set; }

    /// <summary>Offre mensuelle du tuteur au moment de l'achat (remise figée ci-dessous).</summary>
    public int? TutorPackageId { get; set; }

    // ── Payeur et participants (mêmes règles que le 9.3) ──
    [MaxLength(20)]
    public string PayerType { get; set; } = TutorPayerTypes.Student;
    public int PayerUserId { get; set; }
    public int BookedByUserId { get; set; }
    public int? PayerInstitutionId { get; set; }
    public int? StudyGroupId { get; set; }
    public int? TutorAssignmentId { get; set; }
    public int ParticipantCount { get; set; } = 1;
    /// <summary>Élève principal (premier participant), repris sur chaque séance.</summary>
    public int StudentUserId { get; set; }

    [MaxLength(100)]
    public string? Subject { get; set; }

    /// <summary>online | student_home | tutor_home | neutral_place.</summary>
    [MaxLength(30)]
    public string Mode { get; set; } = "online";

    /// <summary>Sujets saisis (JSON, liste de textes) : recopiés sur chaque séance et au renouvellement.</summary>
    public string? TopicsJson { get; set; }

    /// <summary>Premier jour de la période (4 semaines : StartDate à EndDate inclus).</summary>
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int Weeks { get; set; } = 4;

    // ── Prix (figés à la création) ──
    /// <summary>Remise mensuelle du tuteur appliquée (0 à 30 %).</summary>
    public int DiscountPercent { get; set; }
    /// <summary>Somme des prix des séances avant remise (grille 9.1, prix de groupe 9.3).</summary>
    public decimal SubtotalXaf { get; set; }
    /// <summary>SubtotalXaf - TotalXaf (déduit, jamais arrondi séparément).</summary>
    public decimal DiscountXaf { get; set; }
    /// <summary>Montant payé d'avance = somme exacte des PriceXaf des séances générées.</summary>
    public decimal TotalXaf { get; set; }

    // ── Paiement unique du mois ──
    /// <summary>mobile_money | wallet.</summary>
    [MaxLength(20)]
    public string PaymentMethod { get; set; } = "wallet";
    /// <summary>pending | completed | failed.</summary>
    [MaxLength(20)]
    public string PaymentStatus { get; set; } = "pending";
    [MaxLength(100)]
    public string? NotchpayReference { get; set; }
    [MaxLength(30)]
    public string? PhoneNumber { get; set; }
    /// <summary>Frais NotchPay du paiement unique, répartis entre les séances au prorata de leur prix.</summary>
    public decimal? PaymentFeeXaf { get; set; }

    /// <summary>
    /// pending_payment | pending_tutor_approval | active | completed |
    /// cancelled | rejected | expired (voir <see cref="TutorEngagementStatuses"/>).
    /// 30 caractères (« pending_tutor_approval » en compte 22).
    /// </summary>
    [MaxLength(30)]
    public string Status { get; set; } = TutorEngagementStatuses.PendingPayment;

    /// <summary>Le tuteur doit accepter ou refuser l'engagement entier avant cet instant.</summary>
    public DateTime? ResponseDeadlineAt { get; set; }
    public DateTime? RespondedAt { get; set; }

    // ── Renouvellement (manuel par défaut, automatique sur option si payé par solde) ──
    public bool AutoRenew { get; set; }
    public int? RenewedFromEngagementId { get; set; }
    public int? RenewedToEngagementId { get; set; }
    public DateTime? RenewalReminderSentAt { get; set; }
    public DateTime? AutoRenewAttemptedAt { get; set; }
    [MaxLength(300)]
    public string? AutoRenewFailureReason { get; set; }

    [MaxLength(300)]
    public string? CancellationReason { get; set; }
    public int? CancelledByUserId { get; set; }
    public DateTime? CancelledAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }

    public ICollection<TutorEngagementSlot> Slots { get; set; } = new List<TutorEngagementSlot>();

    /// <summary>Séances générées (TutorBookings, ObjectType = monthly_session).</summary>
    public ICollection<TutorBooking> Sessions { get; set; } = new List<TutorBooking>();
}

/// <summary>Créneau hebdomadaire d'un engagement : jour (0 = dimanche), heure de début, durée en tranches de 30 min.</summary>
public class TutorEngagementSlot
{
    public int Id { get; set; }
    public int TutorEngagementId { get; set; }
    public int DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public int DurationMinutes { get; set; }

    [ForeignKey(nameof(TutorEngagementId))]
    public TutorEngagement? TutorEngagement { get; set; }
}

public static class TutorEngagementStatuses
{
    public const string PendingPayment = "pending_payment";
    public const string PendingTutorApproval = "pending_tutor_approval";
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Rejected = "rejected";
    public const string Expired = "expired";

    /// <summary>États où l'engagement n'a pas (ou plus) de séances à venir financées.</summary>
    public static readonly string[] Failed = { Cancelled, Rejected, Expired };
}
