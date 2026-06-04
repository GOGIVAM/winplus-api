using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class PricingPlanRepository : IPricingPlanRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<PricingPlanRepository> _logger;

    public PricingPlanRepository(ApplicationDbContext context, ILogger<PricingPlanRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PricingPlan?> GetByIdAsync(int id)
    {
        try
        {
            return await _context.PricingPlans
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pricing plan by id {PlanId}", id);
            return null;
        }
    }

    public async Task<IEnumerable<PricingPlan>> GetAllAsync()
    {
        try
        {
            return await _context.PricingPlans
                .AsNoTracking()
                .OrderBy(p => p.Price)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all pricing plans");
            return Enumerable.Empty<PricingPlan>();
        }
    }

    public async Task<IEnumerable<PricingPlan>> GetAllAsync(int page, int limit)
    {
        try
        {
            var skip = (page - 1) * limit;
            return await _context.PricingPlans
                .AsNoTracking()
                .OrderBy(p => p.Price)
                .Skip(skip)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paginated pricing plans (page {Page}, limit {Limit})", page, limit);
            return Enumerable.Empty<PricingPlan>();
        }
    }

    public async Task<IEnumerable<PricingPlan>> GetByCategoryAsync(string category)
    {
        try
        {
            return await _context.PricingPlans
                .Where(p => p.Category == category)
                .AsNoTracking()
                .OrderBy(p => p.Price)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pricing plans by category {Category}", category);
            return Enumerable.Empty<PricingPlan>();
        }
    }

    public async Task<IEnumerable<PricingPlan>> GetPopularAsync()
    {
        try
        {
            return await _context.PricingPlans
                .Where(p => p.IsPopular)
                .AsNoTracking()
                .OrderBy(p => p.Price)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting popular pricing plans");
            return Enumerable.Empty<PricingPlan>();
        }
    }

    public async Task<IEnumerable<PricingPlan>> GetActiveAsync()
    {
        try
        {
            return await _context.PricingPlans
                .Where(p => !p.IsArchived)
                .AsNoTracking()
                .OrderBy(p => p.Price)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active pricing plans");
            return Enumerable.Empty<PricingPlan>();
        }
    }

    public async Task<PricingPlan> CreateAsync(PricingPlan plan)
    {
        try
        {
            await _context.PricingPlans.AddAsync(plan);
            await _context.SaveChangesAsync();
            return plan;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating pricing plan");
            throw;
        }
    }

    public async Task<PricingPlan> UpdateAsync(PricingPlan plan)
    {
        try
        {
            _context.PricingPlans.Update(plan);
            await _context.SaveChangesAsync();
            return plan;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating pricing plan {PlanId}", plan.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            var plan = await _context.PricingPlans.FindAsync(id);
            if (plan == null)
                return false;

            _context.PricingPlans.Remove(plan);
            await _context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting pricing plan {PlanId}", id);
            throw;
        }
    }

    public async Task<int> CountAsync()
    {
        try
        {
            return await _context.PricingPlans.CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting pricing plans");
            return 0;
        }
    }
}
