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
    /// <summary>Épreuve ou livre du catalogue (exclusif avec <see cref="CourseId"/>).</summary>
    public int SubjectId { get; set; }

    /// <summary>
    /// Lot 9.7b (F14) : formation achetée pour l'enfant. Quand il est
    /// renseigné, <see cref="SubjectId"/> est ignoré. L'inscription de
    /// l'enfant est créée par le serveur à la confirmation du paiement.
    /// </summary>
    public int? CourseId { get; set; }

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
    private readonly Backend.Services.Entitlements.IEntitlementService _entitlements;

    public ParentCreditsController(ApplicationDbContext db, ILogger<ParentCreditsController> logger, INtfyService ntfy, IWalletService wallet,
        Backend.Services.Entitlements.IEntitlementService entitlements)
    {
        _wallet = wallet;
        _entitlements = entitlements;
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
    /// Lot 9.7b : dotation du cycle en cours (droit parent.monthly_credit_xaf,
    /// cycle mensuel calé sur la date d'abonnement, voir ParentAllowance) et
    /// limite d'enfants (droit children.max), lus dans les droits effectifs.
    /// Appelé par chaque endpoint qui a besoin d'un solde à jour.
    /// </summary>
    private async Task<(string? PlanName, int? MaxChildren, DateTime? SubscriptionEndDate)> EnsureMonthlyAllocationAsync(int parentId)
    {
        var ent = await Backend.Services.Entitlements.ParentAllowance.EnsureCurrentAsync(_entitlements, _wallet, parentId, DateTime.UtcNow);
        if (ent.Plan == null) return (null, null, null);
        var max = ent.Int(Backend.Services.Entitlements.EntitlementCatalog.ChildrenMax);
        return (ent.Plan.PlanName, max == Backend.Services.Entitlements.EntitlementCatalog.Unlimited ? null : (int?)max, ent.Plan.EndDate);
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
    /// GET /api/parent/payments?limit=50 : historique réel des paiements du
    /// parent (écran « Paiements » mobile, qui affichait une liste fictive).
    /// Fusionne, du plus récent au plus ancien :
    ///   - ses commandes (catalogue, formations, abonnements), payées par
    ///     Mobile Money ou par le solde : statut, moyen, opérateur ;
    ///   - les débits de son portefeuille hors commande (achat pour un enfant,
    ///     cours particuliers, recharge WinAI, licence) et ses recharges.
    /// Un achat par le solde lié à une commande n'apparaît qu'une fois (la
    /// commande). « Dépensé ce mois » = commandes payées + débits hors
    /// commande du mois civil en cours ; les recharges n'en font pas partie.
    /// </summary>
    [HttpGet("payments")]
    public async Task<IActionResult> GetPayments([FromQuery] int limit = 50)
    {
        try
        {
            if (limit is < 1 or > 200) limit = 50;
            var parentId = User.GetUserId();
            var now = DateTime.UtcNow;
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var orders = await _db.Orders.AsNoTracking()
                .Where(o => o.UserId == parentId && !o.IsDeleted && o.Status.ToLower() != "cancelled")
                .OrderByDescending(o => o.CreatedAt)
                .Take(limit)
                .Select(o => new
                {
                    o.Id, o.OrderNumber, o.TotalAmount, o.Status, o.PaymentMethod, o.CreatedAt, o.CompletedDate, o.Notes,
                    Titles = o.Items.Select(i => i.Subject != null ? i.Subject.Title : i.Course != null ? i.Course.Title : null).ToList(),
                    Operator = o.Payments.OrderByDescending(p => p.CreatedAt).Select(p => p.Operator).FirstOrDefault(),
                })
                .ToListAsync();

            static string OrderStatus(string s) => s.ToLowerInvariant() switch
            {
                "completed" => "paid",
                "refunded" => "refunded",
                "pending" => "pending",
                "refund_requested" => "refund_requested",
                _ => "failed",
            };
            static string Method(string? method, string? op)
            {
                var m = (method ?? "").ToLowerInvariant();
                if (m.Contains("balance") || m.Contains("wallet") || m.Contains("solde")) return "Solde";
                return (op ?? "").ToLowerInvariant() switch
                {
                    "mtn" => "MTN Mobile Money",
                    "orange" => "Orange Money",
                    "" => "Mobile Money",
                    var other => other.ToUpperInvariant(),
                };
            }

            var items = orders.Select(o =>
            {
                var titles = o.Titles.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                var label = titles.Count > 0
                    ? titles[0] + (titles.Count > 1 ? $" (+{titles.Count - 1})" : "")
                    : o.OrderNumber.StartsWith("SUB-", StringComparison.OrdinalIgnoreCase)
                        ? (string.IsNullOrWhiteSpace(o.Notes) ? "Abonnement WinPlus" : o.Notes!.Length > 80 ? o.Notes[..80] : o.Notes)
                        : $"Commande {o.OrderNumber}";
                return new ParentPaymentItem("order", o.Id, label, o.TotalAmount, "debit", OrderStatus(o.Status),
                    Method(o.PaymentMethod, o.Operator), o.CompletedDate ?? o.CreatedAt, o.OrderNumber);
            }).ToList();

            var orderIds = orders.Select(o => o.Id).ToHashSet();
            var (walletRows, _) = await _wallet.GetHistoryAsync(parentId, "all", 1, limit);
            foreach (var w in walletRows)
            {
                var src = WalletSources.ForEntryType(w.EntryType);
                var isRecharge = src == WalletSources.Recharge && w.AmountXaf > 0;
                var isSpending = w.AmountXaf < 0 && (src == WalletSources.Purchase || src == WalletSources.Tutoring);
                if (!isRecharge && !isSpending) continue;
                if (w.SourceType == "Order" && w.SourceId is int oid && orderIds.Contains(oid)) continue;
                items.Add(new ParentPaymentItem(isRecharge ? "recharge" : "wallet", w.Id, w.Label, Math.Abs(w.AmountXaf),
                    isRecharge ? "credit" : "debit", w.Status == WalletEntryStatus.Confirmed ? "paid" : w.Status,
                    isRecharge ? "Mobile Money" : "Solde", w.Date, null));
            }

            var sorted = items.OrderByDescending(i => i.Date).Take(limit).ToList();
            var monthSpent = items.Where(i => i.Direction == "debit" && i.Status == "paid" && i.Date >= monthStart).Sum(i => i.AmountXaf);
            return Ok(new { data = new { items = sorted, monthSpentXaf = monthSpent, currency = "XAF" }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting parent payments");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    public sealed record ParentPaymentItem(string Kind, long Id, string Label, decimal AmountXaf, string Direction,
        string Status, string Method, DateTime Date, string? Reference);

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

            if (request.CourseId is > 0)
                return await PurchaseCourseForChildAsync(tx, parentId, request.ChildId, request.CourseId.Value);

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
                // Lot 9.7b (18.21) : Subject.Price est le prix TTC payé ; la
                // ligne porte le HT déduit, base du revenu de l'auteur.
                PriceAtPurchase = VatPolicy.ExclTaxFromInclTax(subject.Price)
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

                // Lot 10.6 : dans la langue de l'élève (User.Locale).
                await _ntfy.PublishLocalizedAsync(_db,
                    topic: $"winplus-user-{request.ChildId}",
                    title: new("Nouveau contenu disponible", "New content available"),
                    message: new($"Un parent vient de vous offrir « {subject.Title} ».",
                                 $"A parent has just given you \"{subject.Title}\"."),
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

    /// <summary>
    /// Lot 9.7b (F14) : achat d'une formation pour un enfant lié, depuis le
    /// portefeuille parent (dotation puis recharge). Mêmes règles que l'achat
    /// d'une épreuve : transaction Serializable et verrou du portefeuille déjà
    /// pris par l'appelant, idempotence sur une fenêtre courte, complément
    /// Mobile Money proposé si le solde est insuffisant (la commande reste en
    /// attente, sa confirmation ouvre l'accès). Refus (409) si la formation est
    /// gratuite, déjà achetée ou déjà suivie par l'enfant, ou si elle
    /// appartient au parent ou à l'enfant (F3, F10). Prix TTC payé (18.21) ;
    /// la ligne porte le HT déduit, base du revenu du formateur.
    /// </summary>
    private async Task<IActionResult> PurchaseCourseForChildAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, int parentId, int childId, int courseId)
    {
        var course = await _db.Courses.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == courseId && c.Status == "published");
        if (course == null)
            return NotFound(new { success = false, error = "Formation introuvable." });

        var since = DateTime.UtcNow.Subtract(PurchaseForChildIdempotencyWindow);
        var childMarker = ChildPurchaseMarker(childId);
        var duplicate = await _db.Orders.AsNoTracking()
            .Where(o => o.UserId == parentId
                     && o.CreatedAt >= since
                     && (o.PaymentMethod == "parent_wallet" || o.PaymentMethod == "mobile_money")
                     && o.Items.Count == 1
                     && o.Items.Any(i => i.CourseId == courseId)
                     && o.Notes == childMarker)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();
        if (duplicate != null)
        {
            await tx.CommitAsync();
            return Ok(new
            {
                data = new
                {
                    orderId        = duplicate.Id,
                    orderNumber    = duplicate.OrderNumber,
                    status         = duplicate.Status,
                    paidWithWallet = duplicate.PaymentMethod == "parent_wallet",
                    amount         = duplicate.TotalAmount,
                },
                success   = true,
                duplicate = true,
                message   = $"Cet achat a déjà été enregistré (commande {duplicate.OrderNumber}). Aucun second débit n'a été effectué.",
            });
        }

        var block = await CourseSales.PurchaseBlockAsync(_db, parentId, childId, course);
        if (block != null)
            return Conflict(new { success = false, code = block.Value.Code, error = block.Value.Message, courseId });

        var price = decimal.Round(course.Price, 0, MidpointRounding.AwayFromZero);
        var available = await _wallet.GetAvailableAsync(parentId);
        var payFromWallet = available >= price;

        var order = new Order
        {
            UserId        = parentId,
            OrderNumber   = $"WP-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            TotalAmount   = price,
            Status        = payFromWallet ? "paid" : "pending",
            PaymentMethod = payFromWallet ? "parent_wallet" : "mobile_money",
            Notes         = childMarker,
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        _db.OrderItems.Add(new OrderItem
        {
            OrderId         = order.Id,
            CourseId        = course.Id,
            PriceAtPurchase = VatPolicy.ExclTaxFromInclTax(price),
        });

        if (payFromWallet)
        {
            await _wallet.DebitParentWalletAsync(
                parentId, price, $"purchase_for_child:order:{order.Id}", course.Title, "Order", order.Id);
            _db.ParentCreditLedgers.Add(new ParentCreditLedger
            {
                ParentId    = parentId,
                EntryType   = "consumption",
                Amount      = price,
                ChildId     = childId,
                OrderId     = order.Id,
                PeriodStart = CurrentPeriodStart(),
                Label       = course.Title,
            });
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        if (!payFromWallet)
        {
            return StatusCode(402, new
            {
                success = false,
                requiresTopUp = true,
                error = "Solde du portefeuille insuffisant pour cet achat. Rechargez via Mobile Money pour continuer.",
                orderId = order.Id,
                walletAvailable = available,
                price,
                missingXaf = price - available,
            });
        }

        // Écritures de vente (formateur, commission) et inscription de
        // l'enfant (CourseSales), idempotentes, rattrapées par la
        // réconciliation en cas d'échec.
        try { await _wallet.SyncOrderAsync(order.Id); }
        catch (Exception ex) { _logger.LogError(ex, "Écritures de la commande {OrderId} non posées (réconciliation à venir)", order.Id); }

        await _ntfy.PublishLocalizedAsync(_db,
            topic: $"winplus-user-{childId}",
            title: new("Nouvelle formation disponible", "New course available"),
            message: new($"Un parent vient de vous offrir la formation « {course.Title} ».",
                         $"A parent has just given you the course \"{course.Title}\"."),
            tags: new[] { "gift" },
            userId: childId,
            type: "content",
            relatedEntityType: "course",
            relatedEntityId: course.Id);

        return Ok(new
        {
            data = new
            {
                orderId        = order.Id,
                orderNumber    = order.OrderNumber,
                status         = order.Status,
                paidWithWallet = true,
                amount         = price,
                walletLeft     = available - price,
                courseId       = course.Id,
            },
            success = true,
        });
    }

    /// <summary>Fenêtre de détection d'une double soumission de l'achat pour enfant.</summary>
    private static readonly TimeSpan PurchaseForChildIdempotencyWindow = TimeSpan.FromSeconds(60);

    /// <summary>Marqueur du bénéficiaire, écrit dans Order.Notes.</summary>
    internal static string ChildPurchaseMarker(int childId) => $"{CourseSales.ChildMarkerPrefix}{childId}";
}
