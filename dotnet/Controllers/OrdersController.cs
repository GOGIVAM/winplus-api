using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Services;
using Backend.Models.Entities;
using Backend.Extensions;
using Backend.Models.DTOs;

namespace Backend.Controllers;

public record CreateOrderRequest(
    string PaymentMethod,
    string? GuestEmail,
    string? GuestName,
    List<GuestOrderItem>? Items
);

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ILogger<OrdersController> _logger;
    private readonly ApplicationDbContext _db;
    private readonly IPdfService _pdfService;

    public OrdersController(
        IOrderService orderService,
        ILogger<OrdersController> logger,
        ApplicationDbContext db,
        IPdfService pdfService)
    {
        _orderService = orderService;
        _logger = logger;
        _db = db;
        _pdfService = pdfService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        try
        {
            var isAuth = User.Identity?.IsAuthenticated == true;

            if (isAuth)
            {
                var userId = User.GetUserId();
                var order = await _orderService.CreateOrderAsync(userId, request.PaymentMethod);
                return Ok(order);
            }
            else
            {
                if (request.Items == null || !request.Items.Any())
                    return BadRequest("Les articles du panier sont requis pour une commande anonyme.");

                var order = await _orderService.CreateGuestOrderAsync(
                    request.GuestEmail,
                    request.GuestName,
                    request.PaymentMethod,
                    request.Items
                );
                return Ok(order);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la création de la commande");
            return StatusCode(500, "Erreur serveur");
        }
    }

    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(PaginationResponse<Order>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrders([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var userId = User.GetUserId();
            var orders = await _orderService.GetUserOrdersAsync(userId, page, pageSize);
            var allOrders = await _orderService.GetUserOrdersAsync(userId);
            var totalCount = allOrders.Count();

            var response = new PaginationResponse<Order>(orders, totalCount, page, pageSize);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des commandes");
            return StatusCode(500, "Erreur serveur");
        }
    }

    [HttpGet("{id}")]
    [Authorize]
    public async Task<IActionResult> GetOrderById(int id)
    {
        try
        {
            var order = await _orderService.GetOrderByIdAsync(id);
            if (order == null)
                return NotFound();
            return Ok(order);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération de la commande {OrderId}", id);
            return StatusCode(500, "Erreur serveur");
        }
    }

    [HttpPost("{id}/cancel")]
    [Authorize]
    public async Task<IActionResult> CancelOrder(int id)
    {
        try
        {
            var order = await _orderService.GetOrderByIdAsync(id);
            if (order == null)
                return NotFound(new { success = false, error = "Commande introuvable" });

            if (order.Status.Equals("completed", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, error = "Impossible d'annuler une commande déjà complétée" });

            await _orderService.CancelOrderAsync(id);
            return Ok(new { data = new { id, status = "cancelled" }, success = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling order {OrderId}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("statistics")]
    [Authorize]
    public async Task<IActionResult> GetOrderStatistics()
    {
        try
        {
            var userId = User.GetUserId();
            var orders = (await _orderService.GetUserOrdersAsync(userId)).ToList();

            var data = new
            {
                total      = orders.Count,
                completed  = orders.Count(o => o.Status.Equals("completed",  StringComparison.OrdinalIgnoreCase)),
                pending    = orders.Count(o => o.Status.Equals("pending",    StringComparison.OrdinalIgnoreCase)),
                cancelled  = orders.Count(o => o.Status.Equals("cancelled",  StringComparison.OrdinalIgnoreCase)),
                totalSpent = orders
                    .Where(o => o.Status.Equals("completed", StringComparison.OrdinalIgnoreCase))
                    .Sum(o => o.TotalAmount),
            };

            return Ok(new { data, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order statistics");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("{id}/invoice")]
    [Authorize]
    public async Task<IActionResult> GetOrderInvoice(int id)
    {
        try
        {
            var order = await _db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Subject)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
                return NotFound(new { success = false, error = "Commande introuvable" });

            User? user = null;
            if (order.UserId.HasValue)
                user = await _db.Users.FindAsync(order.UserId.Value);

            var pdfBytes = _pdfService.GenerateInvoice(order, order.Items, user);
            var fileName = $"facture-{order.OrderNumber}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating invoice for order {OrderId}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("{id}/status")]
    [Authorize]
    public async Task<IActionResult> GetOrderStatus(int id)
    {
        try
        {
            var order = await _orderService.GetOrderByIdAsync(id);
            if (order == null)
                return NotFound(new { success = false, error = "Commande introuvable" });

            return Ok(new { data = new { order.Id, order.Status, order.CreatedAt }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order status {OrderId}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("search")]
    [Authorize]
    public async Task<IActionResult> SearchOrders(
        [FromQuery] string? q = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var userId = User.GetUserId();
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var query = _db.Orders.Where(o => o.UserId == userId);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(o => o.Status == status);

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(o =>
                    (o.OrderNumber != null && o.OrderNumber.Contains(q)) ||
                    (o.GuestEmail  != null && o.GuestEmail.Contains(q)));

            var total  = await query.CountAsync();
            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(o => new { o.Id, o.OrderNumber, o.Status, o.TotalAmount, o.CreatedAt })
                .ToListAsync();

            return Ok(new { data = orders, total, page, pageSize, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching orders");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPost("summary")]
    public async Task<IActionResult> GetOrderSummary([FromBody] List<int> subjectIds)
    {
        try
        {
            if (subjectIds == null || subjectIds.Count == 0)
                return BadRequest(new { success = false, error = "Aucun sujet fourni" });

            var subjects = await _db.Subjects
                .Where(s => subjectIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Title, s.Price })
                .ToListAsync();

            var subtotal = subjects.Sum(s => s.Price);
            const decimal taxRate = 0.20m;
            var tax   = Math.Round(subtotal * taxRate, 2);
            var total = Math.Round(subtotal + tax, 2);

            return Ok(new
            {
                data = new
                {
                    items    = subjects,
                    subtotal = Math.Round(subtotal, 2),
                    tax,
                    total,
                    currency = "XAF"
                },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order summary");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
