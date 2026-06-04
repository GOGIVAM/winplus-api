using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class InstitutionRepository : IInstitutionRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<InstitutionRepository> _logger;

    public InstitutionRepository(ApplicationDbContext context, ILogger<InstitutionRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Institution?> GetByIdAsync(int id)
    {
        try
        {
            return await _context.Institutions
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institution by id {InstitutionId}", id);
            return null;
        }
    }

    public async Task<IEnumerable<Institution>> GetAllAsync()
    {
        try
        {
            return await _context.Institutions
                .Where(i => i.IsActive)
                .AsNoTracking()
                .OrderBy(i => i.Name)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all institutions");
            return Enumerable.Empty<Institution>();
        }
    }

    public async Task<IEnumerable<Institution>> GetAllAsync(int page, int limit)
    {
        try
        {
            var skip = (page - 1) * limit;
            return await _context.Institutions
                .Where(i => i.IsActive)
                .AsNoTracking()
                .OrderBy(i => i.Name)
                .Skip(skip)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paginated institutions (page {Page}, limit {Limit})", page, limit);
            return Enumerable.Empty<Institution>();
        }
    }

    public async Task<IEnumerable<Institution>> GetByCountryAsync(string country)
    {
        try
        {
            return await _context.Institutions
                .Where(i => i.Country == country && i.IsActive)
                .AsNoTracking()
                .OrderBy(i => i.Name)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institutions by country {Country}", country);
            return Enumerable.Empty<Institution>();
        }
    }

    public async Task<IEnumerable<Institution>> GetByTypeAsync(string type)
    {
        try
        {
            return await _context.Institutions
                .Where(i => i.Type == type && i.IsActive)
                .AsNoTracking()
                .OrderBy(i => i.Name)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institutions by type {Type}", type);
            return Enumerable.Empty<Institution>();
        }
    }

    public async Task<IEnumerable<Institution>> GetActiveAsync()
    {
        try
        {
            return await _context.Institutions
                .Where(i => i.IsActive)
                .AsNoTracking()
                .OrderBy(i => i.Name)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active institutions");
            return Enumerable.Empty<Institution>();
        }
    }

    public async Task<Institution> CreateAsync(Institution institution)
    {
        try
        {
            await _context.Institutions.AddAsync(institution);
            await _context.SaveChangesAsync();
            return institution;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating institution");
            throw;
        }
    }

    public async Task<Institution> UpdateAsync(Institution institution)
    {
        try
        {
            _context.Institutions.Update(institution);
            await _context.SaveChangesAsync();
            return institution;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating institution {InstitutionId}", institution.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            var institution = await _context.Institutions.FindAsync(id);
            if (institution == null)
                return false;

            _context.Institutions.Remove(institution);
            await _context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting institution {InstitutionId}", id);
            throw;
        }
    }

    public async Task<int> CountAsync()
    {
        try
        {
            return await _context.Institutions.Where(i => i.IsActive).CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting institutions");
            return 0;
        }
    }
}
