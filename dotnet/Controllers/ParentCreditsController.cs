using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

public class PurchaseForChildRequest
{
    public int ChildId { get; set; }
    public int SubjectId { get; set; }

    /// <summary>
    /// Conservé pour compatibilité avec le client existant : le portefeuille
    /// (Module 14) est toujours utilisé en priorité, ce paramètre ne fait plus
    /// basculer vers un registre différent. <c>false</c> n'a plus d'effet
    /// utile depuis le remplacement du registre de crédits par le portefeuille :
    /// il n'existe plus de second moyen de paiement "sans crédits" dédié à ce
    /// parcours, le web doit lancer un paiement Mobile Money classique
    /// (POST /api/orders) s'il veut explicitement éviter le portefeuille.
    /// </summary>
    public bool UseCredits { get; set; } = true;
}

/// <summary>
/// Portefeuille parent (Module 14) et achat de contenu pour un enfant.
///
/// Remplace le registre de crédits mensuels dédié (<see cref="ParentCreditLedger"/>,
/// conservé uniquement pour l'historique déjà écrit avant ce module) par le
/// journal de portefeuille commun (<see cref="IWalletService"/>,
/// <c>WalletOwnerTypes.Parent</c>), avec une dotation mensuelle expirable en
/// plus de la recharge libre permanente (décision §2.6 du suivi).
///
/// Règles conservées de l'ancien système (toujours valables) : pas de report
/// de la dotation au-delà de son expiration, pas de remboursement en fin de
/// mois, pas de recalcul au prorata en cas de changement de plan en cours de
/// mois.
///
/// GET  /api/parent/credits          (compatibilité) résumé simplifié
/// GET  /api/parent/credits/history  (compatibilité) historique simplifié
/// POST /api/parent/purchase-for-child
/// </summary>
[ApiController]
[Route("api/parent")]
[Authorize(Policy = "ParentOnly")]
public class ParentCreditsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ParentCreditsController> _logger;
    private readonly INtfyService _ntfy;
    private readonly IWalletService _wallet;

    public ParentCreditsController(ApplicationDbContext db, ILogger<ParentCreditsController> logger, INtfyService ntfy, IWalletService wallet)
    {
        _wallet = wallet;
        _db = db;
        _logger = logger;
        _ntfy = ntfy;
    }

    private static DateTime CurrentPeriodStart()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private static DateTime CurrentPeriodEnd(DateTime periodStart) => periodStart.AddMonths(1);

    /// <summary>
    /// Crée la dotation du mois en cours si le parent a un abonnement payant
    /// actif dont le plan en prévoit une, et qu'elle n'a pas déjà été écrite.
    /// Appelé par chaque endpoint qui a besoin d'un solde à jour (lecture
    /// paresseuse, comme l'ancien registre de crédits).
    /// </summary>
    private async Task<(string? PlanName, int? MaxChildren, DateTime? SubscriptionEndDate)> EnsureMonthlyAllocationAsync(int parentId)
    {
        var periodStart = CurrentPeriodStart();

        var subscription = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == parentId && s.Status == "active" && !s.IsDeleted)
            .OrderByDescending(s => s.StartDate)
            .Select(s => new
            {
                s.EndDate,
                planName = s.PricingPlan != null ? s.PricingPlan.Name : null,
                monthly = s.PricingPlan != null ? s.PricingPlan.MonthlyCredits : null,
                maxChildren = s.PricingPlan != null ? s.PricingPlan.MaxChildren : null,
            })
            .FirstOrDefaultAsync();

        if (subscription == null) return (null, null, null);

        if (subscription.monthly is > 0)
        {
            await _wallet.PostParentMonthlyAllocationAsync(
                parentId, subscription.monthly.Value, periodStart, CurrentPeriodEnd(periodStart),
                subscription.planName ?? "Standard");
        }

        return (subscription.planName, subscription.maxChildren, subscription.EndDate);
    }

    /// <summary>GET /api/parent/credits — résumé compatible avec l'ancien registre, désormais porté par le portefeuille.</summary>
    [HttpGet("credits")]
    public async Task<IActionResult> GetCredits()
    {
        try
        {
            var parentId = User.GetUserId();
            var (planName, maxChildren, endDate) = await EnsureMonthlyAllocationAsync(parentId);

            if (planName == null)
                return Ok(new { data = (object?)null, success = true });

            var breakdown = await _wallet.GetParentBreakdownAsync(parentId);
            var childrenCount = await _db.ParentStudentLinks.CountAsync(l => l.ParentId == parentId && l.Status == "accepted");
            var daysToRenewal = endDate.HasValue
                ? (int?)Math.Max(0, (endDate.Value.Date - DateTime.UtcNow.Date).Days)
                : null;

            return Ok(new
            {
                data = new
                {
                    planName,
                    periodStart = CurrentPeriodStart(),
                    // Champs historiques conservés pour compatibilité d'affichage :
                    // "creditsLeft" est désormais le disponible total du portefeuille
                    // (dotation restante + recharges), pas seulement la dotation.
                    creditsLeft = breakdown.AvailableXaf,
                    allocationRemaining = breakdown.AllocationRemainingXaf,
                    allocationExpiresAt = breakdown.AllocationExpiresAt,
                    permanent = breakdown.PermanentXaf,
                    currency = "XAF",
                    childrenCount,
                    childrenLimit = maxChildren,
                    daysToRenewal,
                },
                success = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting parent wallet summary");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// GET /api/parent/credits/history — historique combiné : écritures du
    /// portefeuille (dotation, recharge, achats) depuis ce module, et lignes
    /// de l'ancien registre de crédits conservées pour la période antérieure à
    /// la bascule (décision explicite : ne pas perdre l'historique déjà écrit).
    /// </summary>
    [HttpGet("credits/history")]
    public async Task<IActionResult> GetHistory([FromQuery] int limit = 50)
    {
        try
        {
            if (limit is < 1 or > 200) limit = 50;
            var parentId = User.GetUserId();

            var (items, _) = await _wallet.GetHistoryAsync(parentId, WalletSources.MonthlyAllocation, 1, limit);
            var purchases = await _wallet.GetHistoryAsync(parentId, WalletSources.Purchase, 1, limit);
            var recharges = await _wallet.GetHistoryAsync(parentId, WalletSources.Recharge, 1, limit);

            var walletItems = items.Concat(purchases.Items).Concat(recharges.Items)
                .OrderByDescending(i => i.Date)
                .Take(limit)
                .Select(i => new
                {
                    i.Id,
                    entryType = i.EntryType,
                    amount = Math.Abs(i.AmountXaf),
                    label = i.Label,
                    createdAt = i.Date,
                    direction = i.Type, // "credit" | "debit"
                });

            // Lignes historiques de l'ancien registre, écrites avant ce module :
            // conservées telles quelles, jamais recalculées.
            var legacy = await _db.ParentCreditLedgers.AsNoTracking()
                .Where(l => l.ParentId == parentId)
                .OrderByDescending(l => l.CreatedAt)
                .Take(limit)
                .Select(l => new
                {
                    l.Id, l.EntryType, l.Amount, l.Label, l.CreatedAt, l.PeriodStart,
                    childId = l.ChildId,
                    childName = l.Child != null ? (l.Child.FirstName + " " + l.Child.LastName).Trim() : null
                })
                .ToListAsync();

            return Ok(new { data = new { wallet = walletItems, legacy }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting parent wallet history");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Retour d'usage sur les achats faits pour un enfant (inchangé par ce
    /// module, conservé sur le registre historique : l'achat pour enfant par
    /// portefeuille écrit désormais aussi une ligne de registre à titre
    /// d'historique de bénéficiaire, voir <see cref="PurchaseForChild"/>).
    /// </summary>
    [HttpGet("purchases/{childId:int}/impact")]
    public async Task<IActionResult> GetPurchaseImpact(int childId)
    {
        try
        {
            var parentId = User.GetUserId();

            var linked = await _db.ParentStudentLinks.AnyAsync(l =>
                l.ParentId == parentId && l.StudentId == childId && l.Status == "accepted");
            if (!linked)
                return StatusCode(403, new { success = false, error = "Cet enfant n'est pas lié à votre compte." });

            var now = DateTime.UtcNow;
            var windowStart = now.AddDays(-30);
            var windowEnd = now.AddDays(-1);

            var purchases = await (
                from ledger in _db.ParentCreditLedgers
                where ledger.ParentId == parentId && ledger.ChildId == childId
                    && ledger.EntryType == "consumption" && ledger.OrderId != null
                join order in _db.Orders on ledger.OrderId equals order.Id
                where order.CreatedAt >= windowStart && order.CreatedAt <= windowEnd
                join item in _db.OrderItems on order.Id equals item.OrderId
                join subject in _db.Subjects on item.SubjectId equals subject.Id
                select new { SubjectId = subject.Id, subject.Title, PurchaseDate = order.CreatedAt }
            ).AsNoTracking().ToListAsync();

            var results = new List<object>(purchases.Count);
            foreach (var p in purchases)
            {
                var firstConsultedAt = await _db.DownloadHistories.AsNoTracking()
                    .Where(d => d.UserId == childId && d.SubjectId == p.SubjectId && d.CreatedAt >= p.PurchaseDate)
                    .OrderBy(d => d.CreatedAt)
                    .Select(d => (DateTime?)d.CreatedAt)
                    .FirstOrDefaultAsync();

                results.Add(new
                {
                    contentId        = p.SubjectId,
                    contentTitle     = p.Title,
                    purchaseDate     = p.PurchaseDate,
                    consulted        = firstConsultedAt != null,
                    firstConsultedAt = firstConsultedAt,
                });
            }

            return Ok(new { data = results, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting purchase impact for child {ChildId}", childId);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Achète une épreuve pour un enfant. Débite le portefeuille (dotation
    /// mensuelle en priorité, puis recharge — Module 14) si suffisant ; sinon,
    /// propose explicitement le complément Mobile Money au lieu de rejeter
    /// l'achat (décision §2.6 point 2 et prompt Module 14, critère
    /// d'acceptation « n'est plus rejeté »).
    /// </summary>
    [HttpPost("purchase-for-child")]
    public async Task<IActionResult> PurchaseForChild([FromBody] PurchaseForChildRequest request)
    {
        // Module 19 : Serializable, limité à ce chemin, comme le paiement par
        // solde professeur et la réservation de tutorat.
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var parentId = User.GetUserId();
            // Verrou du portefeuille acquis dès l'ouverture de la transaction
            // (même patron que OrdersController.PayWithBalance) : la lecture du
            // disponible ci-dessous et le débit qui suit doivent voir le même
            // état, sans course possible avec un autre achat concurrent.
            await _wallet.RunLockedAsync(parentId, () => Task.FromResult(true));
            await EnsureMonthlyAllocationAsync(parentId);

            var linked = await _db.ParentStudentLinks
                .AnyAsync(l => l.ParentId == parentId && l.StudentId == request.ChildId && l.Status == "accepted");
            if (!linked)
                return StatusCode(403, new { success = false, error = "Cet enfant n'est pas lié à votre compte." });

            var subject = await _db.Subjects
                .Where(s => s.Id == request.SubjectId && !s.IsDeleted && s.IsPublished)
                .Select(s => new { s.Id, s.Title, s.Price })
                .FirstOrDefaultAsync();
            if (subject == null)
                return NotFound(new { success = false, error = "Épreuve introuvable." });

            // Idempotence dérivée côté serveur (passe de clôture du lot 0) :
            // même parent, même enfant, même contenu, fenêtre courte.
            var since = DateTime.UtcNow.Subtract(PurchaseForChildIdempotencyWindow);
            var childMarker = ChildPurchaseMarker(request.ChildId);
            var duplicate = await _db.Orders.AsNoTracking()
                .Where(o => o.UserId == parentId
                         && o.CreatedAt >= since
                         && (o.PaymentMethod == "parent_wallet" || o.PaymentMethod == "mobile_money")
                         && o.Items.Count == 1
                         && o.Items.Any(i => i.SubjectId == request.SubjectId)
                         && o.Notes == childMarker)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            if (duplicate != null)
            {
                await tx.CommitAsync();
                _logger.LogWarning(
                    "Double soumission détectée sur l'achat pour enfant : parent {ParentId}, enfant {ChildId}, contenu {SubjectId}, commande {OrderId} renvoyée",
                    parentId, request.ChildId, request.SubjectId, duplicate.Id);

                return Ok(new
                {
                    data = new
                    {
                        orderId         = duplicate.Id,
                        orderNumber     = duplicate.OrderNumber,
                        status          = duplicate.Status,
                        paidWithWallet  = duplicate.PaymentMethod == "parent_wallet",
                        amount          = duplicate.TotalAmount,
                    },
                    success   = true,
                    duplicate = true,
                    message   = $"Cet achat a déjà été enregistré (commande {duplicate.OrderNumber}). Aucun second débit n'a été effectué.",
                });
            }

            var alreadyEnrolled = await _db.Enrollments
                .AnyAsync(e => e.UserId == request.ChildId && e.SubjectId == request.SubjectId);
            if (alreadyEnrolled)
                return BadRequest(new { success = false, error = "Votre enfant a déjà accès à cette épreuve." });

            var available = await _wallet.GetAvailableAsync(parentId);
            var payFromWallet = available >= subject.Price;

            var order = new Order
            {
                UserId        = parentId,
                OrderNumber   = $"WP-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                TotalAmount   = subject.Price,
                Status        = payFromWallet ? "paid" : "pending",
                PaymentMethod = payFromWallet ? "parent_wallet" : "mobile_money",
                // Bénéficiaire de l'achat, relu par la détection de doublon et
                // par le webhook de paiement (module 18) pour l'inscription.
                Notes         = childMarker,
            };
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            _db.OrderItems.Add(new OrderItem
            {
                OrderId         = order.Id,
                SubjectId       = subject.Id,
                PriceAtPurchase = subject.Price
            });

            if (payFromWallet)
            {
                await _wallet.DebitParentWalletAsync(
                    parentId, subject.Price, $"purchase_for_child:order:{order.Id}", subject.Title,
                    "Order", order.Id);

                _db.Enrollments.Add(new Enrollment
                {
                    UserId    = request.ChildId,
                    SubjectId = subject.Id
                });

                // Historique legacy conservé pour l'écran "impact d'achat"
                // (GetPurchaseImpact), qui lit encore ParentCreditLedger : une
                // ligne y est toujours écrite à titre de trace bénéficiaire,
                // son montant n'est plus la source du solde.
                _db.ParentCreditLedgers.Add(new ParentCreditLedger
                {
                    ParentId    = parentId,
                    EntryType   = "consumption",
                    Amount      = subject.Price,
                    ChildId     = request.ChildId,
                    OrderId     = order.Id,
                    PeriodStart = CurrentPeriodStart(),
                    Label       = subject.Title,
                });
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            if (payFromWallet)
            {
                try { await _wallet.SyncOrderAsync(order.Id); }
                catch (Exception ex) { _logger.LogError(ex, "Écritures de vente de la commande {OrderId} non posées (réconciliation à venir)", order.Id); }

                await _ntfy.PublishAsync(
                    topic: $"winplus-user-{request.ChildId}",
                    title: "Nouveau contenu disponible",
                    message: $"Un parent vient de vous offrir « {subject.Title} ».",
                    tags: new[] { "gift" },
                    userId: request.ChildId,
                    type: "content",
                    relatedEntityType: "subject",
                    relatedEntityId: subject.Id);

                return Ok(new
                {
                    data = new
                    {
                        orderId        = order.Id,
                        orderNumber    = order.OrderNumber,
                        status         = order.Status,
                        paidWithWallet = true,
                        amount         = subject.Price,
                        walletLeft     = available - subject.Price,
                    },
                    success = true,
                });
            }

            // Solde insuffisant : la commande reste en attente, prête pour un
            // complément Mobile Money explicite — plus de rejet sec (Module 14).
            return StatusCode(402, new
            {
                success = false,
                requiresTopUp = true,
                error = "Solde du portefeuille insuffisant pour cet achat. Rechargez via Mobile Money pour continuer.",
                orderId = order.Id,
                walletAvailable = available,
                price = subject.Price,
                missingXaf = subject.Price - available,
            });
        }
        catch (Exception ex) when (DbConcurrency.IsSerializationFailure(ex))
        {
            _logger.LogWarning(ex, "Collision de sérialisation sur l'achat pour enfant");
            return StatusCode(409, new
            {
                success = false,
                error = "Un autre achat sur votre portefeuille est en cours de traitement. Réessayez dans un instant."
            });
        }
        catch (InsufficientWalletBalanceException)
        {
            // Lu de nouveau entre la vérification et le débit (concurrence) :
            // message actionnable plutôt qu'une erreur générique.
            await tx.RollbackAsync();
            return StatusCode(402, new { success = false, error = "Solde du portefeuille insuffisant pour cet achat." });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Error purchasing for child");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Fenêtre de détection d'une double soumission de l'achat pour enfant.</summary>
    private static readonly TimeSpan PurchaseForChildIdempotencyWindow = TimeSpan.FromSeconds(60);

    /// <summary>Marqueur du bénéficiaire, écrit dans Order.Notes.</summary>
    internal static string ChildPurchaseMarker(int childId) => $"purchase-for-child:{childId}";
}
