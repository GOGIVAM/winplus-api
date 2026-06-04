using Backend.Models.Entities;
using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

/// <summary>
/// Interface pour le repository de l'historique d'apprentissage
/// </summary>
public interface IHistoryRepository
{
    Task<LearningHistory?> GetByIdAsync(int id);
    Task<List<LearningHistory>> GetByUserIdAsync(int userId, int page = 1, int limit = 50);
    Task<List<LearningHistory>> GetByUserAndTypeAsync(int userId, string eventType, int page = 1, int limit = 50);
    Task<List<LearningHistory>> GetByUserAndSubjectAsync(int userId, int subjectId, int page = 1, int limit = 50);
    Task<List<LearningHistory>> GetByUserAndDateRangeAsync(int userId, DateTime startDate, DateTime endDate, int page = 1, int limit = 50);
    Task<int> GetTotalCountByUserAsync(int userId);
    Task<LearningHistory> CreateAsync(LearningHistory history);
    Task<LearningHistory> UpdateAsync(LearningHistory history);
    Task<bool> DeleteAsync(int id);
    Task<bool> DeleteByUserIdAsync(int userId);
    Task<List<LearningHistory>> GetRecentActivityAsync(int userId, int count = 10);
    Task<int> GetCompletedCoursesCountAsync(int userId);
    Task<int> GetTotalTimeSpentAsync(int userId); // in seconds
    Task<float> GetAverageScoreAsync(int userId);
    Task<Dictionary<string, int>> GetEventTypeBreakdownAsync(int userId);
}

/// <summary>
/// Repository pour l'historique d'apprentissage
/// </summary>
public class HistoryRepository : IHistoryRepository
{
    private readonly ApplicationDbContext _context;

    public HistoryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<LearningHistory?> GetByIdAsync(int id)
    {
        return await _context.LearningHistories
            .Include(h => h.Subject)
            .FirstOrDefaultAsync(h => h.Id == id);
    }

    public async Task<List<LearningHistory>> GetByUserIdAsync(int userId, int page = 1, int limit = 50)
    {
        return await _context.LearningHistories
            .Include(h => h.Subject)
            .Where(h => h.UserId == userId)
            .AsNoTracking()
            .OrderByDescending(h => h.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<LearningHistory>> GetByUserAndTypeAsync(int userId, string eventType, int page = 1, int limit = 50)
    {
        return await _context.LearningHistories
            .Include(h => h.Subject)
            .Where(h => h.UserId == userId && h.EventType == eventType)
            .AsNoTracking()
            .OrderByDescending(h => h.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<LearningHistory>> GetByUserAndSubjectAsync(int userId, int subjectId, int page = 1, int limit = 50)
    {
        return await _context.LearningHistories
            .Include(h => h.Subject)
            .Where(h => h.UserId == userId && h.SubjectId == subjectId)
            .AsNoTracking()
            .OrderByDescending(h => h.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<LearningHistory>> GetByUserAndDateRangeAsync(int userId, DateTime startDate, DateTime endDate, int page = 1, int limit = 50)
    {
        return await _context.LearningHistories
            .Include(h => h.Subject)
            .Where(h => h.UserId == userId && h.CreatedAt >= startDate && h.CreatedAt <= endDate)
            .OrderByDescending(h => h.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> GetTotalCountByUserAsync(int userId)
    {
        return await _context.LearningHistories
            .Where(h => h.UserId == userId)
            .CountAsync();
    }

    public async Task<LearningHistory> CreateAsync(LearningHistory history)
    {
        _context.LearningHistories.Add(history);
        await _context.SaveChangesAsync();
        return history;
    }

    public async Task<LearningHistory> UpdateAsync(LearningHistory history)
    {
        history.UpdatedAt = DateTime.UtcNow;
        _context.LearningHistories.Update(history);
        await _context.SaveChangesAsync();
        return history;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var history = await GetByIdAsync(id);
        if (history == null) return false;

        _context.LearningHistories.Remove(history);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteByUserIdAsync(int userId)
    {
        var histories = await _context.LearningHistories
            .Where(h => h.UserId == userId)
            .ToListAsync();

        _context.LearningHistories.RemoveRange(histories);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<List<LearningHistory>> GetRecentActivityAsync(int userId, int count = 10)
    {
        return await _context.LearningHistories
            .Include(h => h.Subject)
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<int> GetCompletedCoursesCountAsync(int userId)
    {
        return await _context.LearningHistories
            .Where(h => h.UserId == userId && h.EventType == "course_completed")
            .CountAsync();
    }

    public async Task<int> GetTotalTimeSpentAsync(int userId)
    {
        var total = await _context.LearningHistories
            .Where(h => h.UserId == userId && h.DurationSeconds.HasValue)
            .SumAsync(h => h.DurationSeconds);
        
        return total ?? 0;
    }

    public async Task<float> GetAverageScoreAsync(int userId)
    {
        var scoredHistories = await _context.LearningHistories
            .Where(h => h.UserId == userId && h.Score.HasValue)
            .ToListAsync();

        if (scoredHistories.Count == 0) return 0;
        
        return (float)scoredHistories.Average(h => h.Score ?? 0);
    }

    public async Task<Dictionary<string, int>> GetEventTypeBreakdownAsync(int userId)
    {
        var breakdown = await _context.LearningHistories
            .Where(h => h.UserId == userId)
            .GroupBy(h => h.EventType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();

        return breakdown.ToDictionary(x => x.Type, x => x.Count);
    }
}
