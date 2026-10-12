using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

// ═══════════════════════════════════════════════════════════════════════════
//  Lot 9.5 (suivi 17.11, 17.12, 17.16) : vérification pour le présentiel,
//  signalements et suspension automatique du présentiel.
//
//  Modèle : chaque voie de vérification a sa propre table (pièce d'identité +
//  selfie, entretien, caution d'institution) et garde son historique. Le statut
//  « vérifié pour le présentiel » n'est jamais saisi à la main : il est CALCULÉ
//  à partir des voies valides (TutorVerificationService.RecomputeAsync) et mis
//  en cache sur TutorProfile (InPersonVerifiedAt, InPersonVerificationRoutes)
//  pour que la recherche filtre en SQL. Toute décision (dépôt, validation,
//  refus, caution, révocation, suspension, levée, purge des fichiers) laisse
//  une ligne dans TutorVerificationEvents : qui, quoi, quand, motif.
//
//  Le diplôme (TutorVerificationDocument, IsDiplomaVerified) reste un badge
//  facultatif, sans effet sur le présentiel.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Voies de vérification pour le présentiel (17.11).</summary>
public static class TutorVerificationRoutes
{
    /// <summary>Pièce d'identité + selfie validés par un admin.</summary>
    public const string Identity = "identity";
    /// <summary>Entretien réussi ET pièce d'identité + selfie validés (17.16 : l'entretien ne remplace pas l'identité).</summary>
    public const string Interview = "interview";
    /// <summary>Caution active d'une institution à laquelle le tuteur est toujours rattaché.</summary>
    public const string Institution = "institution";
}

/// <summary>
/// Dépôt d'une pièce d'identité (recto, verso si besoin) et d'un selfie,
/// comparés par un admin WinPlus (17.16 : pas de service automatique externe).
/// Les fichiers vivent dans le stockage PRIVÉ (IPrivateFileStore) : seules des
/// références sont gardées ici, jamais d'URL publique ; un admin les lit par
/// URL présignée de 5 minutes. Conservation limitée : voir
/// TutorVerificationService.PurgeExpiredIdentityFilesAsync.
/// </summary>
public class TutorIdentityCheck
{
    public int Id { get; set; }
    public int TutorProfileId { get; set; }

    /// <summary>tutor (déposé par le tuteur) | interview (joint pendant l'entretien, par le tuteur ou l'agent).</summary>
    [MaxLength(20)]
    public string Source { get; set; } = TutorIdentitySources.Tutor;

    /// <summary>Entretien auquel la pièce a été jointe (Source = interview).</summary>
    public int? TutorVerificationInterviewId { get; set; }

    /// <summary>cni | passport | residence_permit | driving_license | other.</summary>
    [MaxLength(30)]
    public string DocumentType { get; set; } = "cni";

    /// <summary>Références privées (« s3:clé » ou « local:clé »), jamais une URL publique. Null après purge.</summary>
    [MaxLength(500)]
    public string? FrontFileRef { get; set; }
    [MaxLength(500)]
    public string? BackFileRef { get; set; }
    [MaxLength(500)]
    public string? SelfieFileRef { get; set; }

    /// <summary>pending | approved | rejected | superseded (remplacé par un dépôt plus récent avant décision).</summary>
    [MaxLength(20)]
    public string Status { get; set; } = TutorIdentityStatuses.Pending;

    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    public int SubmittedByUserId { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Fichiers supprimés du stockage (règle de conservation) : la décision et son auteur restent.</summary>
    public DateTime? FilesPurgedAt { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }
}

public static class TutorIdentitySources
{
    public const string Tutor = "tutor";
    public const string Interview = "interview";
}

public static class TutorIdentityStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Superseded = "superseded";
}

/// <summary>
/// Entretien de vérification (17.16) : en visio en direct ou en rencontre
/// physique. Le tuteur le demande (forme, disponibilités, ville), un agent
/// WinPlus (admin) le planifie, le mène et enregistre le résultat, assisté par
/// WinAI (guide, grille, brouillon de compte rendu). Un entretien réussi ne
/// vérifie le tuteur que si une pièce d'identité + selfie est validée.
/// </summary>
public class TutorVerificationInterview
{
    public int Id { get; set; }
    public int TutorProfileId { get; set; }

    /// <summary>video | in_person.</summary>
    [MaxLength(20)]
    public string Format { get; set; } = TutorInterviewFormats.Video;

    /// <summary>Disponibilités proposées par le tuteur (texte libre).</summary>
    [MaxLength(500)]
    public string? Availability { get; set; }

    /// <summary>Ville souhaitée pour une rencontre (Format = in_person).</summary>
    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(500)]
    public string? TutorMessage { get; set; }

    /// <summary>requested | scheduled | passed | failed | no_show | cancelled.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = TutorInterviewStatuses.Requested;

    public DateTime? ScheduledAt { get; set; }
    [MaxLength(500)]
    public string? MeetingUrl { get; set; }
    [MaxLength(300)]
    public string? Location { get; set; }

    /// <summary>Agent WinPlus (admin) qui a planifié / mené l'entretien.</summary>
    public int? AgentUserId { get; set; }

    /// <summary>Guide d'entretien et grille d'évaluation générés par WinAI (JSON).</summary>
    public string? GuideJson { get; set; }
    /// <summary>Notes prises par l'agent pendant l'entretien.</summary>
    public string? AgentNotes { get; set; }
    /// <summary>Notes de la grille saisies par l'agent (JSON : critère → note 1 à 5).</summary>
    public string? ScoresJson { get; set; }
    /// <summary>Brouillon de compte rendu rédigé par WinAI à partir des notes.</summary>
    public string? ReportDraft { get; set; }
    /// <summary>Compte rendu final validé par l'agent.</summary>
    public string? FinalReport { get; set; }

    /// <summary>Motif d'échec, d'absence ou d'annulation (visible du tuteur).</summary>
    [MaxLength(500)]
    public string? OutcomeReason { get; set; }

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }
}

public static class TutorInterviewFormats
{
    public const string Video = "video";
    public const string InPerson = "in_person";
}

public static class TutorInterviewStatuses
{
    public const string Requested = "requested";
    public const string Scheduled = "scheduled";
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string NoShow = "no_show";
    public const string Cancelled = "cancelled";

    public static readonly string[] Open = { Requested, Scheduled };
}

/// <summary>
/// Caution d'une institution (17.11) : l'institution, à laquelle le tuteur est
/// rattaché (InstitutionTeacherLink accepté), déclare l'avoir vérifié par ses
/// propres moyens : comment (Statement), avec qui (VerifierName) et quand
/// (VerifiedOn). Révocable par l'institution ; la révocation du rattachement
/// la révoque aussi. Une caution ne compte que tant que le rattachement existe.
/// </summary>
public class TutorInstitutionEndorsement
{
    public int Id { get; set; }
    public int TutorProfileId { get; set; }
    public int InstitutionId { get; set; }
    public int EndorsedByUserId { get; set; }

    /// <summary>Moyens de vérification employés par l'institution.</summary>
    [MaxLength(500)]
    public string Statement { get; set; } = "";

    /// <summary>Personne de l'institution qui a vérifié le tuteur.</summary>
    [MaxLength(150)]
    public string VerifierName { get; set; } = "";

    /// <summary>Date de la vérification par l'institution.</summary>
    public DateOnly VerifiedOn { get; set; }

    /// <summary>active | revoked.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = TutorEndorsementStatuses.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public int? RevokedByUserId { get; set; }
    [MaxLength(300)]
    public string? RevocationReason { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }

    [ForeignKey(nameof(InstitutionId))]
    public Institution? Institution { get; set; }
}

public static class TutorEndorsementStatuses
{
    public const string Active = "active";
    public const string Revoked = "revoked";
}

/// <summary>
/// Historique de la vérification et de la sécurité d'un tuteur : une ligne par
/// décision. Lisible par l'admin en entier ; le tuteur en voit les étapes qui
/// le concernent (sans les notes internes de l'agent).
/// </summary>
public class TutorVerificationEvent
{
    public int Id { get; set; }
    public int TutorProfileId { get; set; }

    /// <summary>identity | interview | institution | status | suspension | report.</summary>
    [MaxLength(20)]
    public string Category { get; set; } = "";

    /// <summary>submitted, approved, rejected, superseded, files_purged, requested, scheduled, passed, failed, no_show, cancelled, endorsed, revoked, verified, unverified, suspended, lifted, reported.</summary>
    [MaxLength(30)]
    public string Action { get; set; } = "";

    public int? ActorUserId { get; set; }
    /// <summary>tutor | admin | institution | system | student | parent.</summary>
    [MaxLength(20)]
    public string ActorRole { get; set; } = "system";

    [MaxLength(500)]
    public string? Reason { get; set; }

    /// <summary>Identifiant de la ligne concernée (pièce, entretien, caution, signalement).</summary>
    public int? RelatedId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Signalement d'un tuteur (17.12, 17.16) par un participant ou par le payeur
/// d'une séance, y compris annulée ou en cours. Un seul signalement par séance
/// et par personne. Chaque signalement alerte les admins ; au-delà du seuil
/// (TutorSafety:ReportThreshold signalements en TutorSafety:ReportWindowDays
/// jours, 3 en 30 par défaut), le présentiel du tuteur est suspendu.
/// </summary>
public class TutorReport
{
    public int Id { get; set; }
    public int TutorProfileId { get; set; }
    public int TutorBookingId { get; set; }
    public int ReporterUserId { get; set; }

    /// <summary>participant | payer.</summary>
    [MaxLength(20)]
    public string ReporterRole { get; set; } = "participant";

    /// <summary>Code de motif (voir <see cref="TutorReportReasons"/>).</summary>
    [MaxLength(40)]
    public string Reason { get; set; } = TutorReportReasons.Other;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Statut de la séance au moment du signalement.</summary>
    [MaxLength(30)]
    public string BookingStatus { get; set; } = "";

    /// <summary>open | reviewed.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "open";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    [MaxLength(500)]
    public string? AdminNote { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }

    [ForeignKey(nameof(TutorBookingId))]
    public TutorBooking? TutorBooking { get; set; }

    [ForeignKey(nameof(ReporterUserId))]
    public User? Reporter { get; set; }
}

/// <summary>Motifs de signalement proposés dans la liste (17.12).</summary>
public static class TutorReportReasons
{
    public const string InappropriateBehavior = "inappropriate_behavior";
    public const string SafetyConcern = "safety_concern";
    public const string Harassment = "harassment";
    public const string NoShow = "no_show";
    public const string OffPlatformPayment = "off_platform_payment";
    public const string IdentityMismatch = "identity_mismatch";
    public const string PoorQuality = "poor_quality";
    public const string Other = "other";

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [InappropriateBehavior] = "Comportement inapproprié",
        [SafetyConcern] = "Problème de sécurité",
        [Harassment] = "Harcèlement ou propos déplacés",
        [NoShow] = "Absent à la séance",
        [OffPlatformPayment] = "Demande de paiement hors WinPlus",
        [IdentityMismatch] = "Ce n'est pas la personne du profil",
        [PoorQuality] = "Séance de mauvaise qualité",
        [Other] = "Autre",
    };
}
