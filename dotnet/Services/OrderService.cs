using Backend.Models.Entities;
using Backend.Repositories;

namespace Backend.Services;

public interface IOrderService
{
    Task<Order> CreateOrderAsync(int userId, string paymentMethod);
    Task<IEnumerable<Order>> GetUserOrdersAsync(int userId);
    Task<IEnumerable<Order>> GetUserOrdersAsync(int userId, int page, int limit);
    Task<Order?> GetOrderByIdAsync(int orderId);
    Task<Order?> GetOrderByNumberAsync(string orderNumber);
    Task<Order> UpdateOrderStatusAsync(int orderId, string status);
    Task<bool> CancelOrderAsync(int orderId);
    Task<decimal> GetTotalRevenueAsync();
    Task<int> GetOrderCountAsync();
    Task<IEnumerable<Order>> GetOrdersByStatusAsync(string status);
}

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICartRepository _cartRepository;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository orderRepository,
        ICartRepository cartRepository,
        ILogger<OrderService> logger)
    {
        _orderRepository = orderRepository;
        _cartRepository = cartRepository;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(int userId, string paymentMethod)
    {
        try
        {
            if (string.IsNullOrEmpty(paymentMethod))
                throw new ArgumentException("Payment method is required");

            // Get cart items
            var cartItems = await _cartRepository.GetByUserIdAsync(userId);
            if (!cartItems.Any())
                throw new InvalidOperationException("Cart is empty");

            // Calculate total
            var totalAmount = cartItems.Sum(c => c.Price);

            // Get user (for navigation property)
            var user = cartItems.FirstOrDefault()?.User;
            if (user == null)
                throw new InvalidOperationException($"User {userId} not found in cart items");

            // Create order
            var order = new Order
            {
                UserId = userId,
                User = user,
                OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}",
                TotalAmount = totalAmount,
                Status = "pending",
                PaymentMethod = paymentMethod,
                OrderDate = DateTime.UtcNow,
                Items = new List<OrderItem>()
            };

            // Add items to order
            foreach (var cartItem in cartItems)
            {
                order.Items.Add(new OrderItem
                {
                    OrderId = order.Id,
                    SubjectId = cartItem.SubjectId,
                    PriceAtPurchase = cartItem.Price,
                    Order = order
                });
            }

            var createdOrder = await _orderRepository.CreateAsync(order);

            // Clear cart
            await _cartRepository.ClearUserCartAsync(userId);

            _logger.LogInformation("Order created {OrderNumber} for user {UserId}", createdOrder.OrderNumber, userId);

            return createdOrder;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating order for user {UserId}", userId);
            throw;
        }
    }

    public async Task<IEnumerable<Order>> GetUserOrdersAsync(int userId)
    {
        try
        {
            return await _orderRepository.GetByUserIdAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting orders for user {UserId}", userId);
            return Enumerable.Empty<Order>();
        }
    }

    public async Task<IEnumerable<Order>> GetUserOrdersAsync(int userId, int page, int limit)
    {
        try
        {
            if (page < 1) page = 1;
            if (limit < 1 || limit > 100) limit = 20;

            var skip = (page - 1) * limit;
            var orders = await _orderRepository.GetByUserIdAsync(userId);
            return orders.Skip(skip).Take(limit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paginated orders for user {UserId} (page {Page}, limit {Limit})", userId, page, limit);
            return Enumerable.Empty<Order>();
        }
    }

    public async Task<Order?> GetOrderByIdAsync(int orderId)
    {
        try
        {
            return await _orderRepository.GetByIdAsync(orderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order {OrderId}", orderId);
            return null;
        }
    }

    public async Task<Order?> GetOrderByNumberAsync(string orderNumber)
    {
        try
        {
            if (string.IsNullOrEmpty(orderNumber))
                throw new ArgumentException("Order number is required");

            return await _orderRepository.GetByOrderNumberAsync(orderNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order {OrderNumber}", orderNumber);
            return null;
        }
    }

    public async Task<Order> UpdateOrderStatusAsync(int orderId, string status)
    {
        try
        {
            if (string.IsNullOrEmpty(status))
                throw new ArgumentException("Status is required");

            var validStatuses = new[] { "pending", "processing", "completed", "cancelled", "failed" };
            if (!validStatuses.Contains(status.ToLower()))
                throw new ArgumentException($"Invalid status. Must be one of: {string.Join(", ", validStatuses)}");

            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
                throw new InvalidOperationException($"Order {orderId} not found");

            order.Status = status;

            return await _orderRepository.UpdateAsync(order);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating order status {OrderId}", orderId);
            throw;
        }
    }

    public async Task<bool> CancelOrderAsync(int orderId)
    {
        try
        {
            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
                throw new InvalidOperationException($"Order {orderId} not found");

            if (order.Status == "completed")
                throw new InvalidOperationException("Cannot cancel a completed order");

            order.Status = "cancelled";

            await _orderRepository.UpdateAsync(order);
            _logger.LogInformation("Order {OrderId} cancelled", orderId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling order {OrderId}", orderId);
            throw;
        }
    }

    public async Task<decimal> GetTotalRevenueAsync()
    {
        try
        {
            return await _orderRepository.GetTotalRevenueAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating total revenue");
            return 0;
        }
    }

    public async Task<int> GetOrderCountAsync()
    {
        try
        {
            // Get all orders and count them
            var orders = await _orderRepository.GetAllAsync();
            return orders.Count();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order count");
            return 0;
        }
    }

    public async Task<IEnumerable<Order>> GetOrdersByStatusAsync(string status)
    {
        try
        {
            if (string.IsNullOrEmpty(status))
                throw new ArgumentException("Status is required");

            return await _orderRepository.GetByStatusAsync(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting orders by status");
            return Enumerable.Empty<Order>();
        }
    }
}
