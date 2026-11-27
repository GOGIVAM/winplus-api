using Backend.Data;
using Backend.Models.Entities;
using Backend.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<OrderRepository> _logger;

    public OrderRepository(ApplicationDbContext context, ILogger<OrderRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Order?> GetByIdAsync(int id)
    {
        try
        {
            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order != null) await FillItemTitlesAsync(new[] { order });
            return order;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order by id {OrderId}", id);
            return null;
        }
    }

    public async Task<Order?> GetByOrderNumberAsync(string orderNumber)
    {
        try
        {
            return await _context.Orders
                .Include(o => o.User)
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order by order number {OrderNumber}", orderNumber);
            return null;
        }
    }

    public async Task<IEnumerable<Order>> GetByUserIdAsync(int userId)
    {
        try
        {
            var orders = await _context.Orders
                .WhereNotDeleted()
                .Where(o => o.UserId == userId)
                .Include(o => o.Items)
                .AsNoTracking()
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();
            await FillItemTitlesAsync(orders);
            return orders;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting orders for user {UserId}", userId);
            return Enumerable.Empty<Order>();
        }
    }

    /// <summary>Titres des lignes (épreuve ou formation) en deux requêtes pour toute la liste.</summary>
    private async Task FillItemTitlesAsync(IEnumerable<Order> orders)
    {
        var items = orders.SelectMany(o => o.Items).ToList();
        if (items.Count == 0) return;
        var subjectIds = items.Where(i => i.SubjectId != null).Select(i => i.SubjectId!.Value).Distinct().ToList();
        var courseIds = items.Where(i => i.CourseId != null).Select(i => i.CourseId!.Value).Distinct().ToList();
        var subjects = subjectIds.Count == 0 ? new Dictionary<int, string>() : await _context.Subjects.AsNoTracking()
            .Where(s => subjectIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Title);
        var courses = courseIds.Count == 0 ? new Dictionary<int, string>() : await _context.Courses.AsNoTracking()
            .Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title);
        foreach (var i in items)
            i.Title = i.SubjectId is int sid ? subjects.GetValueOrDefault(sid)
                : i.CourseId is int cid ? courses.GetValueOrDefault(cid) : null;
    }

    public async Task<IEnumerable<Order>> GetAllAsync()
    {
        try
        {
            return await _context.Orders
                .WhereNotDeleted()
                .Include(o => o.User)
                .Include(o => o.Items)
                .AsNoTracking()
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all orders");
            return Enumerable.Empty<Order>();
        }
    }

    public async Task<IEnumerable<Order>> GetAllAsync(int page, int limit)
    {
        try
        {
            var skip = (page - 1) * limit;
            return await _context.Orders
                .WhereNotDeleted()
                .Include(o => o.User)
                .Include(o => o.Items)
                .AsNoTracking()
                .OrderByDescending(o => o.OrderDate)
                .Skip(skip)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paginated orders (page {Page}, limit {Limit})", page, limit);
            return Enumerable.Empty<Order>();
        }
    }

    public async Task<Order> CreateAsync(Order order)
    {
        try
        {
            order.OrderDate  = DateTime.UtcNow;
            order.CreatedAt  = DateTime.UtcNow;
            if (string.IsNullOrEmpty(order.OrderNumber))
            {
                order.OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString().Substring(0, 6).ToUpper()}";
            }
            
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Order {OrderNumber} created successfully", order.OrderNumber);
            return order;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating order");
            throw;
        }
    }

    public async Task<Order> UpdateAsync(Order order)
    {
        try
        {
            _context.Orders.Update(order);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Order {OrderId} updated successfully", order.Id);
            return order;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating order {OrderId}", order.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            var order = await _context.Orders.FindAsync(id);
            if (order == null)
                return false;

            // Soft delete - mark as deleted instead of removing
            order.IsDeleted = true;
            
            _context.Orders.Update(order);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Order {OrderId} soft deleted successfully", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting order {OrderId}", id);
            throw;
        }
    }

    public async Task<IEnumerable<Order>> GetByStatusAsync(string status)
    {
        try
        {
            return await _context.Orders
                .Where(o => o.Status == status)
                .Include(o => o.User)
                .Include(o => o.Items)
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting orders by status {Status}", status);
            return Enumerable.Empty<Order>();
        }
    }

    public async Task<decimal> GetTotalRevenueAsync()
    {
        try
        {
            return await _context.Orders
                .Where(o => o.Status == "completed")
                .SumAsync(o => o.TotalAmount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating total revenue");
            return 0;
        }
    }

    public async Task<int> GetCountAsync()
    {
        try
        {
            return await _context.Orders.CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting orders");
            return 0;
        }
    }
}
