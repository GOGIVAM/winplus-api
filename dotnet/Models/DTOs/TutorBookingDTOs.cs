namespace Backend.Models.DTOs;

/// <summary>Demande de réservation d'une séance (US-ELV-01, Module 6).</summary>
public class CreateTutorBookingRequestDto
{
    public int TutorUserId { get; set; }
    public DateOnly SessionDate { get; set; }
    /// <summary>Format "HH:mm", aligné sur une tranche de 30 min (lot 9.1).</summary>
    public string StartTime { get; set; } = null!;
    /// <summary>
    /// Format "HH:mm". Facultatif depuis le lot 9.1 si <see cref="DurationMinutes"/>
    /// est fourni ; conservé pour les anciens clients qui envoient début + fin.
    /// </summary>
    public string? EndTime { get; set; }
    /// <summary>Durée en minutes, multiple de 30 (prioritaire sur EndTime).</summary>
    public int? DurationMinutes { get; set; }
    /// <summary>online | student_home | tutor_home | neutral_place.</summary>
    public string Mode { get; set; } = "online";
    /// <summary>Numéro Mobile Money pour le paiement NotchPay.</summary>
    public string Phone { get; set; } = null!;
    /// <summary>Matière de la séance : détermine le tarif de base (grille 17.3) et la ventilation des revenus (US-REP-10).</summary>
    public string? Subject { get; set; }
    /// <summary>Sujets à travailler (17.5), textes courts, 5 au plus.</summary>
    public List<string>? Topics { get; set; }
}

/// <summary>Demande de devis (lot 9.1) : le prix affiché avant paiement vient toujours du serveur.</summary>
public class TutorPriceQuoteRequestDto
{
    public int TutorUserId { get; set; }
    public DateOnly SessionDate { get; set; }
    /// <summary>Format "HH:mm".</summary>
    public string StartTime { get; set; } = null!;
    public int DurationMinutes { get; set; }
    public string Mode { get; set; } = "online";
    public string? Subject { get; set; }
}

/// <summary>
/// Devis calculé par <c>TutorPricing.Compute</c>. Renvoyé par l'endpoint de
/// devis et figé tel quel sur la réservation (TutorBooking.PriceBreakdownJson).
/// </summary>
public class TutorPriceQuoteDto
{
    public string SessionDate { get; set; } = null!;
    public string StartTime { get; set; } = null!;
    public string EndTime { get; set; } = null!;
    public int DurationMinutes { get; set; }
    public string Mode { get; set; } = null!;
    /// <summary>"online" | "in_person".</summary>
    public string ModeCategory { get; set; } = null!;
    public string? Subject { get; set; }

    /// <summary>Tarif horaire de base retenu, avant modulation.</summary>
    public decimal BaseHourlyRateXaf { get; set; }
    /// <summary>subject_mode | subject | mode | all | profile_default : quelle règle de la grille a fourni le tarif de base.</summary>
    public string BaseRateSource { get; set; } = null!;

    public List<TutorPriceQuoteSegmentDto> Segments { get; set; } = new();

    /// <summary>Somme des tranches, arrondie à l'unité XAF.</summary>
    public decimal SubtotalXaf { get; set; }
    public int DurationDiscountPercent { get; set; }
    /// <summary>SubtotalXaf - TotalXaf (déduit, jamais arrondi séparément : décision §4.E).</summary>
    public decimal DurationDiscountXaf { get; set; }
    /// <summary>Montant facturé, arrondi à 0 décimale.</summary>
    public decimal TotalXaf { get; set; }

    /// <summary>Renseignés par l'endpoint de devis uniquement (pas figés sur la réservation).</summary>
    public bool? IsAvailable { get; set; }
    public string? UnavailableReason { get; set; }
}

/// <summary>Suite de tranches de 30 min consécutives au même tarif effectif.</summary>
public class TutorPriceQuoteSegmentDto
{
    public string StartTime { get; set; } = null!;
    public string EndTime { get; set; } = null!;
    public int Minutes { get; set; }
    /// <summary>Somme des modulations appliquées à ces tranches (bornée à -50/+100 %).</summary>
    public int ModifierPercent { get; set; }
    public List<string> ModifierLabels { get; set; } = new();
    /// <summary>Tarif horaire effectif (base modulée).</summary>
    public decimal EffectiveHourlyRateXaf { get; set; }
    /// <summary>Montant de la ligne, arrondi pour l'affichage (le total est calculé sur les montants exacts).</summary>
    public decimal AmountXaf { get; set; }
}

public class TutorBookingDto
{
    public int Id { get; set; }
    public int TutorUserId { get; set; }
    public string? TutorName { get; set; }
    public string? TutorAvatarUrl { get; set; }
    public int StudentUserId { get; set; }
    public string? StudentName { get; set; }
    public DateOnly SessionDate { get; set; }
    public string StartTime { get; set; } = null!;
    public string EndTime { get; set; } = null!;
    public string Mode { get; set; } = null!;
    public decimal PriceXaf { get; set; }
    public string Status { get; set; } = null!;
    public string PaymentStatus { get; set; } = null!;
    public string? NotchpayReference { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? EscrowReleasedAt { get; set; }
    public DateTime? DisputedAt { get; set; }
    public string? DisputeReason { get; set; }
    public string? DisputeResolution { get; set; }
    public string? DisputeResolutionNote { get; set; }
    public DateTime? DisputeResolvedAt { get; set; }
    public string? Subject { get; set; }
    /// <summary>Sujets à travailler saisis à la réservation (17.5).</summary>
    public List<string> Topics { get; set; } = new();
    public int DurationMinutes { get; set; }
    /// <summary>Devis figé à la création ; null pour les réservations antérieures au lot 9.1.</summary>
    public TutorPriceQuoteDto? PriceBreakdown { get; set; }
    public string? SummaryText { get; set; }
    /// <summary>true si l'heure de fin de la séance est passée  active "Marquer effectuée" côté répétiteur.</summary>
    public bool CanMarkCompleted { get; set; }
    /// <summary>true si encore dans la fenêtre de contestation de 2h côté élève.</summary>
    public bool CanDispute { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Génération WinAI du compte-rendu de séance (US-REP-07).</summary>
public class GenerateTutorBookingSummaryRequestDto
{
    public string TranscriptText { get; set; } = null!;
}

public class UpdateTutorBookingSummaryRequestDto
{
    public string SummaryText { get; set; } = null!;
}

/// <summary>Décision du support sur un litige (US-REP-09).</summary>
public class ResolveTutorBookingDisputeRequestDto
{
    /// <summary>refunded_full | refunded_partial | released_to_tutor.</summary>
    public string Resolution { get; set; } = null!;
    public string? Note { get; set; }

    /// <summary>
    /// Montant remboursé à l'élève, en XAF (Module 29). Obligatoire et borné
    /// par le montant bloqué en escrow pour <c>refunded_partial</c> ; ignoré
    /// pour les deux autres issues (déduit intégralement du montant bloqué).
    /// </summary>
    public decimal? AmountXaf { get; set; }
}

/// <summary>Réservation en attente de décision du répétiteur, avec délai restant (US-REP-05).</summary>
public class TutorPendingBookingDto
{
    public TutorBookingDto Booking { get; set; } = null!;
    public int MinutesRemaining { get; set; }
}

public class DeclineTutorBookingRequestDto
{
    public string? Reason { get; set; }
}

public class DisputeTutorBookingRequestDto
{
    public string Reason { get; set; } = null!;
}

public class TutorBookingCreatedResponseDto
{
    public TutorBookingDto Booking { get; set; } = null!;
    public string? NotchpayAuthorizationUrl { get; set; }
}

public class CancelTutorBookingRequestDto
{
    public string? Reason { get; set; }
}

/// <summary>
/// Point de départ réservable dans une plage de disponibilité récurrente,
/// pour la vue calendrier côté élève (Module 6, lot 9.1). Une plage de 4h
/// génère un point de départ par tranche de 30 min ; depuis chacun, l'élève
/// choisit librement sa durée (multiple de 30 min) jusqu'à la fin de la
/// partie libre en continu. Plusieurs personnes réservent ainsi à la suite
/// dans la même plage, sans chevauchement.
/// </summary>
public class TutorAvailabilityOccurrenceDto
{
    public DateOnly Date { get; set; }
    public string StartTime { get; set; } = null!;
    /// <summary>Durées (en minutes) réservables depuis ce point de départ : 30, 60, 90… jusqu'à MaxDurationMinutes. Jamais vide dans la réponse.</summary>
    public List<int> AvailableDurationsMinutes { get; set; } = new();
    /// <summary>Durée maximale réservable depuis ce point (fin de la plage libre en continu, bornée par TutorBooking:MaxSessionMinutes).</summary>
    public int MaxDurationMinutes { get; set; }
    /// <summary>false si le préavis minimum n'est pas respecté, ou si le plafond hebdo du répétiteur est atteint  l'occurrence reste listée mais non cliquable.</summary>
    public bool IsBookable { get; set; }
}
