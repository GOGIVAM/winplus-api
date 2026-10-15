using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

/// <summary>
/// Journal d'audit des actions sensibles de l'administration (lot 9.7a,
/// suivi 18.3). Une ligne par action réellement appliquée, écrite par
/// <see cref="Backend.Services.IAdminAuditService"/> et jamais modifiée ni
/// supprimée par l'application.
///
/// <see cref="Before"/> et <see cref="After"/> portent un résumé court
/// (quelques champs, en JSON), jamais un objet complet ni un secret.
/// </summary>
public class AdminAuditLog
{
    public long Id { get; set; }

    /// <summary>Administrateur auteur de l'action (null : tâche système).</summary>
    public int? ActorUserId { get; set; }

    /// <summary>E-mail de l'auteur au moment de l'action (le compte peut disparaître).</summary>
    [MaxLength(256)]
    public string? ActorEmail { get; set; }

    /// <summary>Code de l'action (voir <see cref="AdminAuditActions"/>).</summary>
    [MaxLength(80)]
    public string Action { get; set; } = null!;

    /// <summary>Type de la cible : User, Order, Setting, PromoCode, Conversation…</summary>
    [MaxLength(50)]
    public string? TargetType { get; set; }

    [MaxLength(64)]
    public string? TargetId { get; set; }

    /// <summary>Libellé lisible de la cible (nom, e-mail, titre) au moment de l'action.</summary>
    [MaxLength(300)]
    public string? TargetLabel { get; set; }

    /// <summary>Résumé de l'état avant (JSON court).</summary>
    public string? Before { get; set; }

    /// <summary>Résumé de l'état après (JSON court).</summary>
    public string? After { get; set; }

    /// <summary>Motif saisi par l'administrateur, quand l'action en exige un.</summary>
    [MaxLength(1000)]
    public string? Reason { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Codes d'action du journal d'audit (libellés français côté écran).</summary>
public static class AdminAuditActions
{
    public const string UserRoleChanged = "user.role_changed";
    public const string UserSuspended = "user.suspended";
    public const string UserReactivated = "user.reactivated";
    public const string UserSoftDeleted = "user.soft_deleted";
    public const string UserHardDeleted = "user.hard_deleted";
    public const string UserUpdated = "user.updated";
    public const string UserCreated = "user.created";
    public const string UserSessionsRevoked = "user.sessions_revoked";
    public const string UserSubscriptionGranted = "user.subscription_granted";

    public const string WalletCredit = "wallet.admin_credit";
    public const string WalletBackfillRun = "wallet.backfill_run";
    public const string ExamPdfReplaced = "content.pdf_replaced";
    public const string LearningPublished = "learning.published";
    public const string AlbumGenerated = "album.generated";
    public const string AlbumDispatched = "album.dispatched";
    public const string WithdrawalRetried = "withdrawal.retried";
    public const string WithdrawalClosed = "withdrawal.closed";
    public const string InstitutionWalletHolderChanged = "institution_wallet.holder_changed";

    public const string RefundApproved = "refund.approved";
    public const string RefundRejected = "refund.rejected";

    public const string MassEmailQueued = "email.mass_queued";

    public const string SettingChanged = "setting.changed";
    public const string CatalogCommissionChanged = "catalog.commission_changed";
    public const string CatalogItemUpdated = "catalog.item_updated";
    public const string CatalogItemDeleted = "catalog.item_deleted";

    public const string PromoCodeCreated = "promo.created";
    public const string PromoCodeUpdated = "promo.updated";
    public const string PromoCodeDeactivated = "promo.deactivated";

    public const string AnnouncementCreated = "announcement.created";
    public const string AnnouncementUpdated = "announcement.updated";
    public const string AnnouncementDeleted = "announcement.deleted";
    public const string AnnouncementPublished = "announcement.published";

    public const string TutorDisputeResolved = "tutor.dispute_resolved";
    public const string TutorReportReviewed = "tutor.report_reviewed";
    public const string TutorReviewModerated = "tutor.review_moderated";
    public const string TutorIdentityDecided = "tutor.identity_decided";
    public const string TutorInterviewCompleted = "tutor.interview_completed";
    public const string TutorEndorsementRevoked = "tutor.endorsement_revoked";
    public const string TutorSuspensionLifted = "tutor.suspension_lifted";
    public const string TutorDiplomaApproved = "tutor.diploma_approved";
    public const string TutorDiplomaRejected = "tutor.diploma_rejected";

    public const string ForumModerationResolved = "forum.moderation_resolved";
    public const string ContentApproved = "content.approved";
    public const string ContentRejected = "content.rejected";
    public const string CourseApproved = "course.approved";
    public const string CourseRejected = "course.rejected";
    public const string CourseDeleted = "course.deleted";

    public const string WinAiConversationViewed = "winai.conversation_viewed";
    public const string WinAiMessageDeleted = "winai.message_deleted";

    public const string AffiliateStatusChanged = "affiliate.status_changed";
    public const string AffiliateSettingsChanged = "affiliate.settings_changed";
    public const string CertificateIssued = "certificate.issued";

    // Lot 9.7b (18.13) : plans d'abonnement, promotions, licences.
    public const string PlanCreated = "plan.created";
    public const string PlanUpdated = "plan.updated";
    public const string PlanArchived = "plan.archived";
    public const string PlanRestored = "plan.restored";
    public const string PlanVersionDrafted = "plan.version_drafted";
    public const string PlanVersionUpdated = "plan.version_updated";
    public const string PlanVersionDiscarded = "plan.version_discarded";
    public const string PlanVersionPublished = "plan.version_published";
}
