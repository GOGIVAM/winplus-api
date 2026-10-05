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
    public bool UseCredits { get; set; } = true;
}

/// <summary>
/// Crédits mensuels du parent et achat de contenu pour un enfant (S3-1 / S3-5).
/// Le solde est la dotation du plan moins les consommations du mois : aucune
/// valeur en dur, tout est en base.
///
/// Règles figées (voir parent_decisions_session.md, correction 1) :
/// 1 crédit = 1 FCFA, aucun taux de conversion  le montant en base EST le
/// montant en FCFA, ne jamais introduire d'unité "crédit" distincte de la
/// devise. Non reportables et non remboursables en fin de mois. Changement de
/// plan en cours de mois : le cycle en cours garde son montant déjà alloué,
/// le nouveau montant s'applique au cycle suivant, jamais de prorata.
/// Déliaison d'un enfant après dépense : jamais de remboursement.
///
/// GET  /api/parent/credits
/// GET  /api/parent/credits/history
/// POST /api/parent/purchase-for-child
/// </summary>
[ApiController]
[Route("api/parent")]
// Module 20 : la politique "ParentOnly" était déclarée sans jamais être
// utilisée. Ce contrôleur gère les crédits d'un parent et l'achat pour son
// enfant : un simple [Authorize] laissait un compte élève appeler ces routes
// (la propriété métier est vérifiée ensuite, mais le rôle ne l'était pas).
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

    /// <summary>
    /// Solde du mois. Crée la dotation du mois si le plan en prévoit une et
    /// qu'elle n'a pas encore été écrite.
    /// </summary>
    [HttpGet("credits")]
    public async Task<IActionResult> GetCredits()
    {
        try
        {
            var parentId = User.GetUserId();
            var periodStart = CurrentPeriodStart();

            var subscription = await _db.Subscriptions.AsNoTracking()
                .Where(s => s.UserId == parentId && s.Status == "active" && !s.IsDeleted)
                .OrderByDescending(s => s.StartDate)
                .Select(s => new
                {
                    s.Id,
                    s.EndDate,
                    planName    = s.PricingPlan != null ? s.PricingPlan.Name : null,
                    monthly     = s.PricingPlan != null ? s.PricingPlan.MonthlyCredits : null,
                    maxChildren = s.PricingPlan != null ? s.PricingPlan.MaxChildren : null
                })
                .FirstOrDefaultAsync();

            // Aucun abonnement actif : pas de crédits, et on le dit clairement.
            if (subscription == null)
                return Ok(new { data = (object?)null, success = true });

            if (subscription.monthly is > 0)
            {
                // Une seule allocation par (parent, mois civil) : les crédits sont
                // strictement scopés au mois en cours, jamais reportés d'un mois sur
                // l'autre (pas de rollover) et jamais remboursés à la fin du mois (le
                // solde du mois précédent est simplement hors du filtre PeriodStart
                // ci-dessous, il n'existe nulle part une opération qui l'annule ou le
                // transfère). Si le parent change de plan en cours de mois, le montant
                // déjà alloué ce mois-ci n'est pas recalculé : le nouveau montant du
                // plan ne s'appliquera qu'à la prochaine allocation, au mois suivant.
                // Ne pas ajouter de logique de prorata ni de report ici.
                var hasAllocation = await _db.ParentCreditLedgers.AnyAsync(
                    l => l.ParentId == parentId && l.PeriodStart == periodStart && l.EntryType == "allocation");

                if (!hasAllocation)
                {
                    _db.ParentCreditLedgers.Add(new ParentCreditLedger
                    {
                        ParentId    = parentId,
                        EntryType   = "allocation",
                        Amount      = subscription.monthly.Value,
                        PeriodStart = periodStart,
                        Label       = $"Dotation mensuelle  plan {subscription.planName}"
                    });
                    await _db.SaveChangesAsync();
                }
            }

            var entries = await _db.ParentCreditLedgers.AsNoTracking()
                .Where(l => l.ParentId == parentId && l.PeriodStart == periodStart)
                .Select(l => new { l.EntryType, l.Amount })
                .ToListAsync();

            var allocated = entries.Where(e => e.EntryType == "allocation").Sum(e => e.Amount);
            var consumed  = entries.Where(e => e.EntryType == "consumption").Sum(e => e.Amount);
            var refunded  = entries.Where(e => e.EntryType == "refund").Sum(e => e.Amount);

            var childrenCount = await _db.ParentStudentLinks.CountAsync(l => l.ParentId == parentId && l.Status == "accepted");

            var daysToRenewal = subscription.EndDate.HasValue
                ? (int?)Math.Max(0, (subscription.EndDate.Value.Date - DateTime.UtcNow.Date).Days)
                : null;

            return Ok(new
            {
                data = new
                {
                    planName       = subscription.planName,
                    periodStart,
                    creditsTotal   = allocated,
                    creditsUsed    = consumed - refunded,
                    creditsLeft    = allocated - consumed + refunded,
                    currency       = "XAF",
                    childrenCount,
                    childrenLimit  = subscription.maxChildren,
                    daysToRenewal
                },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting parent credits");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("credits/history")]
    public async Task<IActionResult> GetHistory([FromQuery] int limit = 50)
    {
        try
        {
            if (limit is < 1 or > 200) limit = 50;
            var parentId = User.GetUserId();

            var items = await _db.ParentCreditLedgers.AsNoTracking()
                .Where(l => l.ParentId == parentId)
                .OrderByDescending(l => l.CreatedAt)
                .Take(limit)
                .Select(l => new
                {
                    l.Id, l.EntryType, l.Amount, l.Label, l.CreatedAt, l.PeriodStart,
                    childId   = l.ChildId,
                    childName = l.Child != null ? (l.Child.FirstName + " " + l.Child.LastName).Trim() : null
                })
                .ToListAsync();

            return Ok(new { data = items, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting parent credit history");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Retour d'usage sur les achats faits pour un enfant, 1 à 30 jours après
    /// l'achat : signal binaire "consulté / pas encore consulté", jamais un
    /// compteur (DownloadHistories n'est pas fiable pour compter les
    /// consultations réelles  déduplication incohérente selon le canal
    /// d'accès, voir parent_decisions_session.md, fonctionnalité E).
    ///
    /// OrderItem n'a pas de colonne "pour quel enfant" : le seul lien fiable
    /// entre une commande et l'enfant destinataire est ParentCreditLedger
    /// (EntryType="consumption", ChildId + OrderId), écrit par
    /// PurchaseForChild au moment de l'achat. Un achat payé autrement qu'avec
    /// les crédits mensuels n'a donc pas de suivi ici  c'est le seul système
    /// d'achat-pour-enfant réellement implémenté aujourd'hui.
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
    /// Achète une épreuve pour un enfant. Débite les crédits du mois si demandé
    /// et suffisants, crée la commande et inscrit l'enfant au contenu.
    /// </summary>
    [HttpPost("purchase-for-child")]
    public async Task<IActionResult> PurchaseForChild([FromBody] PurchaseForChildRequest request)
    {
        // Module 19 : la transaction existait mais sans isolation suffisante,
        // et le solde de crédits est lui aussi recalculé par sommation du
        // journal. Deux achats concurrents pouvaient donc consommer deux fois
        // les mêmes crédits. Même traitement que le paiement par solde
        // professeur et que la réservation de tutorat : Serializable, limité
        // à ce chemin.
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var parentId = User.GetUserId();

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

            // Idempotence dérivée côté serveur (passe de clôture du lot 0), sur
            // le modèle du paiement par solde professeur : même parent, même
            // enfant, même ensemble de contenus, fenêtre courte. Aucun client
            // n'envoie de jeton d'idempotence, l'exiger casserait le web. Un
            // rejeu renvoie la commande d'origine au lieu d'échouer sur
            // « déjà accès » (voie crédits) ou de créer une seconde commande en
            // attente (voie Mobile Money). L'enfant est reconnu par le marqueur
            // posé dans Notes, ou par l'écriture de consommation de crédits
            // pour les commandes antérieures à ce marqueur.
            var since = DateTime.UtcNow.Subtract(PurchaseForChildIdempotencyWindow);
            var childMarker = ChildPurchaseMarker(request.ChildId);
            var duplicate = await _db.Orders.AsNoTracking()
                .Where(o => o.UserId == parentId
                         && o.CreatedAt >= since
                         && (o.PaymentMethod == "parent_credits" || o.PaymentMethod == "mobile_money")
                         && o.Items.Count == 1
                         && o.Items.Any(i => i.SubjectId == request.SubjectId)
                         && (o.Notes == childMarker
                             || _db.ParentCreditLedgers.Any(l => l.OrderId == o.Id && l.ChildId == request.ChildId)))
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
                        paidWithCredits = duplicate.PaymentMethod == "parent_credits",
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

            var periodStart = CurrentPeriodStart();
            var entries = await _db.ParentCreditLedgers.AsNoTracking()
                .Where(l => l.ParentId == parentId && l.PeriodStart == periodStart)
                .Select(l => new { l.EntryType, l.Amount })
                .ToListAsync();

            var creditsLeft = entries.Where(e => e.EntryType == "allocation").Sum(e => e.Amount)
                            - entries.Where(e => e.EntryType == "consumption").Sum(e => e.Amount)
                            + entries.Where(e => e.EntryType == "refund").Sum(e => e.Amount);

            var payWithCredits = request.UseCredits && creditsLeft >= subject.Price;

            if (request.UseCredits && !payWithCredits)
                return BadRequest(new
                {
                    success = false,
                    error   = "Crédits insuffisants pour cet achat.",
                    creditsLeft,
                    price   = subject.Price
                });

            var order = new Order
            {
                UserId        = parentId,
                OrderNumber   = $"WP-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                TotalAmount   = subject.Price,
                Status        = payWithCredits ? "paid" : "pending",
                PaymentMethod = payWithCredits ? "parent_credits" : "mobile_money",
                // Bénéficiaire de l'achat, relu par la détection de doublon.
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

            if (payWithCredits)
            {
                _db.ParentCreditLedgers.Add(new ParentCreditLedger
                {
                    ParentId    = parentId,
                    EntryType   = "consumption",
                    Amount      = subject.Price,
                    ChildId     = request.ChildId,
                    OrderId     = order.Id,
                    PeriodStart = periodStart,
                    Label       = subject.Title
                });

                _db.Enrollments.Add(new Enrollment
                {
                    UserId    = request.ChildId,
                    SubjectId = subject.Id
                });

            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            if (payWithCredits)
            {
                // Lot 2, Module 1 : vente créditée à l'auteur dans le journal
                // (idempotent, rattrapé par la réconciliation en cas d'échec).
                try { await _wallet.SyncOrderAsync(order.Id); }
                catch (Exception ex) { _logger.LogError(ex, "Écritures de vente de la commande {OrderId} non posées (réconciliation à venir)", order.Id); }

                // Passe par PublishAsync (ntfy + DB) plutôt qu'un Notifications.Add direct :
                // sans ça, aucun événement SSE n'était jamais émis, donc l'enfant ne
                // voyait jamais ce contenu offert avant de recharger la page manuellement.
                // Module 22 : envoyée APRÈS la validation de la transaction, pour que
                // l'enfant ne soit jamais prévenu d'un achat finalement annulé.
                await _ntfy.PublishAsync(
                    topic: $"winplus-user-{request.ChildId}",
                    title: "Nouveau contenu disponible",
                    message: $"Un parent vient de vous offrir « {subject.Title} ».",
                    tags: new[] { "gift" },
                    userId: request.ChildId,
                    type: "content",
                    relatedEntityType: "subject",
                    relatedEntityId: subject.Id);
            }

            return Ok(new
            {
                data = new
                {
                    orderId      = order.Id,
                    orderNumber  = order.OrderNumber,
                    status       = order.Status,
                    paidWithCredits = payWithCredits,
                    amount       = subject.Price,
                    creditsLeft  = payWithCredits ? creditsLeft - subject.Price : creditsLeft
                },
                success = true
            });
        }
        catch (Exception ex) when (DbConcurrency.IsSerializationFailure(ex))
        {
            // Collision de sérialisation (40001) ou interblocage (40P01) :
            // un autre achat du même parent consommait les mêmes crédits en
            // parallèle. Même traitement que le paiement par solde
            // (OrdersController) : 409 actionnable au lieu d'un 500. La
            // transaction est annulée par `await using` à la sortie.
            _logger.LogWarning(ex, "Collision de sérialisation sur l'achat pour enfant");
            return StatusCode(409, new
            {
                success = false,
                error = "Un autre achat sur vos crédits est en cours de traitement. Réessayez dans un instant."
            });
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
    private static string ChildPurchaseMarker(int childId) => $"purchase-for-child:{childId}";
}
