using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class SessionRepository : ISessionRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SessionRepository> _logger;

    public SessionRepository(ApplicationDbContext context, ILogger<SessionRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Session?> GetByIdAsync(int id)
    {
        try
        {
            return await _context.Sessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session by id {SessionId}", id);
            return null;
        }
    }

    public async Task<IEnumerable<Session>> GetAllAsync()
    {
        try
        {
            return await _context.Sessions
                .AsNoTracking()
                .OrderByDescending(s => s.StartDate)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all sessions");
            return Enumerable.Empty<Session>();
        }
    }

    public async Task<IEnumerable<Session>> GetAllAsync(int page, int limit)
    {
        try
        {
            var skip = (page - 1) * limit;
            return await _context.Sessions
                .AsNoTracking()
                .OrderByDescending(s => s.StartDate)
                .Skip(skip)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paginated sessions (page {Page}, limit {Limit})", page, limit);
            return Enumerable.Empty<Session>();
        }
    }

    public async Task<IEnumerable<Session>> GetUpcomingAsync()
    {
        try
        {
            var now = DateTime.UtcNow;
            return await _context.Sessions
                .Where(s => s.StartDate >= now && s.Status == "Scheduled")
                .AsNoTracking()
                .OrderBy(s => s.StartDate)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting upcoming sessions");
            return Enumerable.Empty<Session>();
        }
    }

    public async Task<IEnumerable<Session>> GetByTeacherAsync(int teacherId)
    {
        try
        {
            return await _context.Sessions
                .Where(s => s.CreatedBy == teacherId)
                .AsNoTracking()
                .OrderByDescending(s => s.StartDate)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sessions by teacher {TeacherId}", teacherId);
            return Enumerable.Empty<Session>();
        }
    }

    public async Task<IEnumerable<Session>> GetByStatusAsync(string status)
    {
        try
        {
            return await _context.Sessions
                .Where(s => s.Status == status)
                .AsNoTracking()
                .OrderByDescending(s => s.StartDate)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sessions by status {Status}", status);
            return Enumerable.Empty<Session>();
        }
    }

    public async Task<IEnumerable<Session>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            return await _context.Sessions
                .Where(s => s.StartDate >= startDate && s.EndDate <= endDate)
                .AsNoTracking()
                .OrderBy(s => s.StartDate)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sessions by date range");
            return Enumerable.Empty<Session>();
        }
    }

    public async Task<Session> CreateAsync(Session session)
    {
        try
        {
            await _context.Sessions.AddAsync(session);
            await _context.SaveChangesAsync();
            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating session");
            throw;
        }
    }

    public async Task<Session> UpdateAsync(Session session)
    {
        try
        {
            _context.Sessions.Update(session);
            await _context.SaveChangesAsync();
            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating session {SessionId}", session.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            var session = await _context.Sessions.FindAsync(id);
            if (session == null)
                return false;

            _context.Sessions.Remove(session);
            await _context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting session {SessionId}", id);
            throw;
        }
    }

    public async Task<int> CountAsync()
    {
        try
        {
            return await _context.Sessions.CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting sessions");
            return 0;
        }
    }
}
