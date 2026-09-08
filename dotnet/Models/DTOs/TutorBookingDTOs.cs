namespace Backend.Models.DTOs;

/// <summary>Demande de réservation d'une séance (US-ELV-01, Module 6).</summary>
public class CreateTutorBookingRequestDto
{
    public int TutorUserId { get; set; }
    public DateOnly SessionDate { get; set; }
    /// <summary>Format "HH:mm".</summary>
    public string StartTime { get; set; } = null!;
    public string EndTime { get; set; } = null!;
    /// <summary>online | student_home | tutor_home | neutral_place.</summary>
    public string Mode { get; set; } = "online";
    /// <summary>Numéro Mobile Money pour le paiement NotchPay.</summary>
    public string Phone { get; set; } = null!;
    /// <summary>Matière souhaitée (facultatif — utilisé pour la ventilation des revenus, US-REP-10).</summary>
    public string? Subject { get; set; }
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
    public string? SummaryText { get; set; }
    /// <summary>true si l'heure de fin de la séance est passée — active "Marquer effectuée" côté répétiteur.</summary>
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

/// <summary>Occurrence datée d'un créneau récurrent, pour la vue calendrier côté élève (Module 6).</summary>
public class TutorAvailabilityOccurrenceDto
{
    public DateOnly Date { get; set; }
    public string StartTime { get; set; } = null!;
    public string EndTime { get; set; } = null!;
    /// <summary>false si déjà réservé, si le préavis minimum n'est pas respecté, ou si le plafond hebdo du répétiteur est atteint pour la semaine de cette date.</summary>
    public bool IsBookable { get; set; }
}
