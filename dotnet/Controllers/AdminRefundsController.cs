using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Décision 10.10 du suivi : écran admin minimal pour les demandes de
/// remboursement. Une commande passée en <c>refund_requested</c>
/// (<c>POST /orders/{id}/refund</c>) ne pouvait plus être annulée ni repayée
/// (décisions 10.3, 10.5, 10.6, <see cref="OrderStatusRules"/>), mais aucun
/// chemin ne permettait à un administrateur de l'en sortir.
///
/// Volontairement borné : pas de remboursement partiel, pas de motif
/// structuré, pas de virement Mobile Money.
///
/// GET  /api/admin/refunds                 commandes en refund_requested
/// POST /api/admin/refunds/{orderId}/approve refund_requested → refunded
/// POST /api/admin/refunds/{orderId}/reject  refund_requested → completed
///
/// L'accès au contenu catalogue et le revenu dérivent du statut de la
/// commande via <see cref="PaidOrderStatus"/> (ContentAccessService,
/// LibraryController, CourseEnrollmentController, TeacherService…).
/// <c>refunded</c> n'étant pas un statut payé, l'approbation les retire
/// exactement comme pour une commande <c>failed</c>/<c>cancelled</c> ; le rejet
/// remet <c>completed</c>, qui conserve les deux.
///
/// Décision 10.11 : les droits matérialisés hors de la commande (abonnement,
/// inscription à une formation, assignation de classe) ne relisent pas ce
/// statut ; l'approbation les retire explicitement (voir
/// <c>RevokeLinkedAccessAsync</c>).
/// </summary>
[ApiController]
[Route("api/admin/refunds")]
[Authorize(Policy = "AdminOnly")]
public class AdminRefundsController : ControllerBase
{
    /// <summary>Statut d'une demande en attente, tel qu'écrit par OrdersController.RequestRefund.</summary>
    private const string RefundRequestedStatus = "refund_requested";

    /// <summary>
    /// Statut « remboursée ». Valeur déjà prévue par le modèle
    /// (commentaire de <c>Order.Status</c> : Pending, Completed, Failed,
    /// Refunded), écrite en minuscules selon la convention du Module 19, et
    /// déjà reconnue par l'écran admin des utilisateurs et par les
    /// paiements (<c>Payment.Status = "refunded"</c>).
    /// </summary>
    private const string RefundedStatus = "refunded";

    private const string CompletedStatus = "completed";

    private readonly ApplicationDbContext _db;
    private readonly INtfyService _ntfy;
    private readonly IContentAccessService _contentAccess;
    private readonly ILogger<AdminRefundsController> _logger;
    private readonly IWalletService _wallet;

    public AdminRefundsController(
        ApplicationDbContext db,
        INtfyService ntfy,
        IContentAccessService contentAccess,
        ILogger<AdminRefundsController> logger,
        IWalletService wallet)
    {
        _wallet = wallet;
        _db = db;
        _ntfy = ntfy;
        _contentAccess = contentAccess;
        _logger = logger;
    }

    /// <summary>
    /// Bilan de ce que l'approbation a réellement retiré (décision 10.11),
    /// renvoyé à l'écran admin. <c>Warnings</c> liste les liens que la base ne
    /// permet pas d'établir sans ambiguïté : l'administrateur doit alors
    /// vérifier à la main, plutôt que de croire l'accès retiré.
    /// </summary>
    private sealed record RevocationResult(
        int Subscriptions,
        int SubscriptionCourseEnrollments,
        int PurchasedCourseEnrollments,
        int ClassAssignments,
        List<string> Warnings)
    {
        public static RevocationResult None() => new(0, 0, 0, 0, new List<string>());
    }

    /// <summary>
    /// Commandes en attente de décision, les plus anciennes demandes d'abord.
    /// <c>requestedAt</c> est <c>Order.UpdatedAt</c>, posé par
    /// RequestRefund au moment de la demande (aucune autre écriture ne touche
    /// une commande <c>refund_requested</c> depuis la passe 10.1-10.9).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPending()
    {
        try
        {
            var rows = await _db.Orders.AsNoTracking()
                .Where(o => o.Status.ToLower() == RefundRequestedStatus)
                .OrderBy(o => o.UpdatedAt ?? o.CreatedAt)
                .Select(o => new
                {
                    id = o.Id,
                    orderNumber = o.OrderNumber,
                    totalAmount = o.TotalAmount,
                    paymentMethod = o.PaymentMethod,
                    orderedAt = o.CreatedAt,
                    completedAt = o.CompletedDate,
                    requestedAt = o.UpdatedAt,
                    buyer = o.User == null ? null : new
                    {
                        id = o.User.Id,
                        name = ((o.User.FirstName ?? "") + " " + (o.User.LastName ?? "")).Trim(),
                        email = o.User.Email,
                    },
                    items = o.Items.Select(i => new
                    {
                        subjectId = i.SubjectId,
                        courseId = i.CourseId,
                        title = i.Course != null ? i.Course.Title
                              : i.Subject != null ? i.Subject.Title
                              : null,
                        price = i.PriceAtPurchase,
                    }).ToList(),
                })
                .ToListAsync();

            return Ok(new { data = rows, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la lecture des demandes de remboursement");
            return StatusCode(500, new { success = false, error = "Erreur serveur" });
        }
    }

    /// <summary>
    /// Approuve : la commande passe en <c>refunded</c>. Accès catalogue et
    /// revenu du vendeur tombent d'eux-mêmes (statut hors
    /// <see cref="PaidOrderStatus"/>) ; abonnement, inscriptions de formation
    /// et assignations de classe liés sont retirés explicitement (10.11).
    /// Une commission d'affiliation encore <c>pending</c> sur cette commande
    /// est annulée (<c>reversed</c>), comme le fait déjà
    /// AffiliateCommissionMaturityService pour une commande annulée ou échouée.
    /// Aucun argent n'est renvoyé vers Mobile Money.
    /// </summary>
    [HttpPost("{orderId:int}/approve")]
    public Task<IActionResult> Approve(int orderId) => DecideAsync(orderId, approve: true);

    /// <summary>
    /// Rejette : la commande revient en <c>completed</c>, accès et revenu
    /// restent acquis comme avant la demande.
    /// </summary>
    [HttpPost("{orderId:int}/reject")]
    public Task<IActionResult> Reject(int orderId) => DecideAsync(orderId, approve: false);

    private async Task<IActionResult> DecideAsync(int orderId, bool approve)
    {
        var targetStatus = approve ? RefundedStatus : CompletedStatus;
        try
        {
            var now = DateTime.UtcNow;
            int reversedCommissions = 0;
            var revocation = RevocationResult.None();

            await using (var tx = await _db.Database.BeginTransactionAsync())
            {
                // Mise à jour conditionnelle : seule une commande encore en
                // refund_requested change de statut. Deux décisions
                // concurrentes (double clic, deux administrateurs) ne peuvent
                // donc pas s'appliquer toutes les deux.
                var updated = await _db.Orders
                    .Where(o => o.Id == orderId && o.Status.ToLower() == RefundRequestedStatus)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.Status, targetStatus)
                        .SetProperty(o => o.UpdatedAt, (DateTime?)now));

                if (updated == 0)
                {
                    var current = await _db.Orders.AsNoTracking()
                        .Where(o => o.Id == orderId)
                        .Select(o => o.Status)
                        .FirstOrDefaultAsync();
                    if (current == null)
                        return NotFound(new { success = false, error = "Commande introuvable" });
                    return Conflict(new
                    {
                        success = false,
                        error = $"Cette commande n'est plus en attente de remboursement (statut actuel : {current}).",
                        status = current,
                    });
                }

                if (approve)
                {
                    reversedCommissions = await _db.AffiliateCommissions
                        .Where(c => c.OrderId == orderId && c.Status == "pending")
                        .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, "reversed"));

                    // Décision 10.11 : le statut suffit pour le contenu
                    // catalogue acheté, pas pour les droits matérialisés
                    // ailleurs (abonnement, inscription à une formation,
                    // assignation de classe), qui ne relisent pas la commande.
                    // Même transaction : la décision et le retrait d'accès
                    // s'appliquent ensemble ou pas du tout.
                    revocation = await RevokeLinkedAccessAsync(orderId, now);
                }

                await tx.CommitAsync();
            }

            // Lot 2, Module 1 : l'approbation se traduit dans le journal par
            // des contre-passations (vente de l'auteur retirée, débit du
            // paiement par solde restitué, commission d'affiliation annulée),
            // jamais par une suppression d'écriture. Idempotent ; rattrapé par
            // la réconciliation en cas d'échec.
            if (approve)
            {
                try
                {
                    await _wallet.SyncOrderAsync(orderId);
                    var commissionIds = await _db.AffiliateCommissions.AsNoTracking()
                        .Where(c => c.OrderId == orderId).Select(c => c.Id).ToListAsync();
                    foreach (var commissionId in commissionIds)
                        await _wallet.SyncAffiliateCommissionAsync(commissionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Contre-passations du remboursement de la commande {OrderId} non posées (réconciliation à venir)", orderId);
                }
            }

            var order = await _db.Orders.AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => new { o.UserId, o.OrderNumber, o.TotalAmount, o.PaymentMethod })
                .FirstAsync();

            _logger.LogInformation(
                "Demande de remboursement de la commande {OrderId} {Decision} par l'administrateur {AdminId} ({Reversed} commission(s) d'affiliation annulée(s), "
                + "{Subs} abonnement(s) résilié(s), {SubCourses} inscription(s) par abonnement et {Courses} inscription(s) achetée(s) désactivée(s), "
                + "{Classes} assignation(s) de classe retirée(s))",
                orderId, approve ? "approuvée" : "rejetée", User.GetUserId(), reversedCommissions,
                revocation.Subscriptions, revocation.SubscriptionCourseEnrollments,
                revocation.PurchasedCourseEnrollments, revocation.ClassAssignments);

            await NotifyBuyerAsync(orderId, order.UserId, order.OrderNumber, order.TotalAmount, order.PaymentMethod, approve);

            return Ok(new
            {
                data = new
                {
                    id = orderId,
                    status = targetStatus,
                    revoked = new
                    {
                        subscriptions = revocation.Subscriptions,
                        subscriptionCourseEnrollments = revocation.SubscriptionCourseEnrollments,
                        courseEnrollments = revocation.PurchasedCourseEnrollments,
                        classAssignments = revocation.ClassAssignments,
                    },
                    warnings = revocation.Warnings,
                },
                success = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la décision sur le remboursement de la commande {OrderId}", orderId);
            return StatusCode(500, new { success = false, error = "Erreur serveur" });
        }
    }

    /// <summary>
    /// Décision 10.11 : retire les droits dérivés de la commande qui ne
    /// relisent pas son statut. Appelée dans la transaction de l'approbation,
    /// après le passage en <c>refunded</c>.
    ///
    /// Aucune de ces tables ne porte de référence de commande exploitable
    /// (Subscriptions n'en a pas ; <c>CourseEnrollment.OrderId</c> est mappé
    /// dans le modèle mais absent du schéma SQL et jamais renseigné ;
    /// TeacherClassContents n'en a pas). Chaque lien est donc reconstitué de
    /// façon stricte, et un droit n'est retiré que s'il ne peut provenir
    /// d'aucune autre commande encore payée : en cas de doute, rien n'est
    /// retiré et un avertissement est remonté à l'administrateur.
    /// </summary>
    private async Task<RevocationResult> RevokeLinkedAccessAsync(int orderId, DateTime now)
    {
        var result = RevocationResult.None();

        var order = await _db.Orders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.UserId, o.Notes, o.CreatedAt })
            .FirstAsync();

        if (order.UserId == null) return result; // commande invité : aucun droit rattaché à un compte
        var userId = order.UserId.Value;

        int subscriptions = 0, subscriptionCourses = 0, purchasedCourses = 0, classAssignments = 0;

        // ── 1. Abonnement ────────────────────────────────────────────────
        // Une commande d'abonnement se reconnaît à son intention dans Notes
        // (SubscriptionOrderIntent) ; l'abonnement créé à sa confirmation se
        // retrouve par le même critère que la garde d'idempotence de
        // SubscriptionActivationService (utilisateur, plan, actif, démarré
        // après la création de la commande).
        if (SubscriptionOrderIntent.TryParse(order.Notes, out var intent))
        {
            var candidates = await _db.Subscriptions
                .Where(s => s.UserId == userId
                         && s.PricingPlanId == intent.PricingPlanId
                         && !s.IsDeleted
                         && s.Status.ToLower() == "active"
                         && s.StartDate >= order.CreatedAt.AddMinutes(-1))
                .OrderBy(s => s.StartDate)
                .ToListAsync();

            if (candidates.Count == 0)
            {
                result.Warnings.Add(
                    "Commande d'abonnement : aucun abonnement actif correspondant n'a été trouvé (déjà expiré, résilié ou jamais activé). Rien à résilier.");
            }
            else
            {
                // Autres commandes payées du même plan, créées après celle-ci :
                // si l'une d'elles précède le démarrage d'un abonnement, c'est
                // peut-être elle qui l'a activé (renouvellement). On ne résilie
                // pas un abonnement qu'une autre commande a pu payer.
                var laterNotes = await _db.Orders.AsNoTracking()
                    .Where(o => o.UserId == userId
                             && o.Id != orderId
                             && o.CreatedAt > order.CreatedAt
                             && o.Notes != null
                             && o.Notes.ToLower().StartsWith("subscription:")
                             && PaidOrderStatus.All.Contains(o.Status.ToLower()))
                    .Select(o => new { o.CreatedAt, o.Notes })
                    .ToListAsync();

                var laterSamePlan = laterNotes
                    .Where(o => SubscriptionOrderIntent.TryParse(o.Notes, out var other)
                             && other.PricingPlanId == intent.PricingPlanId)
                    .Select(o => o.CreatedAt)
                    .ToList();

                foreach (var sub in candidates)
                {
                    if (laterSamePlan.Any(created => created <= sub.StartDate))
                    {
                        result.Warnings.Add(
                            $"Abonnement {sub.Id} non résilié : une autre commande payée du même plan a pu l'activer. À vérifier manuellement.");
                        continue;
                    }
                    SubscriptionTermination.EndNow(sub, now);
                    subscriptions++;
                }

                await _db.SaveChangesAsync();
            }

            // Formations ouvertes par l'abonnement : CoursePlayerController ne
            // vérifie que l'inscription active, pas l'abonnement. Si plus aucun
            // abonnement payant ne couvre l'utilisateur, ces inscriptions sont
            // désactivées, sauf celles qu'il a aussi achetées par ailleurs ou
            // dont la formation est devenue gratuite.
            if (subscriptions > 0 && !await _contentAccess.HasActiveSubscriptionAsync(userId))
            {
                subscriptionCourses = await _db.CourseEnrollments
                    .Where(e => e.UserId == userId
                             && e.IsActive
                             && e.AccessType.ToLower() == "subscription"
                             && !e.Course.IsFree
                             && !_db.OrderItems.Any(oi => oi.CourseId == e.CourseId
                                                       && oi.Order.UserId == userId
                                                       && oi.OrderId != orderId
                                                       && PaidOrderStatus.All.Contains(oi.Order.Status.ToLower())))
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsActive, false));
            }
        }

        // ── 2. Formations achetées dans cette commande ───────────────────
        // Lien : ligne de commande portant la formation, inscription du même
        // utilisateur de type « purchase » (CourseEnrollmentController.Enroll
        // n'accepte ce type que sur une commande payée de l'utilisateur).
        var courseIds = await _db.OrderItems.AsNoTracking()
            .Where(oi => oi.OrderId == orderId && oi.CourseId != null)
            .Select(oi => oi.CourseId!.Value)
            .Distinct()
            .ToListAsync();

        if (courseIds.Count > 0)
        {
            purchasedCourses = await _db.CourseEnrollments
                .Where(e => e.UserId == userId
                         && e.IsActive
                         && courseIds.Contains(e.CourseId)
                         && e.AccessType.ToLower() == "purchase"
                         && !e.Course.IsFree
                         && !_db.OrderItems.Any(oi => oi.CourseId == e.CourseId
                                                   && oi.Order.UserId == userId
                                                   && oi.OrderId != orderId
                                                   && PaidOrderStatus.All.Contains(oi.Order.Status.ToLower())))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsActive, false));
        }

        // ── 3. Assignations de classe ────────────────────────────────────
        // TeacherClassesController.AssignContent ne crée pas de commande : il
        // facture l'assignation sur le solde (PriceChargedXaf > 0), sauf si
        // le professeur possède déjà le contenu par une commande payée, auquel
        // cas PriceChargedXaf = 0. Seul ce second cas dépend de la commande
        // remboursée : assignation gratuite, par l'acheteur, du contenu de
        // cette commande, postérieure à la commande, contenu payant non écrit
        // par lui, et aucune autre commande payée du même contenu. Une
        // assignation facturée sur le solde reste acquise.
        var subjectIds = await _db.OrderItems.AsNoTracking()
            .Where(oi => oi.OrderId == orderId && oi.CourseId == null && oi.SubjectId > 0)
            .Select(oi => oi.SubjectId)
            .Distinct()
            .ToListAsync();

        if (subjectIds.Count > 0)
        {
            var links = await _db.TeacherClassContents
                .Where(tcc => tcc.AssignedByUserId == userId
                           && subjectIds.Contains(tcc.SubjectId)
                           && tcc.PriceChargedXaf == 0
                           && tcc.AssignedAt >= order.CreatedAt
                           && tcc.Subject != null
                           && tcc.Subject.Price > 0
                           && tcc.Subject.AuthorUserId != userId
                           && !_db.OrderItems.Any(oi => oi.SubjectId == tcc.SubjectId
                                                     && oi.Order.UserId == userId
                                                     && oi.OrderId != orderId
                                                     && PaidOrderStatus.All.Contains(oi.Order.Status.ToLower())))
                .ToListAsync();

            if (links.Count > 0)
            {
                // Même effet que TeacherClassesController.UnassignContent.
                _db.TeacherClassContents.RemoveRange(links);
                await _db.SaveChangesAsync();
                classAssignments = links.Count;
            }
        }

        return result with
        {
            Subscriptions = subscriptions,
            SubscriptionCourseEnrollments = subscriptionCourses,
            PurchasedCourseEnrollments = purchasedCourses,
            ClassAssignments = classAssignments,
        };
    }

    /// <summary>
    /// Notification in-app + ntfy à l'acheteur. Un échec d'envoi ne remet pas
    /// en cause la décision déjà enregistrée.
    /// </summary>
    private async Task NotifyBuyerAsync(int orderId, int? buyerId, string orderNumber, decimal amount, string? paymentMethod, bool approved)
    {
        if (buyerId == null) return;
        try
        {
            string title, message;
            if (approved)
            {
                title = "Remboursement accepté";
                // Commande payée sur le solde WinPlus (professeur) : le débit
                // est contre-passé dans le journal (lot 2), le montant est donc
                // effectivement recrédité.
                message = string.Equals(paymentMethod, "balance", StringComparison.OrdinalIgnoreCase)
                    ? $"Ta demande de remboursement pour la commande {orderNumber} a été acceptée. {amount:0} XAF ont été recrédités sur ton solde WinPlus. L'accès au contenu de cette commande est retiré."
                    : $"Ta demande de remboursement pour la commande {orderNumber} a été acceptée. L'accès au contenu de cette commande est retiré. Le reversement de {amount:0} XAF est traité séparément par le support WinPlus.";
            }
            else
            {
                title = "Remboursement refusé";
                message = $"Ta demande de remboursement pour la commande {orderNumber} n'a pas été acceptée. Tu conserves l'accès au contenu acheté.";
            }

            await _ntfy.PublishAsync($"winplus-user-{buyerId}", title, message,
                userId: buyerId, type: "Order", relatedEntityType: "Order", relatedEntityId: orderId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification de décision de remboursement non envoyée pour la commande {OrderId}", orderId);
        }
    }
}
