using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public OrdersController(IOrderService orderService, ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _logger = logger;
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
}
