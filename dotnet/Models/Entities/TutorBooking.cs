using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Réservation d'une séance de cours particulier (Module 6). Distincte de
/// <see cref="TutorAvailabilitySlot"/> (créneau hebdomadaire type déclaré par
/// le répétiteur) : une réservation porte sur une date précise et suit son
/// propre cycle de vie (paiement, confirmation, annulation). Le paiement est
/// suivi directement ici (pas via Payment/Order, réservés au catalogue
/// épreuves/formations  voir Order.cs) pour ne pas coupler le module
/// Répétiteur au panier/checkout existant.
/// </summary>
public class TutorBooking
{
    public int Id { get; set; }

    public int TutorProfileId { get; set; }
    public int StudentUserId { get; set; }

    /// <summary>Matière de la séance (US-REP-10, saisie facultative côté élève à la réservation).</summary>
    [MaxLength(100)]
    public string? Subject { get; set; }

    public DateOnly SessionDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }

    /// <summary>online | student_home | tutor_home | neutral_place.</summary>
    [MaxLength(30)]
    public string Mode { get; set; } = "online";

    /// <summary>Prix figé à la création : jamais recalculé ensuite, même si le répétiteur change sa grille.</summary>
    public decimal PriceXaf { get; set; }

    /// <summary>
    /// Devis serveur figé à la création (lot 9.1) : détail du calcul de
    /// PriceXaf (tarif de base, modulations par tranche, dégressivité),
    /// sérialisé en JSON. Null pour les réservations antérieures au 9.1.
    /// </summary>
    public string? PriceBreakdownJson { get; set; }

    /// <summary>
    /// pending_payment (paiement en cours) → pending_tutor_approval (payé, en
    /// attente d'acceptation du répétiteur) → confirmed → completed (marquée
    /// effectuée) → disputed (contestée dans les 24 h, réglable :
    /// TutorBooking:DisputeWindowHours). Sorties : rejected
    /// (refusée par le répétiteur), expired (pas de réponse dans le délai de
    /// préavis), cancelled (paiement échoué / annulation).
    /// </summary>
    /// <summary>
    /// pending_payment | pending_tutor_approval | confirmed | completed | disputed | cancelled | rejected | expired.
    /// 30 caractères (suivi 17.14) : « pending_tutor_approval » en compte 22,
    /// l'ancienne limite de 20 faisait échouer son enregistrement en base.
    /// </summary>
    [MaxLength(30)]
    public string Status { get; set; } = "pending_payment";

    [MaxLength(100)]
    public string? NotchpayReference { get; set; }

    /// <summary>pending | completed | failed  miroir du statut NotchPay pour cette réservation.</summary>
    [MaxLength(20)]
    public string PaymentStatus { get; set; } = "pending";

    [MaxLength(30)]
    public string? PhoneNumber { get; set; }

    // ── Paiement et remboursement (lot 9.2, suivi 17.6 et 17.9) ───────────

    /// <summary>mobile_money | wallet ; null = réservation antérieure au 9.2 (Mobile Money).</summary>
    [MaxLength(20)]
    public string? PaymentMethod { get; set; }

    /// <summary>
    /// Portefeuille qui a payé et qui reçoit les remboursements ; null = l'élève
    /// (réservations antérieures au 9.2). Depuis le 9.3 : l'élève, le parent,
    /// le titulaire du portefeuille institution (<see cref="Institution.WalletOwnerUserId"/>)
    /// ou le membre du groupe d'étude qui a réservé.
    /// </summary>
    public int? PayerUserId { get; set; }

    /// <summary>Frais NotchPay figés à la confirmation du paiement (0 si portefeuille).</summary>
    public decimal? PaymentFeeXaf { get; set; }

    // ── Payeur, participants, objet (lot 9.3, suivi 17.4) ────────────────
    //
    // Le payeur (PayerUserId + PayerType) est le portefeuille débité et
    // crédité des remboursements ; les participants (TutorBookingParticipants)
    // sont les élèves qui suivent la séance ; StudentUserId reste le premier
    // participant (élève « principal »), pour les avis, l'historique élève et
    // les anciens clients. Une séance de groupe reste UNE réservation : elle
    // occupe le créneau du tuteur une seule fois.

    /// <summary>student | parent | institution | study_group (voir <see cref="TutorPayerTypes"/>) ; null = élève (réservations antérieures au 9.3).</summary>
    [MaxLength(20)]
    public string? PayerType { get; set; }

    /// <summary>Compte qui a effectivement réservé (peut différer du titulaire du portefeuille institution).</summary>
    public int? BookedByUserId { get; set; }

    /// <summary>Institution payeuse (PayerType = institution) : tout compte de cette institution suit la réservation.</summary>
    public int? PayerInstitutionId { get; set; }

    /// <summary>Groupe d'étude au nom duquel la séance est réservée (PayerType = study_group).</summary>
    public int? StudyGroupId { get; set; }

    /// <summary>Nombre de participants figé à la création (base du prix de groupe).</summary>
    public int ParticipantCount { get; set; } = 1;

    /// <summary>Attribution de tuteur (parent ou institution) à l'origine de la réservation, le cas échéant.</summary>
    public int? TutorAssignmentId { get; set; }

    /// <summary>
    /// Objet de la réservation : single_session, ou monthly_session (lot 9.4)
    /// pour une séance générée par un engagement mensuel
    /// (<see cref="TutorEngagementId"/>), qui garde son propre cycle (17.8) :
    /// réalisation, contestation, libération et remboursement séance par séance.
    /// </summary>
    [MaxLength(20)]
    public string ObjectType { get; set; } = TutorBookingObjectTypes.SingleSession;

    /// <summary>Engagement mensuel d'origine (ObjectType = monthly_session), sinon null.</summary>
    public int? TutorEngagementId { get; set; }

    [ForeignKey(nameof(TutorEngagementId))]
    public TutorEngagement? TutorEngagement { get; set; }

    /// <summary>Remboursement décidé (null tant qu'aucun) : instant de la décision.</summary>
    public DateTime? RefundedAt { get; set; }

    /// <summary>Voir <c>TutorRefundCause</c> : payer_cancelled, tutor_cancelled, tutor_declined, tutor_no_response, dispute_full, dispute_partial, in_person_blocked (lot 9.5, 17.17).</summary>
    [MaxLength(30)]
    public string? RefundCause { get; set; }

    /// <summary>Montant crédité au portefeuille du payeur.</summary>
    public decimal? RefundAmountXaf { get; set; }

    /// <summary>Frais NotchPay retenus (sur le payeur ou à retenir sur le tuteur) ; 0 si absorbés ou sans frais.</summary>
    public decimal? RefundFeeXaf { get; set; }

    /// <summary>none | payer | tutor | platform.</summary>
    [MaxLength(20)]
    public string? RefundFeeBorneBy { get; set; }

    /// <summary>Frais effectivement débités au tuteur (borné par son solde disponible au moment du remboursement).</summary>
    public decimal? TutorFeeChargedXaf { get; set; }

    /// <summary>Montant brut conservé pour le tuteur (annulation tardive, litige partiel), commission déduite au registre.</summary>
    public decimal? TutorRetainedXaf { get; set; }

    [MaxLength(300)]
    public string? CancellationReason { get; set; }
    public int? CancelledByUserId { get; set; }
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// Part remboursée à l'élève lors d'une annulation par l'élève lui-même
    /// (0-100), calculée depuis la politique d'annulation du répétiteur au
    /// moment de l'annulation. Null si annulée par le répétiteur (toujours
    /// remboursement total dans ce cas) ou pas encore annulée.
    /// </summary>
    public int? RefundPercent { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>Séance marquée "Effectuée" par le répétiteur (déclenche la fenêtre de contestation, 24 h par défaut).</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Fonds crédités au solde WinPlus du répétiteur (simulation en base  le
    /// virement Mobile Money réel reste manuel, voir TutorBookingLifecycleService).
    /// Non nul = disponible dans TeacherService.GetSpendableBalanceAsync.
    /// </summary>
    public DateTime? EscrowReleasedAt { get; set; }

    public DateTime? DisputedAt { get; set; }
    [MaxLength(500)]
    public string? DisputeReason { get; set; }

    /// <summary>Décision admin sur un litige (US-REP-09) : "refunded_full" | "refunded_partial" | "released_to_tutor".</summary>
    [MaxLength(30)]
    public string? DisputeResolution { get; set; }
    [MaxLength(500)]
    public string? DisputeResolutionNote { get; set; }
    public int? DisputeResolvedByUserId { get; set; }
    public DateTime? DisputeResolvedAt { get; set; }

    /// <summary>Transcription collée par le répétiteur pour générer le compte-rendu WinAI (US-REP-07).</summary>
    public string? TranscriptText { get; set; }
    public string? SummaryText { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }

    [ForeignKey(nameof(StudentUserId))]
    public User? Student { get; set; }

    /// <summary>Sujets à travailler saisis à la réservation (lot 9.1, suivi 17.5).</summary>
    public ICollection<TutorBookingTopic> Topics { get; set; } = new List<TutorBookingTopic>();

    /// <summary>Élèves qui suivent la séance (lot 9.3) ; au moins un, l'élève principal compris.</summary>
    public ICollection<TutorBookingParticipant> Participants { get; set; } = new List<TutorBookingParticipant>();

    [ForeignKey(nameof(PayerUserId))]
    public User? Payer { get; set; }

    [ForeignKey(nameof(StudyGroupId))]
    public StudyGroup? StudyGroup { get; set; }
}

/// <summary>Élève participant à une séance (lot 9.3, suivi 17.4).</summary>
public class TutorBookingParticipant
{
    public int Id { get; set; }
    public int TutorBookingId { get; set; }
    public int StudentUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TutorBookingId))]
    public TutorBooking? TutorBooking { get; set; }

    [ForeignKey(nameof(StudentUserId))]
    public User? Student { get; set; }
}

/// <summary>Types de payeur d'une séance (17.4).</summary>
public static class TutorPayerTypes
{
    public const string Student = "student";
    public const string Parent = "parent";
    public const string Institution = "institution";
    public const string StudyGroup = "study_group";

    public static readonly string[] All = { Student, Parent, Institution, StudyGroup };
}

/// <summary>Objet d'une réservation (17.4, 17.7).</summary>
public static class TutorBookingObjectTypes
{
    public const string SingleSession = "single_session";
    /// <summary>Séance générée par un engagement mensuel (lot 9.4).</summary>
    public const string MonthlySession = "monthly_session";
}
