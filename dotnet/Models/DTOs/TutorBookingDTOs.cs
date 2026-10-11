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
    /// <summary>Numéro Mobile Money pour le paiement NotchPay (inutile si <see cref="PaymentMethod"/> = wallet).</summary>
    public string? Phone { get; set; }
    /// <summary>
    /// Lot 9.2 (17.6) : "mobile_money" (défaut, flux NotchPay) ou "wallet"
    /// (solde du portefeuille de l'élève, débité atomiquement avec la création).
    /// </summary>
    public string? PaymentMethod { get; set; }
    /// <summary>Matière de la séance : détermine le tarif de base (grille 17.3) et la ventilation des revenus (US-REP-10).</summary>
    public string? Subject { get; set; }
    /// <summary>Sujets à travailler (17.5), textes courts, 5 au plus.</summary>
    public List<string>? Topics { get; set; }

    // ── Lot 9.3 (17.4) : qui réserve, qui paie, qui participe ──
    /// <summary>
    /// student (défaut) | parent | institution | study_group. Contrôlé contre
    /// le rôle réel du compte : un parent ne peut pas se déclarer institution.
    /// </summary>
    public string? PayerType { get; set; }
    /// <summary>Élèves participants. Vide = le compte élève qui réserve. Chaque élève doit avoir un lien vérifié avec le payeur.</summary>
    public List<int>? ParticipantUserIds { get; set; }
    /// <summary>Groupe d'étude (PayerType = study_group) : le réservant et chaque participant doivent en être membres.</summary>
    public int? StudyGroupId { get; set; }
    /// <summary>Attribution active à l'origine de la réservation (facultatif) : les participants doivent en être bénéficiaires.</summary>
    public int? TutorAssignmentId { get; set; }
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
    /// <summary>Lot 9.3 : nombre d'élèves (1 par défaut) pour le prix de groupe.</summary>
    public int ParticipantCount { get; set; } = 1;
}

/// <summary>Participant d'une séance (lot 9.3).</summary>
public class TutorBookingParticipantDto
{
    public int UserId { get; set; }
    public string? Name { get; set; }
    public string? AvatarUrl { get; set; }
}

/// <summary>
/// Ce que le compte connecté peut réserver (lot 9.3) : enfants liés (parent),
/// élèves rattachés (institution), groupes d'étude (élève), attributions
/// actives. Les mêmes liens sont revérifiés à la création de la réservation.
/// </summary>
public class TutorBookingContextDto
{
    public string Role { get; set; } = "";
    /// <summary>Types de payeur ouverts à ce compte (student, parent, institution, study_group).</summary>
    public List<string> AllowedPayerTypes { get; set; } = new();
    public List<TutorBookingParticipantDto> Children { get; set; } = new();
    public int? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }
    public List<TutorBookableStudentDto> InstitutionStudents { get; set; } = new();
    public List<TutorBookableStudyGroupDto> StudyGroups { get; set; } = new();
    public List<TutorAssignmentDto> ActiveAssignments { get; set; } = new();
}

public class TutorBookableStudentDto
{
    public int UserId { get; set; }
    public string? Name { get; set; }
    public string? GroupName { get; set; }
    public string? Level { get; set; }
}

public class TutorBookableStudyGroupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Subject { get; set; }
    public List<TutorBookingParticipantDto> Members { get; set; } = new();
}

/// <summary>Attribution de tuteur (lot 9.3).</summary>
public class TutorAssignmentDto
{
    public int Id { get; set; }
    public int TutorUserId { get; set; }
    public string? TutorName { get; set; }
    public string? TutorAvatarUrl { get; set; }
    public int AssignedByUserId { get; set; }
    public string? AssignedByName { get; set; }
    /// <summary>parent | institution.</summary>
    public string AssignerType { get; set; } = "";
    public int? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }
    public string? GroupName { get; set; }
    public string? Subject { get; set; }
    public string? Note { get; set; }
    /// <summary>pending | active | declined | ended.</summary>
    public string Status { get; set; } = "";
    public string? ResponseNote { get; set; }
    public List<TutorBookingParticipantDto> Beneficiaries { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}

public class CreateTutorAssignmentRequestDto
{
    public int TutorUserId { get; set; }
    /// <summary>Élèves bénéficiaires (enfants liés du parent, ou élèves de l'institution).</summary>
    public List<int>? StudentUserIds { get; set; }
    /// <summary>Institution : classe/groupe entier (InstitutionStudents.GroupName), élèves actifs ajoutés aux bénéficiaires.</summary>
    public string? GroupName { get; set; }
    public string? Subject { get; set; }
    public string? Note { get; set; }
}

public class RespondTutorAssignmentRequestDto
{
    public string? Note { get; set; }
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
    /// <summary>SubtotalXaf - SoloTotalXaf (déduit, jamais arrondi séparément : décision §4.E).</summary>
    public decimal DurationDiscountXaf { get; set; }

    // ── Groupe (lot 9.3) ──
    /// <summary>Nombre d'élèves de la séance (1 = séance individuelle).</summary>
    public int ParticipantCount { get; set; } = 1;
    /// <summary>Prix de la séance pour un seul élève (après dégressivité de durée).</summary>
    public decimal SoloTotalXaf { get; set; }
    /// <summary>null (individuel) | per_student | flat.</summary>
    public string? GroupPricingMethod { get; set; }
    /// <summary>Remise par élève du palier atteint (per_student).</summary>
    public int GroupDiscountPercent { get; set; }
    /// <summary>Prix du groupe en % du prix individuel (flat).</summary>
    public int? GroupFlatPercent { get; set; }
    /// <summary>Prix par élève (exact en per_student, indicatif en flat).</summary>
    public decimal PerStudentXaf { get; set; }

    /// <summary>Montant facturé au payeur pour toute la séance, arrondi à 0 décimale.</summary>
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
    /// <summary>true si encore dans la fenêtre de contestation (24 h par défaut) côté élève.</summary>
    public bool CanDispute { get; set; }
    /// <summary>Fin de la fenêtre de contestation (séance effectuée, fonds pas encore libérés), sinon null.</summary>
    public DateTime? DisputeDeadline { get; set; }
    /// <summary>Durée de la fenêtre de contestation configurée côté serveur (17.8).</summary>
    public int DisputeWindowHours { get; set; }

    // ── Paiement et remboursement (lot 9.2) ──
    /// <summary>mobile_money | wallet (null pour une réservation ancienne jamais payée).</summary>
    public string? PaymentMethod { get; set; }
    public DateTime? RefundedAt { get; set; }
    /// <summary>payer_cancelled | tutor_cancelled | tutor_declined | tutor_no_response | dispute_full | dispute_partial.</summary>
    public string? RefundCause { get; set; }
    public int? RefundPercent { get; set; }
    /// <summary>Montant crédité au portefeuille du payeur.</summary>
    public decimal? RefundAmountXaf { get; set; }
    /// <summary>Frais Mobile Money retenus (déduits du remboursement si RefundFeeBorneBy = payer).</summary>
    public decimal? RefundFeeXaf { get; set; }
    /// <summary>none | payer | tutor | platform.</summary>
    public string? RefundFeeBorneBy { get; set; }
    public decimal? TutorFeeChargedXaf { get; set; }
    public decimal? TutorRetainedXaf { get; set; }

    // ── Payeur, participants, objet (lot 9.3) ──
    /// <summary>student | parent | institution | study_group.</summary>
    public string PayerType { get; set; } = "student";
    public int PayerUserId { get; set; }
    /// <summary>Nom affiché du payeur (parent, institution, membre du groupe).</summary>
    public string? PayerName { get; set; }
    public int? BookedByUserId { get; set; }
    public int? PayerInstitutionId { get; set; }
    public int? StudyGroupId { get; set; }
    public string? StudyGroupName { get; set; }
    public int ParticipantCount { get; set; } = 1;
    public List<TutorBookingParticipantDto> Participants { get; set; } = new();
    public int? TutorAssignmentId { get; set; }
    /// <summary>single_session (9.3) ; le 9.4 ajoutera l'engagement mensuel.</summary>
    public string ObjectType { get; set; } = "single_session";
    /// <summary>Le compte connecté est du côté du payeur (payeur, réservant, compte de l'institution payeuse).</summary>
    public bool ViewerIsPayer { get; set; }
    /// <summary>Le compte connecté est un élève participant.</summary>
    public bool ViewerIsParticipant { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Politique de tutorat publique (lot 9.2, suivi 17.10) : source unique pour le web et le mobile.</summary>
public class TutorPolicyDto
{
    public string Version { get; set; } = null!;
    public int DisputeWindowHours { get; set; }
    public decimal NotchPayFeePercent { get; set; }
    public TutorPolicyBoundsDto Bounds { get; set; } = new();
    public List<TutorPolicySectionDto> Sections { get; set; } = new();
}

/// <summary>Limites plateforme des réglages d'annulation du tuteur, et valeurs par défaut.</summary>
public class TutorPolicyBoundsDto
{
    public int FullRefundHoursMin { get; set; }
    public int FullRefundHoursMax { get; set; }
    public int PartialRefundPercentMin { get; set; }
    public int PartialRefundPercentMax { get; set; }
    public int NoRefundHoursMin { get; set; }
    public int NoRefundHoursMax { get; set; }
    public int DefaultFullRefundHours { get; set; }
    public int DefaultPartialRefundPercent { get; set; }
    public int DefaultNoRefundHours { get; set; }
}

public class TutorPolicySectionDto
{
    public string Id { get; set; } = null!;
    public string Title { get; set; } = null!;
    public List<string> Paragraphs { get; set; } = new();
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
