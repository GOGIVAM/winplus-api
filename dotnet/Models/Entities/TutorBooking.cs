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
    [MaxLength(20)]
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
    /// Compte qui a payé et qui reçoit les remboursements ; null = l'élève
    /// (réservations antérieures au 9.2). Au 9.2 c'est toujours l'élève ; le
    /// 9.3 y mettra le parent ou l'institution payeur.
    /// </summary>
    public int? PayerUserId { get; set; }

    /// <summary>Frais NotchPay figés à la confirmation du paiement (0 si portefeuille).</summary>
    public decimal? PaymentFeeXaf { get; set; }

    /// <summary>Remboursement décidé (null tant qu'aucun) : instant de la décision.</summary>
    public DateTime? RefundedAt { get; set; }

    /// <summary>Voir <c>TutorRefundCause</c> : payer_cancelled, tutor_cancelled, tutor_declined, tutor_no_response, dispute_full, dispute_partial.</summary>
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
}
