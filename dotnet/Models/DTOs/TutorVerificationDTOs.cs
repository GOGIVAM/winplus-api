namespace Backend.Models.DTOs;

// ── Lot 9.5 (suivi 17.11, 17.12, 17.16) : vérification, entretien, caution, signalements ──

/// <summary>Suivi de la vérification vu par le tuteur.</summary>
public class TutorVerificationStatusDto
{
    public int TutorProfileId { get; set; }
    /// <summary>Vérifié pour le présentiel (au moins une voie valide).</summary>
    public bool IsVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
    /// <summary>Voies valides : identity, interview, institution.</summary>
    public List<string> Routes { get; set; } = new();
    public bool IsSuspended { get; set; }
    public DateTime? SuspendedAt { get; set; }
    public string? SuspensionReason { get; set; }
    /// <summary>Présentiel possible maintenant.</summary>
    public bool InPersonAllowed { get; set; }
    /// <summary>Badge facultatif, sans effet sur le présentiel.</summary>
    public bool IsDiplomaVerified { get; set; }

    public TutorIdentityCheckSummaryDto? LatestIdentity { get; set; }
    public bool HasApprovedIdentity { get; set; }
    public TutorInterviewSummaryDto? LatestInterview { get; set; }
    public bool HasPassedInterview { get; set; }
    public List<TutorEndorsementDto> Endorsements { get; set; } = new();
    public List<TutorVerificationEventDto> History { get; set; } = new();

    /// <summary>Durée de conservation des fichiers d'identité, affichée au tuteur (RGPD).</summary>
    public int IdentityRetentionDaysAfterApproval { get; set; }
    public int IdentityRetentionDaysAfterRejection { get; set; }
}

public class TutorIdentityCheckSummaryDto
{
    public int Id { get; set; }
    public string Source { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public bool HasBack { get; set; }
    public string Status { get; set; } = "";
    public string? RejectionReason { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? FilesPurgedAt { get; set; }
    public int? InterviewId { get; set; }
}

public class TutorInterviewSummaryDto
{
    public int Id { get; set; }
    public string Format { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Availability { get; set; }
    public string? City { get; set; }
    public string? TutorMessage { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public string? MeetingUrl { get; set; }
    public string? Location { get; set; }
    public string? OutcomeReason { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    /// <summary>Une pièce d'identité + selfie est jointe à cet entretien.</summary>
    public bool HasIdentityAttached { get; set; }
}

public class TutorEndorsementDto
{
    public int Id { get; set; }
    public int TutorProfileId { get; set; }
    public int TutorUserId { get; set; }
    public string? TutorName { get; set; }
    public int InstitutionId { get; set; }
    public string? InstitutionName { get; set; }
    public string Statement { get; set; } = "";
    public string VerifierName { get; set; } = "";
    public DateOnly VerifiedOn { get; set; }
    public string Status { get; set; } = "";
    /// <summary>Active ET rattachement toujours accepté : compte pour la vérification.</summary>
    public bool IsValid { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
}

public class TutorVerificationEventDto
{
    public int Id { get; set; }
    public string Category { get; set; } = "";
    public string Action { get; set; } = "";
    public string ActorRole { get; set; } = "";
    public int? ActorUserId { get; set; }
    public string? ActorName { get; set; }
    public string? Reason { get; set; }
    public int? RelatedId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class RequestTutorInterviewDto
{
    /// <summary>video | in_person.</summary>
    public string Format { get; set; } = "video";
    public string? Availability { get; set; }
    public string? City { get; set; }
    public string? Message { get; set; }
}

public class ReasonRequestDto
{
    public string? Reason { get; set; }
}

// ── Institution ──

/// <summary>Tuteur rattaché à l'institution, avec l'état de sa caution.</summary>
public class InstitutionTutorForEndorsementDto
{
    public int TutorUserId { get; set; }
    public int? TutorProfileId { get; set; }
    public string? TutorName { get; set; }
    public string? AvatarUrl { get; set; }
    public bool HasTutorProfile { get; set; }
    public bool IsInPersonVerified { get; set; }
    public TutorEndorsementDto? ActiveEndorsement { get; set; }
}

public class CreateTutorEndorsementDto
{
    public int TutorUserId { get; set; }
    /// <summary>Moyens de vérification employés par l'institution.</summary>
    public string Statement { get; set; } = "";
    /// <summary>Personne de l'institution qui a vérifié le tuteur.</summary>
    public string VerifierName { get; set; } = "";
    public DateOnly VerifiedOn { get; set; }
}

// ── Admin ──

public class AdminTutorSummaryDto
{
    public int TutorProfileId { get; set; }
    public int TutorUserId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsInPersonVerified { get; set; }
    public bool IsSuspended { get; set; }
    public List<string> Routes { get; set; } = new();
}

public class AdminIdentityCheckDto
{
    public int Id { get; set; }
    public AdminTutorSummaryDto Tutor { get; set; } = new();
    public string Source { get; set; } = "";
    public int? InterviewId { get; set; }
    public string DocumentType { get; set; } = "";
    public bool HasBack { get; set; }
    public string Status { get; set; } = "";
    public string? RejectionReason { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string? SubmittedByName { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByName { get; set; }
    public DateTime? FilesPurgedAt { get; set; }
}

/// <summary>URL de lecture des fichiers d'identité : présignées 5 min (S3) ou endpoint admin (stockage local).</summary>
public class AdminIdentityFilesDto
{
    public string? FrontUrl { get; set; }
    public string? BackUrl { get; set; }
    public string? SelfieUrl { get; set; }
    /// <summary>true : URL de l'API à lire avec le jeton admin (stockage local, développement).</summary>
    public bool RequiresAuth { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class AdminInterviewDto
{
    public int Id { get; set; }
    public AdminTutorSummaryDto Tutor { get; set; } = new();
    public List<string> Subjects { get; set; } = new();
    public List<string> Levels { get; set; } = new();
    public List<string> Specialties { get; set; } = new();
    public string? TeachingStyle { get; set; }
    public string? Bio { get; set; }
    public string Format { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Availability { get; set; }
    public string? City { get; set; }
    public string? TutorMessage { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public string? MeetingUrl { get; set; }
    public string? Location { get; set; }
    public int? AgentUserId { get; set; }
    public string? AgentName { get; set; }
    public string? GuideJson { get; set; }
    public string? AgentNotes { get; set; }
    public string? ScoresJson { get; set; }
    public string? ReportDraft { get; set; }
    public string? FinalReport { get; set; }
    public string? OutcomeReason { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    /// <summary>Pièce d'identité + selfie jointes à l'entretien (la plus récente).</summary>
    public AdminIdentityCheckDto? Identity { get; set; }
    /// <summary>Le tuteur a déjà une pièce validée (par n'importe quelle voie).</summary>
    public bool HasApprovedIdentity { get; set; }
}

public class ScheduleInterviewDto
{
    public DateTime ScheduledAt { get; set; }
    public string? MeetingUrl { get; set; }
    public string? Location { get; set; }
}

public class SaveInterviewNotesDto
{
    public string? Notes { get; set; }
    /// <summary>JSON : critère → note 1 à 5.</summary>
    public string? ScoresJson { get; set; }
}

public class CompleteInterviewDto
{
    /// <summary>passed | failed | no_show | cancelled.</summary>
    public string Outcome { get; set; } = "";
    public string? FinalReport { get; set; }
    public string? Reason { get; set; }
}

public class AdminTutorReportDto
{
    public int Id { get; set; }
    public AdminTutorSummaryDto Tutor { get; set; } = new();
    public int TutorBookingId { get; set; }
    public DateOnly SessionDate { get; set; }
    public string SessionMode { get; set; } = "";
    public string BookingStatus { get; set; } = "";
    public int ReporterUserId { get; set; }
    public string? ReporterName { get; set; }
    public string ReporterRole { get; set; } = "";
    public string Reason { get; set; } = "";
    public string ReasonLabel { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByName { get; set; }
    public string? AdminNote { get; set; }
}

public class AdminSuspendedTutorDto
{
    public AdminTutorSummaryDto Tutor { get; set; } = new();
    public DateTime SuspendedAt { get; set; }
    public string? Reason { get; set; }
    public int ReportsInWindow { get; set; }
    public int OpenReports { get; set; }
    /// <summary>Séances présentielles futures déjà réservées (non annulées automatiquement).</summary>
    public List<AdminInPersonBookingDto> UpcomingInPersonBookings { get; set; } = new();
}

public class AdminInPersonBookingDto
{
    public int BookingId { get; set; }
    public int TutorProfileId { get; set; }
    public string? TutorName { get; set; }
    public DateOnly SessionDate { get; set; }
    public string StartTime { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Status { get; set; } = "";
    public string? PayerName { get; set; }
}

/// <summary>Contrôle : séances présentielles à venir avec un tuteur non vérifié ou suspendu.</summary>
public class AdminInPersonControlDto
{
    public int Count { get; set; }
    public List<AdminInPersonBookingDto> Bookings { get; set; } = new();
}

public class AdminTutorSafetyFileDto
{
    public AdminTutorSummaryDto Tutor { get; set; } = new();
    public DateTime? SuspendedAt { get; set; }
    public string? SuspensionReason { get; set; }
    public DateTime? SuspensionLiftedAt { get; set; }
    public List<AdminTutorReportDto> Reports { get; set; } = new();
    public List<TutorVerificationEventDto> History { get; set; } = new();
    public List<TutorEndorsementDto> Endorsements { get; set; } = new();
    public List<AdminInPersonBookingDto> UpcomingInPersonBookings { get; set; } = new();
}

// ── Signalements (côté participant / payeur) ──

public class CreateTutorReportDto
{
    public int TutorBookingId { get; set; }
    public string Reason { get; set; } = "";
    public string? Description { get; set; }
}

public class TutorReportReasonDto
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
}

public class TutorReportCreatedDto
{
    public int Id { get; set; }
    public string Message { get; set; } = "";
}
