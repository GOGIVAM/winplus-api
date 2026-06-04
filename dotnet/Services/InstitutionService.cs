using Backend.Models.Entities;
using Backend.Repositories;

namespace Backend.Services;

public interface IInstitutionService
{
    Task<IEnumerable<Institution>> GetAllInstitutionsAsync();
    Task<IEnumerable<Institution>> GetInstitutionsByCountryAsync(string country);
    Task<Institution?> GetInstitutionByIdAsync(int id);
    Task<Institution> CreateInstitutionAsync(Institution institution);
    Task<Institution> UpdateInstitutionAsync(Institution institution);
    Task<bool> DeleteInstitutionAsync(int id);
}

public class InstitutionService : IInstitutionService
{
    private readonly IRepository<Institution> _institutionRepository;
    private readonly ILogger<InstitutionService> _logger;

    public InstitutionService(IRepository<Institution> institutionRepository, ILogger<InstitutionService> logger)
    {
        _institutionRepository = institutionRepository ?? throw new ArgumentNullException(nameof(institutionRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IEnumerable<Institution>> GetAllInstitutionsAsync()
    {
        try
        {
            var institutions = await _institutionRepository.GetAllAsync();
            return institutions.Where(i => !i.IsDeleted && i.IsActive).OrderBy(i => i.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all institutions");
            return Enumerable.Empty<Institution>();
        }
    }

    public async Task<IEnumerable<Institution>> GetInstitutionsByCountryAsync(string country)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(country))
                return await GetAllInstitutionsAsync();

            var institutions = await _institutionRepository.GetAllAsync();
            return institutions
                .Where(i => !i.IsDeleted && i.IsActive && 
                           i.Country.Equals(country, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institutions by country {Country}", country);
            return Enumerable.Empty<Institution>();
        }
    }

    public async Task<Institution?> GetInstitutionByIdAsync(int id)
    {
        try
        {
            var institution = await _institutionRepository.GetByIdAsync(id);
            return institution?.IsDeleted == false && institution?.IsActive == true ? institution : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institution {InstitutionId}", id);
            return null;
        }
    }

    public async Task<Institution> CreateInstitutionAsync(Institution institution)
    {
        try
        {
            if (institution == null)
                throw new ArgumentNullException(nameof(institution));

            institution.CreatedAt = DateTime.UtcNow;
            institution.IsDeleted = false;
            institution.IsActive = true;
            
            return await _institutionRepository.CreateAsync(institution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating institution");
            throw;
        }
    }

    public async Task<Institution> UpdateInstitutionAsync(Institution institution)
    {
        try
        {
            if (institution == null)
                throw new ArgumentNullException(nameof(institution));

            institution.UpdatedAt = DateTime.UtcNow;
            return await _institutionRepository.UpdateAsync(institution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating institution {InstitutionId}", institution.Id);
            throw;
        }
    }

    public async Task<bool> DeleteInstitutionAsync(int id)
    {
        try
        {
            var institution = await _institutionRepository.GetByIdAsync(id);
            if (institution == null)
                return false;

            institution.IsDeleted = true;
            institution.UpdatedAt = DateTime.UtcNow;
            await _institutionRepository.UpdateAsync(institution);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting institution {InstitutionId}", id);
            return false;
        }
    }
}
