namespace Backend.Models.DTOs;

// Lot 9.4 (suivi 17.7) : engagement mensuel à créneaux fixes.

/// <summary>Créneau hebdomadaire demandé : jour (0 = dimanche … 6 = samedi), début "HH:mm", durée multiple de 30 min.</summary>
public class TutorEngagementSlotDto
{
    public int DayOfWeek { get; set; }
    public string StartTime { get; set; } = null!;
    public int DurationMinutes { get; set; }
    /// <summary>Renseigné dans les réponses.</summary>
    public string? EndTime { get; set; }
}

/// <summary>Devis mensuel : le client n'affiche jamais un prix qu'il aurait calculé lui-même.</summary>
public class TutorEngagementQuoteRequestDto
{
    public int TutorUserId { get; set; }
    /// <summary>Premier jour de la période de 4 semaines.</summary>
    public DateOnly StartDate { get; set; }
    public List<TutorEngagementSlotDto> Slots { get; set; } = new();
    public string Mode { get; set; } = "online";
    public string? Subject { get; set; }
    public int ParticipantCount { get; set; } = 1;
}

public class CreateTutorEngagementRequestDto : TutorEngagementQuoteRequestDto
{
    public List<string>? Topics { get; set; }
    /// <summary>wallet | mobile_money.</summary>
    public string? PaymentMethod { get; set; }
    public string? Phone { get; set; }
    /// <summary>student | parent | institution | study_group (mêmes contrôles que la séance unique, lot 9.3).</summary>
    public string? PayerType { get; set; }
    public List<int>? ParticipantUserIds { get; set; }
    public int? StudyGroupId { get; set; }
    /// <summary>Attribution active servant de point de départ (bénéficiaires pré-remplis côté client, revérifiés ici).</summary>
    public int? TutorAssignmentId { get; set; }
    /// <summary>Renouvellement automatique (accepté seulement pour un paiement par solde).</summary>
    public bool AutoRenew { get; set; }
}

public class RenewTutorEngagementRequestDto
{
    public string? PaymentMethod { get; set; }
    public string? Phone { get; set; }
    public bool? AutoRenew { get; set; }
}

public class SetTutorEngagementAutoRenewRequestDto
{
    public bool Enabled { get; set; }
}

public class CancelTutorEngagementRequestDto
{
    public string? Reason { get; set; }
}

/// <summary>Séance prévue par le devis mensuel.</summary>
public class TutorEngagementQuoteSessionDto
{
    public DateOnly SessionDate { get; set; }
    public string StartTime { get; set; } = null!;
    public string EndTime { get; set; } = null!;
    public int DurationMinutes { get; set; }
    /// <summary>Prix de la séance par la grille du tuteur (TutorPricing.Compute, groupe compris), avant remise mensuelle.</summary>
    public decimal BaseTotalXaf { get; set; }
    public decimal DiscountXaf { get; set; }
    /// <summary>Prix facturé pour cette séance (sa part du total), figé sur la séance.</summary>
    public decimal PriceXaf { get; set; }
    public bool IsAvailable { get; set; } = true;
    public string? UnavailableReason { get; set; }
}

public class TutorEngagementQuoteDto
{
    public int TutorUserId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int Weeks { get; set; }
    public string Mode { get; set; } = "online";
    public string? Subject { get; set; }
    public int ParticipantCount { get; set; } = 1;
    /// <summary>L'offre mensuelle du tuteur est activée.</summary>
    public bool MonthlyEnabled { get; set; }
    public int DiscountPercent { get; set; }
    public List<TutorEngagementSlotDto> Slots { get; set; } = new();
    public List<TutorEngagementQuoteSessionDto> Sessions { get; set; } = new();
    public decimal SubtotalXaf { get; set; }
    public decimal DiscountXaf { get; set; }
    public decimal TotalXaf { get; set; }
    /// <summary>Toutes les séances sont réservables (sinon la création sera refusée).</summary>
    public bool IsAvailable { get; set; }
    /// <summary>Séances indisponibles, lisibles : « mar. 14/10 18:00 : déjà réservé ».</summary>
    public List<string> Conflicts { get; set; } = new();
}

/// <summary>Séance d'un engagement, telle qu'affichée dans la vue de l'engagement.</summary>
public class TutorEngagementSessionDto
{
    public int Id { get; set; }
    public DateOnly SessionDate { get; set; }
    public string StartTime { get; set; } = null!;
    public string EndTime { get; set; } = null!;
    public int DurationMinutes { get; set; }
    public decimal PriceXaf { get; set; }
    public string Status { get; set; } = null!;
    public string PaymentStatus { get; set; } = null!;
    public DateTime? CompletedAt { get; set; }
    public DateTime? EscrowReleasedAt { get; set; }
    public DateTime? DisputedAt { get; set; }
    public DateTime? DisputeDeadline { get; set; }
    public decimal? RefundAmountXaf { get; set; }
    public decimal? RefundFeeXaf { get; set; }
    public string? RefundCause { get; set; }
    public int? RefundPercent { get; set; }
    public decimal? TutorRetainedXaf { get; set; }
    /// <summary>held (bloqué) | released (libéré au tuteur) | refunded (remboursé) | partial (part conservée + remboursement) | unpaid.</summary>
    public string MoneyState { get; set; } = "held";
    public bool CanMarkCompleted { get; set; }
    public bool CanDispute { get; set; }
    /// <summary>Le compte connecté peut annuler cette séance seule (payeur ou tuteur, séance future).</summary>
    public bool CanCancel { get; set; }
}

public class TutorEngagementDto
{
    public int Id { get; set; }
    public int TutorUserId { get; set; }
    public string? TutorName { get; set; }
    public string? TutorAvatarUrl { get; set; }
    public string Status { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    /// <summary>Début de la période suivante (renouvellement).</summary>
    public DateOnly NextStartDate { get; set; }
    public int Weeks { get; set; }
    public List<TutorEngagementSlotDto> Slots { get; set; } = new();
    public string Mode { get; set; } = null!;
    public string? Subject { get; set; }
    public List<string> Topics { get; set; } = new();

    public string PayerType { get; set; } = "student";
    public int PayerUserId { get; set; }
    public string? PayerName { get; set; }
    public int BookedByUserId { get; set; }
    public int? PayerInstitutionId { get; set; }
    public int? StudyGroupId { get; set; }
    public string? StudyGroupName { get; set; }
    public int? TutorAssignmentId { get; set; }
    public int ParticipantCount { get; set; }
    public List<TutorBookingParticipantDto> Participants { get; set; } = new();

    public int DiscountPercent { get; set; }
    public decimal SubtotalXaf { get; set; }
    public decimal DiscountXaf { get; set; }
    public decimal TotalXaf { get; set; }
    public string PaymentMethod { get; set; } = null!;
    public string PaymentStatus { get; set; } = null!;
    public string? NotchpayReference { get; set; }

    // ── Argent, séance par séance ──
    /// <summary>Montant brut libéré au tuteur (séances effectuées non contestées, parts conservées), commission comprise.</summary>
    public decimal ReleasedXaf { get; set; }
    /// <summary>Montant encore bloqué par WinPlus (séances à venir, en attente de libération ou en litige).</summary>
    public decimal HeldXaf { get; set; }
    /// <summary>Montant remboursé au portefeuille du payeur.</summary>
    public decimal RefundedXaf { get; set; }
    /// <summary>Frais Mobile Money retenus sur le payeur lors d'annulations.</summary>
    public decimal FeesRetainedXaf { get; set; }
    public int SessionsCount { get; set; }
    public int SessionsDone { get; set; }
    public int SessionsCancelled { get; set; }
    public List<TutorEngagementSessionDto> Sessions { get; set; } = new();

    public DateTime? ResponseDeadlineAt { get; set; }
    public bool AutoRenew { get; set; }
    /// <summary>Renouvellement automatique possible (payé par solde, engagement en cours, pas encore renouvelé).</summary>
    public bool CanSetAutoRenew { get; set; }
    public bool CanRenew { get; set; }
    public bool CanCancel { get; set; }
    /// <summary>Instant de la tentative de renouvellement automatique (NextStartDate - 3 jours).</summary>
    public DateTime AutoRenewAt { get; set; }
    public DateTime? AutoRenewAttemptedAt { get; set; }
    public string? AutoRenewFailureReason { get; set; }
    public int? RenewedFromEngagementId { get; set; }
    public int? RenewedToEngagementId { get; set; }

    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public bool ViewerIsPayer { get; set; }
    public bool ViewerIsParticipant { get; set; }
    public bool ViewerIsTutor { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TutorEngagementCreatedResponseDto
{
    public TutorEngagementDto Engagement { get; set; } = null!;
    public string? NotchpayAuthorizationUrl { get; set; }
}

/// <summary>Demande d'engagement en attente de la décision du tuteur, avec délai restant.</summary>
public class TutorPendingEngagementDto
{
    public TutorEngagementDto Engagement { get; set; } = null!;
    public int MinutesRemaining { get; set; }
}

/// <summary>Réglages de l'offre mensuelle du tuteur.</summary>
public class TutorMonthlyOfferDto
{
    public bool Enabled { get; set; }
    public int DiscountPercent { get; set; }
    public int MaxDiscountPercent { get; set; }
}
