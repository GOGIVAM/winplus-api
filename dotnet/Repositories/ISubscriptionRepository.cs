using Backend.Models.Entities;

namespace Backend.Repositories;

public interface ISubscriptionRepository
{
    Task<Subscription?> GetByIdAsync(int id);
    Task<IEnumerable<Subscription>> GetAllAsync();
    Task<IEnumerable<Subscription>> GetAllAsync(int page, int limit);
    Task<IEnumerable<Subscription>> GetByUserAsync(int userId);
    Task<IEnumerable<Subscription>> GetActivSubscriptionsAsync();
    Task<IEnumerable<Subscription>> GetByStatusAsync(string status);
    Task<IEnumerable<Subscription>> GetExpiringAsync(DateTime beforeDate);
    Task<Subscription?> GetActiveSubscriptionAsync(int userId, int planId);
    Task<Subscription> CreateAsync(Subscription subscription);
    Task<Subscription> UpdateAsync(Subscription subscription);
    Task<bool> DeleteAsync(int id);
    Task<int> CountAsync();
}
