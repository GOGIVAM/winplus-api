using Backend.Models.Entities;

namespace Backend.Repositories;

public interface IInstitutionRepository
{
    Task<Institution?> GetByIdAsync(int id);
    Task<IEnumerable<Institution>> GetAllAsync();
    Task<IEnumerable<Institution>> GetAllAsync(int page, int limit);
    Task<IEnumerable<Institution>> GetByCountryAsync(string country);
    Task<IEnumerable<Institution>> GetByTypeAsync(string type);
    Task<IEnumerable<Institution>> GetActiveAsync();
    Task<Institution> CreateAsync(Institution institution);
    Task<Institution> UpdateAsync(Institution institution);
    Task<bool> DeleteAsync(int id);
    Task<int> CountAsync();
}
