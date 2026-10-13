using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;

namespace Backend.Controllers;

/// <summary>
/// Écran « Commandes » (lot 9.7a, suivi 18.1) : vraies commandes, paginées et
/// filtrées en base, avec leur détail. L'écran lisait jusqu'ici au plus 50
/// PAIEMENTS (GET /admin/payments), affichait un mode de paiement écrit en
/// dur et un bouton « Voir » sans effet.
///
///   GET /api/admin/orders?q=&amp;status=&amp;page=&amp;pageSize=   (≤ 100)
///   GET /api/admin/orders/{id}                         détail : lignes, paiements, remboursement
/// </summary>
[ApiController]
[Route("api/admin/orders")]
[Authorize(Policy = "AdminOnly")]
[Produces("application/json")]
public class AdminOrdersController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public AdminOrdersController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? q, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Orders.AsNoTracking().Where(o => !o.IsDeleted);
        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var s = status.Trim().ToLower();
            query = s == "completed"
                ? query.Where(o => o.Status.ToLower() == "completed" || o.Status.ToLower() == "paid")
                : query.Where(o => o.Status.ToLower() == s);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(o => o.OrderNumber.ToLower().Contains(term)
                || (o.User != null && (o.User.Email.ToLower().Contains(term)
                    || (o.User.FirstName + " " + o.User.LastName).ToLower().Contains(term)))
                || (o.GuestEmail != null && o.GuestEmail.ToLower().Contains(term)));
        }

        var total = await query.CountAsync();
        var totalAmount = await query.Where(o => o.Status.ToLower() == "completed" || o.Status.ToLower() == "paid")
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;
        var items = await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new
            {
                id = o.Id,
                orderNumber = o.OrderNumber,
                status = o.Status.ToLower(),
                totalAmount = o.TotalAmount,
                discountAmount = o.DiscountAmount,
                paymentMethod = o.PaymentMethod,
                @operator = o.Payments.OrderByDescending(p => p.CreatedAt).Select(p => p.Operator).FirstOrDefault(),
                itemsCount = o.Items.Count,
                createdAt = o.CreatedAt,
                completedAt = o.CompletedDate,
                customerName = o.User != null ? (o.User.FirstName + " " + o.User.LastName).Trim() : o.GuestName,
                customerEmail = o.User != null ? o.User.Email : o.GuestEmail,
                userId = o.UserId,
            })
            .ToListAsync();

        return Ok(new { success = true, data = new { items, total, page, pageSize, totalPaidAmount = totalAmount } });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        var order = await _db.Orders.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new
            {
                id = o.Id,
                orderNumber = o.OrderNumber,
                status = o.Status.ToLower(),
                totalAmount = o.TotalAmount,
                discountAmount = o.DiscountAmount,
                promoCode = o.PromoCode,
                referralCode = o.ReferralCode,
                paymentMethod = o.PaymentMethod,
                notes = o.Notes,
                createdAt = o.CreatedAt,
                completedAt = o.CompletedDate,
                updatedAt = o.UpdatedAt,
                customer = o.User == null
                    ? new { id = (int?)null, name = o.GuestName, email = o.GuestEmail, role = (string?)null }
                    : new { id = (int?)o.User.Id, name = (o.User.FirstName + " " + o.User.LastName).Trim(), email = o.User.Email, role = (string?)o.User.Role },
                items = o.Items.Select(i => new
                {
                    id = i.Id,
                    subjectId = i.CourseId == null ? (int?)i.SubjectId : null,
                    courseId = i.CourseId,
                    title = i.Course != null ? i.Course.Title : i.Subject != null ? i.Subject.Title : null,
                    kind = i.CourseId != null ? "formation" : "contenu",
                    price = i.PriceAtPurchase,
                }).ToList(),
                payments = o.Payments.OrderByDescending(p => p.CreatedAt).Select(p => new
                {
                    id = p.Id,
                    amount = p.Amount,
                    currency = p.Currency,
                    status = p.Status,
                    method = p.PaymentMethod,
                    @operator = p.Operator,
                    reference = p.TransactionId ?? p.NotchpayReference,
                    fee = p.FeeAmount,
                    createdAt = p.CreatedAt,
                    completedAt = p.CompletedAt,
                    error = p.ErrorMessage,
                }).ToList(),
            })
            .FirstOrDefaultAsync();
        if (order == null) return NotFound(new { success = false, error = "Commande introuvable." });

        var refund = await _db.CatalogRefundDecisions.AsNoTracking().FirstOrDefaultAsync(d => d.OrderId == id);
        return Ok(new
        {
            success = true,
            data = new
            {
                order,
                refund = refund == null ? null : new { refund.RefundXaf, refund.FeeXaf, refund.BalancePartXaf, refund.PaymentKind, refund.DecidedAt },
            },
        });
    }
}
