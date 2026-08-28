using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;

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
/// GET  /api/parent/credits
/// GET  /api/parent/credits/history
/// POST /api/parent/purchase-for-child
/// </summary>
[ApiController]
[Route("api/parent")]
[Authorize]
public class ParentCreditsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ParentCreditsController> _logger;

    public ParentCreditsController(ApplicationDbContext db, ILogger<ParentCreditsController> logger)
    {
        _db = db;
        _logger = logger;
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
                        Label       = $"Dotation mensuelle — plan {subscription.planName}"
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

            var childrenCount = await _db.ParentStudentLinks.CountAsync(l => l.ParentId == parentId);

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
    /// Achète une épreuve pour un enfant. Débite les crédits du mois si demandé
    /// et suffisants, crée la commande et inscrit l'enfant au contenu.
    /// </summary>
    [HttpPost("purchase-for-child")]
    public async Task<IActionResult> PurchaseForChild([FromBody] PurchaseForChildRequest request)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var parentId = User.GetUserId();

            var linked = await _db.ParentStudentLinks
                .AnyAsync(l => l.ParentId == parentId && l.StudentId == request.ChildId);
            if (!linked)
                return StatusCode(403, new { success = false, error = "Cet enfant n'est pas lié à votre compte." });

            var subject = await _db.Subjects
                .Where(s => s.Id == request.SubjectId && !s.IsDeleted && s.IsPublished)
                .Select(s => new { s.Id, s.Title, s.Price })
                .FirstOrDefaultAsync();
            if (subject == null)
                return NotFound(new { success = false, error = "Épreuve introuvable." });

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
                PaymentMethod = payWithCredits ? "parent_credits" : "mobile_money"
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

                _db.Notifications.Add(new Notification
                {
                    UserId  = request.ChildId,
                    Title   = "Nouveau contenu disponible",
                    Message = $"Un parent vient de vous offrir « {subject.Title} ».",
                    Type    = "content",
                    RelatedEntityType = "subject",
                    RelatedEntityId   = subject.Id,
                    User    = null!
                });
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

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
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Error purchasing for child");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
