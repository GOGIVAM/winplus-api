using Backend.Models.Entities;

namespace Backend.Repositories;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(int id);
    Task<Order?> GetByOrderNumberAsync(string orderNumber);
    Task<IEnumerable<Order>> GetByUserIdAsync(int userId);
    Task<IEnumerable<Order>> GetAllAsync();
    Task<IEnumerable<Order>> GetAllAsync(int page, int limit);
    Task<Order> CreateAsync(Order order);
    Task<Order> UpdateAsync(Order order);
    Task<bool> DeleteAsync(int id);
    Task<IEnumerable<Order>> GetByStatusAsync(string status);
    Task<decimal> GetTotalRevenueAsync();
    Task<int> GetCountAsync();
}
