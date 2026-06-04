using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class FavoriteRepository : IFavoriteRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<FavoriteRepository> _logger;

    public FavoriteRepository(ApplicationDbContext context, ILogger<FavoriteRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Favorite?> GetByIdAsync(int id)
    {
        try
        {
            return await _context.Favorites
                .Include(f => f.User)
                .Include(f => f.Subject)
                .FirstOrDefaultAsync(f => f.Id == id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting favorite by id {FavoriteId}", id);
            return null;
        }
    }

    public async Task<IEnumerable<Favorite>> GetByUserIdAsync(int userId)
    {
        try
        {
            return await _context.Favorites
                .Where(f => f.UserId == userId)
                .Include(f => f.Subject)
                .OrderByDescending(f => f.AddedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting favorites for user {UserId}", userId);
            return Enumerable.Empty<Favorite>();
        }
    }

    public async Task<Favorite?> GetByUserAndSubjectAsync(int userId, int subjectId)
    {
        try
        {
            return await _context.Favorites
                .FirstOrDefaultAsync(f => f.UserId == userId && f.SubjectId == subjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting favorite for user {UserId} and subject {SubjectId}", userId, subjectId);
            return null;
        }
    }

    public async Task<Favorite> AddAsync(Favorite favorite)
    {
        try
        {
            favorite.AddedAt = DateTime.UtcNow;
            
            _context.Favorites.Add(favorite);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Favorite added for user {UserId}", favorite.UserId);
            return favorite;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding favorite");
            throw;
        }
    }

    public async Task<Favorite> UpdateAsync(Favorite favorite)
    {
        try
        {
            _context.Favorites.Update(favorite);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Favorite {FavoriteId} updated", favorite.Id);
            return favorite;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating favorite {FavoriteId}", favorite.Id);
            throw;
        }
    }

    public async Task<bool> RemoveAsync(int id)
    {
        try
        {
            var favorite = await _context.Favorites.FindAsync(id);
            if (favorite == null)
                return false;

            _context.Favorites.Remove(favorite);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Favorite {FavoriteId} removed", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing favorite {FavoriteId}", id);
            throw;
        }
    }

    public async Task<bool> RemoveByUserAndSubjectAsync(int userId, int subjectId)
    {
        try
        {
            var favorite = await _context.Favorites
                .FirstOrDefaultAsync(f => f.UserId == userId && f.SubjectId == subjectId);
            
            if (favorite == null)
                return false;

            _context.Favorites.Remove(favorite);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Favorite removed for user {UserId} and subject {SubjectId}", userId, subjectId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing favorite for user {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(int userId, int subjectId)
    {
        try
        {
            return await _context.Favorites
                .AnyAsync(f => f.UserId == userId && f.SubjectId == subjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking favorite existence");
            return false;
        }
    }

    public async Task<int> GetCountByUserAsync(int userId)
    {
        try
        {
            return await _context.Favorites
                .Where(f => f.UserId == userId)
                .CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting favorites for user {UserId}", userId);
            return 0;
        }
    }
}
