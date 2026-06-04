using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class AnnouncementRepository : IAnnouncementRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AnnouncementRepository> _logger;

    public AnnouncementRepository(ApplicationDbContext context, ILogger<AnnouncementRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Announcement?> GetByIdAsync(int id)
    {
        try
        {
            return await _context.Announcements
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting announcement by id {AnnouncementId}", id);
            return null;
        }
    }

    public async Task<IEnumerable<Announcement>> GetAllAsync()
    {
        try
        {
            return await _context.Announcements
                .AsNoTracking()
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all announcements");
            return Enumerable.Empty<Announcement>();
        }
    }

    public async Task<IEnumerable<Announcement>> GetAllAsync(int page, int limit)
    {
        try
        {
            var skip = (page - 1) * limit;
            return await _context.Announcements
                .AsNoTracking()
                .OrderByDescending(a => a.CreatedAt)
                .Skip(skip)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paginated announcements (page {Page}, limit {Limit})", page, limit);
            return Enumerable.Empty<Announcement>();
        }
    }

    public async Task<IEnumerable<Announcement>> GetPublishedAsync()
    {
        try
        {
            return await _context.Announcements
                .Where(a => a.IsPublished)
                .AsNoTracking()
                .OrderByDescending(a => a.PublishedAt ?? a.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting published announcements");
            return Enumerable.Empty<Announcement>();
        }
    }

    public async Task<IEnumerable<Announcement>> GetByPriorityAsync(int priority)
    {
        try
        {
            return await _context.Announcements
                .Where(a => a.Priority == priority && a.IsPublished)
                .AsNoTracking()
                .OrderByDescending(a => a.PublishedAt ?? a.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting announcements by priority {Priority}", priority);
            return Enumerable.Empty<Announcement>();
        }
    }

    public async Task<IEnumerable<Announcement>> GetRecentAsync(int limit = 10)
    {
        try
        {
            return await _context.Announcements
                .Where(a => a.IsPublished)
                .AsNoTracking()
                .OrderByDescending(a => a.PublishedAt ?? a.CreatedAt)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent announcements");
            return Enumerable.Empty<Announcement>();
        }
    }

    public async Task<Announcement> CreateAsync(Announcement announcement)
    {
        try
        {
            await _context.Announcements.AddAsync(announcement);
            await _context.SaveChangesAsync();
            return announcement;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating announcement");
            throw;
        }
    }

    public async Task<Announcement> UpdateAsync(Announcement announcement)
    {
        try
        {
            _context.Announcements.Update(announcement);
            await _context.SaveChangesAsync();
            return announcement;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating announcement {AnnouncementId}", announcement.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            var announcement = await _context.Announcements.FindAsync(id);
            if (announcement == null)
                return false;

            _context.Announcements.Remove(announcement);
            await _context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting announcement {AnnouncementId}", id);
            throw;
        }
    }

    public async Task<int> CountAsync()
    {
        try
        {
            return await _context.Announcements.CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting announcements");
            return 0;
        }
    }
}
