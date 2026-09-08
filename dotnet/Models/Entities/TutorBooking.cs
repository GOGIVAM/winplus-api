using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Réservation d'une séance de cours particulier (Module 6). Distincte de
/// <see cref="TutorAvailabilitySlot"/> (créneau hebdomadaire type déclaré par
/// le répétiteur) : une réservation porte sur une date précise et suit son
/// propre cycle de vie (paiement, confirmation, annulation). Le paiement est
/// suivi directement ici (pas via Payment/Order, réservés au catalogue
/// épreuves/formations — voir Order.cs) pour ne pas coupler le module
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

    public decimal PriceXaf { get; set; }

    /// <summary>
    /// pending_payment (paiement en cours) → pending_tutor_approval (payé, en
    /// attente d'acceptation du répétiteur) → confirmed → completed (marquée
    /// effectuée) → disputed (contestée &lt;2h après). Sorties : rejected
    /// (refusée par le répétiteur), expired (pas de réponse dans le délai de
    /// préavis), cancelled (paiement échoué / annulation).
    /// </summary>
    [MaxLength(20)]
    public string Status { get; set; } = "pending_payment";

    [MaxLength(100)]
    public string? NotchpayReference { get; set; }

    /// <summary>pending | completed | failed — miroir du statut NotchPay pour cette réservation.</summary>
    [MaxLength(20)]
    public string PaymentStatus { get; set; } = "pending";

    [MaxLength(30)]
    public string? PhoneNumber { get; set; }

    [MaxLength(300)]
    public string? CancellationReason { get; set; }
    public int? CancelledByUserId { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>Séance marquée "Effectuée" par le répétiteur (déclenche la fenêtre de contestation de 2h).</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Fonds crédités au solde WinPlus du répétiteur (simulation en base — le
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
}
