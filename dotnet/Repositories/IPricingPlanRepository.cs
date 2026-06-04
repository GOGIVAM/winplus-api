using Backend.Models.Entities;

namespace Backend.Repositories;

public interface IPricingPlanRepository
{
    Task<PricingPlan?> GetByIdAsync(int id);
    Task<IEnumerable<PricingPlan>> GetAllAsync();
    Task<IEnumerable<PricingPlan>> GetAllAsync(int page, int limit);
    Task<IEnumerable<PricingPlan>> GetByCategoryAsync(string category);
    Task<IEnumerable<PricingPlan>> GetPopularAsync();
    Task<IEnumerable<PricingPlan>> GetActiveAsync();
    Task<PricingPlan> CreateAsync(PricingPlan plan);
    Task<PricingPlan> UpdateAsync(PricingPlan plan);
    Task<bool> DeleteAsync(int id);
    Task<int> CountAsync();
}
