using Microsoft.EntityFrameworkCore;
using Backend.Data;

namespace Backend.Services;

/// <summary>
/// Module 43 (lot 7)  export RGPD des données utilisateur (décision §5.5.R).
///
/// Le service frontal promettait déjà cet export (page de confidentialité /
/// bouton "Télécharger mes données" du profil), mais aucun endpoint
/// n'existait côté serveur. Cette classe agrège, pour un seul utilisateur
/// cible, l'ensemble des catégories de données recensées à l'audit :
/// profil, commandes/paiements, inscriptions/progression, certificats,
/// quiz/lacunes, objectifs, bulletins, messages (directs et IA), mémoires
/// IA, liens de parenté, portefeuille/retraits/commissions d'affiliation
/// (lot 2, Module 1), préférences de notification, historique de
/// téléchargement.
///
/// Exclusions strictes (jamais exportées) : empreinte de mot de passe,
/// jetons de session/rafraîchissement, codes de vérification/2FA, et toute
/// donnée appartenant à un autre utilisateur  sauf l'unique exception déjà
/// tranchée (§6.8 du suivi) : un parent peut exporter les données de son
/// enfant mineur dont le lien est accepté (voir UsersController, qui vérifie
/// ce lien avant d'appeler ce service ; la minorité elle-même ne peut pas
/// être vérifiée techniquement aujourd'hui, faute de champ de date de
/// naissance sur l'entité utilisateur  décision produit encore ouverte,
/// signalée explicitement dans la réponse via `minorityVerified: false`).
///
/// Génération synchrone : les volumes de cette plateforme (un utilisateur
/// individuel, pas un export de masse) restent raisonnables pour un calcul
/// dans le flux de la requête. Si un compte isolé s'avérait trop volumineux
/// en production, ce calcul est le point à déporter en tâche de fond avec
/// notification de disponibilité (Module 22)  non fait ici par choix de
/// périmètre, signalé dans le rapport de livraison plutôt que deviné.
/// </summary>
public interface IUserDataExportService
{
    Task<object> BuildExportAsync(int targetUserId, bool exportedForParent, CancellationToken ct = default);
}

public sealed class UserDataExportService : IUserDataExportService
{
    private readonly ApplicationDbContext _db;

    public UserDataExportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<object> BuildExportAsync(int targetUserId, bool exportedForParent, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
        if (user == null) throw new KeyNotFoundException($"User {targetUserId} not found");

        var profile = new
        {
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Username,
            user.Phone,
            user.Role,
            user.IsEmailVerified,
            user.Bio,
            user.Level,
            user.City,
            user.Locale,
            user.Specialization,
            user.TargetExam,
            user.TeachingSubjects,
            user.TeachingLevels,
            user.CreatedAt,
            user.LastLoginAt,
            // Note explicitement pas exportés : PasswordHash, VerificationCode,
            // VerificationCodeExpiredAt, EmailChangeToken, tout jeton de session.
        };

        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.UserId == targetUserId)
            .Select(o => new
            {
                o.Id, o.OrderNumber, o.TotalAmount, o.DiscountAmount, o.Status,
                o.PaymentMethod, o.PromoCode, o.OrderDate, o.CompletedDate, o.IsDeleted,
                items = o.Items.Select(i => new { i.Id, i.SubjectId, i.CourseId, i.PriceAtPurchase }),
            })
            .ToListAsync(ct);

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.UserId == targetUserId)
            .Select(p => new
            {
                p.Id, p.OrderId, p.Amount, p.Currency, p.Status, p.PaymentMethod,
                p.Operator, p.InitiatedAt, p.CompletedAt,
                // Exclu : TransactionId/NotchpayReference/PhoneNumber bruts du provider
                // ne sont pas des données nécessaires à l'utilisateur ; conservés
                // uniquement si un besoin de preuve de paiement est confirmé.
            })
            .ToListAsync(ct);

        var enrollments = await _db.Enrollments.AsNoTracking()
            .Where(e => e.UserId == targetUserId)
            .Select(e => new
            {
                e.Id, e.SubjectId, e.EnrolledAt, e.CompletedAt, e.ProgressPercentage,
                e.IsCompleted, e.UnenrolledAt, e.UnenrollReason,
            })
            .ToListAsync(ct);

        var courseEnrollments = await _db.CourseEnrollments.AsNoTracking()
            .Where(e => e.UserId == targetUserId)
            .Select(e => new { e.Id, e.CourseId, e.EnrolledAt })
            .ToListAsync(ct);

        var certificates = await _db.Certificates.AsNoTracking()
            .Where(c => c.UserId == targetUserId)
            .Select(c => new { c.Id, c.SubjectId, c.CertificateNumber, c.IssuedAt, c.CompletionDate, c.Grade, c.VerificationCode })
            .ToListAsync(ct);

        var courseCertificates = await _db.CourseCertificates.AsNoTracking()
            .Where(c => c.UserId == targetUserId)
            .ToListAsync(ct);

        var quizAttempts = await _db.QuizAttempts.AsNoTracking()
            .Where(a => a.UserId == targetUserId)
            .Select(a => new { a.Id, a.QuizId, a.Score, a.CorrectAnswers, a.Passed, a.AttemptNumber, a.StartedAt, a.CompletedAt })
            .ToListAsync(ct);

        var quizMistakes = await _db.QuizMistakes.AsNoTracking()
            .Where(m => m.UserId == targetUserId)
            .Select(m => new { m.Id, m.Subject, m.Question, m.GivenAnswer, m.CorrectAnswer, m.IsResolved, m.CreatedAt })
            .ToListAsync(ct);

        var goals = await _db.Goals.AsNoTracking()
            .Where(g => g.UserId == targetUserId)
            .Select(g => new { g.Id, g.Title, g.Description, g.Type, g.Progress, g.Status, g.TargetDate, g.CompletedAt })
            .ToListAsync(ct);

        var academicRecords = await _db.AcademicRecords.AsNoTracking()
            .Where(r => r.StudentId == targetUserId)
            .Select(r => new { r.Id, r.SchoolYear, r.AverageGrade, r.CreatedAt })
            .ToListAsync(ct);

        var parentLinksAsParent = await _db.ParentStudentLinks.AsNoTracking()
            .Where(l => l.ParentId == targetUserId)
            .Select(l => new { l.Id, l.StudentId, l.Status, l.CreatedAt })
            .ToListAsync(ct);

        var parentLinksAsChild = await _db.ParentStudentLinks.AsNoTracking()
            .Where(l => l.StudentId == targetUserId)
            .Select(l => new { l.Id, l.ParentId, l.Status, l.CreatedAt })
            .ToListAsync(ct);

        var subscriptions = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == targetUserId)
            .Select(s => new { s.Id, s.PlanName, s.Status, s.StartDate, s.EndDate, s.RenewalCount })
            .ToListAsync(ct);

        // ── Portefeuille / journal (Module 1, lot 2) ────────────────────────
        // Décision §13.4 : l'effacement de compte (Module 19) anonymise plutôt
        // que de supprimer en cascade les écritures financières  l'export
        // reste donc disponible même pour un compte dont la suppression
        // logique est en cours, cohérent avec cette règle déjà prise.
        var walletTransactions = await _db.WalletTransactions.AsNoTracking()
            .Where(w => w.OwnerId == targetUserId)
            .Select(w => new { w.Id, w.OwnerType, w.EntryType, w.Amount, w.Status, w.SourceType, w.SourceId, w.Description, w.OccurredAt, w.SettledAt })
            .ToListAsync(ct);

        var withdrawals = await _db.Withdrawals.AsNoTracking()
            .Where(w => w.UserId == targetUserId)
            .Select(w => new { w.Id, w.Operator, w.Phone, w.AmountXaf, w.Status, w.RequestedAt, w.ProcessedAt, w.FailureReason })
            .ToListAsync(ct);

        var affiliateCommissions = await _db.AffiliateAccounts.AsNoTracking()
            .Where(a => a.UserId == targetUserId)
            .SelectMany(a => a.Commissions)
            .Select(c => new { c.Id, c.OrderId, c.OrderAmount, c.CommissionRateApplied, c.CommissionAmount, c.Status, c.CreatedAt, c.ConfirmedAt })
            .ToListAsync(ct);

        var notificationSettings = await _db.UserNotificationSettings.AsNoTracking()
            .Where(s => s.UserId == targetUserId)
            .Select(s => new { s.EmailNotifications, s.PushNotifications, s.CourseCommunity, s.Promotions, s.Newsletters, s.LearningReminders })
            .FirstOrDefaultAsync(ct);

        var privacySettings = await _db.UserPrivacySettings.AsNoTracking()
            .Where(s => s.UserId == targetUserId)
            .FirstOrDefaultAsync(ct);

        var downloadHistory = await _db.DownloadHistories.AsNoTracking()
            .Where(d => d.UserId == targetUserId)
            .Select(d => new { d.Id, d.SubjectId, d.ExamId, d.FileName, d.CreatedAt })
            .ToListAsync(ct);

        // Messages directs : seulement ceux de l'utilisateur cible, jamais le
        // contenu provenant d'un autre compte au-delà de ce strict nécessaire
        // pour comprendre l'échange (identifiant de l'autre partie, pas son
        // propre profil complet).
        var directMessages = await _db.DirectMessages.AsNoTracking()
            .Where(m => m.FromUserId == targetUserId || m.ToUserId == targetUserId)
            .Select(m => new { m.Id, m.FromUserId, m.ToUserId, m.Content, m.Type, m.CreatedAt, m.IsRead })
            .ToListAsync(ct);

        var aiConversations = await _db.Conversations.AsNoTracking()
            .Where(c => c.UserId == targetUserId)
            .Select(c => new
            {
                c.Id, c.Title, c.CreatedAt, c.UpdatedAt,
                messages = c.Messages.Select(m => new { m.Id, m.Role, m.Content, m.CreatedAt }),
            })
            .ToListAsync(ct);

        var aiMemories = await _db.UserAIMemories.AsNoTracking()
            .Where(m => m.UserId == targetUserId)
            .Select(m => new { m.Id, m.MemoryType, m.Content, m.CreatedAt, m.UpdatedAt })
            .ToListAsync(ct);

        var parentReports = await _db.ParentReports.AsNoTracking()
            .Where(r => (r.ParentId == targetUserId || r.ChildId == targetUserId) && !r.IsPreviewPending)
            .Select(r => new { r.Id, r.ChildId, r.ReportType, r.Content, r.CapsuleText, r.CreatedAt })
            .ToListAsync(ct);

        return new
        {
            exportedAt = DateTime.UtcNow,
            exportedForParent,
            // §6.8 du suivi : autorisé pour un enfant mineur lié par un lien
            // accepté. La minorité elle-même n'est techniquement pas
            // vérifiable aujourd'hui (pas de champ de date de naissance) :
            // ce drapeau le dit explicitement plutôt que de le supposer.
            minorityVerified = false,
            profile,
            orders,
            payments,
            enrollments,
            courseEnrollments,
            certificates,
            courseCertificates,
            quizAttempts,
            quizMistakes,
            goals,
            academicRecords,
            parentLinksAsParent,
            parentLinksAsChild,
            subscriptions,
            walletTransactions,
            withdrawals,
            affiliateCommissions,
            notificationSettings,
            privacySettings,
            downloadHistory,
            directMessages,
            aiConversations,
            aiMemories,
            parentReports,
        };
    }
}
