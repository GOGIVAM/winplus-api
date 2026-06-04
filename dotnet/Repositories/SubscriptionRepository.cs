using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class SubscriptionRepository : ISubscriptionRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SubscriptionRepository> _logger;

    public SubscriptionRepository(ApplicationDbContext context, ILogger<SubscriptionRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Subscription?> GetByIdAsync(int id)
    {
        try
        {
            return await _context.Subscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscription by id {SubscriptionId}", id);
            return null;
        }
    }

    public async Task<IEnumerable<Subscription>> GetAllAsync()
    {
        try
        {
            return await _context.Subscriptions
                .AsNoTracking()
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all subscriptions");
            return Enumerable.Empty<Subscription>();
        }
    }

    public async Task<IEnumerable<Subscription>> GetAllAsync(int page, int limit)
    {
        try
        {
            var skip = (page - 1) * limit;
            return await _context.Subscriptions
                .AsNoTracking()
                .OrderByDescending(s => s.CreatedAt)
                .Skip(skip)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paginated subscriptions (page {Page}, limit {Limit})", page, limit);
            return Enumerable.Empty<Subscription>();
        }
    }

    public async Task<IEnumerable<Subscription>> GetByUserAsync(int userId)
    {
        try
        {
            return await _context.Subscriptions
                .Where(s => s.UserId == userId)
                .AsNoTracking()
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscriptions by user {UserId}", userId);
            return Enumerable.Empty<Subscription>();
        }
    }

    public async Task<IEnumerable<Subscription>> GetActivSubscriptionsAsync()
    {
        try
        {
            var now = DateTime.UtcNow;
            return await _context.Subscriptions
                .Where(s => s.StartDate <= now && s.EndDate >= now && s.Status == "Active")
                .AsNoTracking()
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active subscriptions");
            return Enumerable.Empty<Subscription>();
        }
    }

    public async Task<IEnumerable<Subscription>> GetByStatusAsync(string status)
    {
        try
        {
            return await _context.Subscriptions
                .Where(s => s.Status == status)
                .AsNoTracking()
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscriptions by status {Status}", status);
            return Enumerable.Empty<Subscription>();
        }
    }

    public async Task<IEnumerable<Subscription>> GetExpiringAsync(DateTime beforeDate)
    {
        try
        {
            return await _context.Subscriptions
                .Where(s => s.EndDate <= beforeDate && s.Status == "Active")
                .AsNoTracking()
                .OrderBy(s => s.EndDate)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expiring subscriptions");
            return Enumerable.Empty<Subscription>();
        }
    }

    public async Task<Subscription?> GetActiveSubscriptionAsync(int userId, int planId)
    {
        try
        {
            var now = DateTime.UtcNow;
            return await _context.Subscriptions
                .Where(s => s.UserId == userId && s.PricingPlanId == planId && 
                           s.StartDate <= now && s.EndDate >= now && s.Status == "Active")
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active subscription for user {UserId} and plan {PlanId}", userId, planId);
            return null;
        }
    }

    public async Task<Subscription> CreateAsync(Subscription subscription)
    {
        try
        {
            await _context.Subscriptions.AddAsync(subscription);
            await _context.SaveChangesAsync();
            return subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription");
            throw;
        }
    }

    public async Task<Subscription> UpdateAsync(Subscription subscription)
    {
        try
        {
            _context.Subscriptions.Update(subscription);
            await _context.SaveChangesAsync();
            return subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating subscription {SubscriptionId}", subscription.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            var subscription = await _context.Subscriptions.FindAsync(id);
            if (subscription == null)
                return false;

            _context.Subscriptions.Remove(subscription);
            await _context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting subscription {SubscriptionId}", id);
            throw;
        }
    }

    public async Task<int> CountAsync()
    {
        try
        {
            return await _context.Subscriptions.CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting subscriptions");
            return 0;
        }
    }
}
