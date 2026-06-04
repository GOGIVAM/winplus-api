using Backend.Models.Entities;
using Backend.Repositories;

namespace Backend.Services;

public interface IPricingService
{
    Task<IEnumerable<PricingPlan>> GetAllPlansAsync();
    Task<IEnumerable<PricingPlan>> GetPlansByCategoryAsync(string category);
    Task<PricingPlan?> GetPlanByIdAsync(int id);
    Task<PricingPlan> CreatePlanAsync(PricingPlan plan);
    Task<PricingPlan> UpdatePlanAsync(PricingPlan plan);
    Task<bool> DeletePlanAsync(int id);
}

public class PricingService : IPricingService
{
    private readonly IRepository<PricingPlan> _pricingRepository;
    private readonly ILogger<PricingService> _logger;

    public PricingService(IRepository<PricingPlan> pricingRepository, ILogger<PricingService> logger)
    {
        _pricingRepository = pricingRepository ?? throw new ArgumentNullException(nameof(pricingRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IEnumerable<PricingPlan>> GetAllPlansAsync()
    {
        try
        {
            var plans = await _pricingRepository.GetAllAsync();
            return plans.Where(p => !p.IsDeleted).OrderBy(p => p.Price);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all pricing plans");
            return Enumerable.Empty<PricingPlan>();
        }
    }

    public async Task<IEnumerable<PricingPlan>> GetPlansByCategoryAsync(string category)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(category))
                return await GetAllPlansAsync();

            var plans = await _pricingRepository.GetAllAsync();
            return plans
                .Where(p => !p.IsDeleted && p.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Price);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pricing plans by category {Category}", category);
            return Enumerable.Empty<PricingPlan>();
        }
    }

    public async Task<PricingPlan?> GetPlanByIdAsync(int id)
    {
        try
        {
            var plan = await _pricingRepository.GetByIdAsync(id);
            return plan?.IsDeleted == false ? plan : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pricing plan {PlanId}", id);
            return null;
        }
    }

    public async Task<PricingPlan> CreatePlanAsync(PricingPlan plan)
    {
        try
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));

            plan.CreatedAt = DateTime.UtcNow;
            plan.IsDeleted = false;
            
            return await _pricingRepository.CreateAsync(plan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating pricing plan");
            throw;
        }
    }

    public async Task<PricingPlan> UpdatePlanAsync(PricingPlan plan)
    {
        try
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));

            plan.UpdatedAt = DateTime.UtcNow;
            return await _pricingRepository.UpdateAsync(plan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating pricing plan {PlanId}", plan.Id);
            throw;
        }
    }

    public async Task<bool> DeletePlanAsync(int id)
    {
        try
        {
            var plan = await _pricingRepository.GetByIdAsync(id);
            if (plan == null)
                return false;

            plan.IsDeleted = true;
            plan.UpdatedAt = DateTime.UtcNow;
            await _pricingRepository.UpdateAsync(plan);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting pricing plan {PlanId}", id);
            return false;
        }
    }
}
