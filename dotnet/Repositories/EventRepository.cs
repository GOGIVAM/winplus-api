using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class EventRepository : IEventRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<EventRepository> _logger;

    public EventRepository(ApplicationDbContext context, ILogger<EventRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Event?> GetByIdAsync(int id)
    {
        try
        {
            return await _context.Events
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting event by id {EventId}", id);
            return null;
        }
    }

    public async Task<IEnumerable<Event>> GetAllAsync()
    {
        try
        {
            return await _context.Events
                .AsNoTracking()
                .OrderByDescending(e => e.StartDate)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all events");
            return Enumerable.Empty<Event>();
        }
    }

    public async Task<IEnumerable<Event>> GetAllAsync(int page, int limit)
    {
        try
        {
            var skip = (page - 1) * limit;
            return await _context.Events
                .AsNoTracking()
                .OrderByDescending(e => e.StartDate)
                .Skip(skip)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paginated events (page {Page}, limit {Limit})", page, limit);
            return Enumerable.Empty<Event>();
        }
    }

    public async Task<IEnumerable<Event>> GetByTypeAsync(string eventType)
    {
        try
        {
            return await _context.Events
                .Where(e => e.EventType == eventType)
                .AsNoTracking()
                .OrderByDescending(e => e.StartDate)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting events by type {EventType}", eventType);
            return Enumerable.Empty<Event>();
        }
    }

    public async Task<IEnumerable<Event>> GetUpcomingAsync(int limit = 10)
    {
        try
        {
            var now = DateTime.UtcNow;
            return await _context.Events
                .Where(e => e.StartDate >= now)
                .AsNoTracking()
                .OrderBy(e => e.StartDate)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting upcoming events");
            return Enumerable.Empty<Event>();
        }
    }

    public async Task<IEnumerable<Event>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            return await _context.Events
                .Where(e => e.StartDate >= startDate && e.EndDate <= endDate)
                .AsNoTracking()
                .OrderBy(e => e.StartDate)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting events by date range");
            return Enumerable.Empty<Event>();
        }
    }

    public async Task<Event> CreateAsync(Event eventItem)
    {
        try
        {
            await _context.Events.AddAsync(eventItem);
            await _context.SaveChangesAsync();
            return eventItem;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating event");
            throw;
        }
    }

    public async Task<Event> UpdateAsync(Event eventItem)
    {
        try
        {
            _context.Events.Update(eventItem);
            await _context.SaveChangesAsync();
            return eventItem;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating event {EventId}", eventItem.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            var eventItem = await _context.Events.FindAsync(id);
            if (eventItem == null)
                return false;

            _context.Events.Remove(eventItem);
            await _context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting event {EventId}", id);
            throw;
        }
    }

    public async Task<int> CountAsync()
    {
        try
        {
            return await _context.Events.CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting events");
            return 0;
        }
    }
}
