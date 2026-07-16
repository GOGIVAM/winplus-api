using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public class DailyScoreService : IDailyScoreService
{
    private readonly ApplicationDbContext _db;

    public DailyScoreService(ApplicationDbContext db) => _db = db;

    public async Task UpsertDailyScoreAsync(int userId, decimal score, int? subjectId = null)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var existing = await _db.DailyScores
            .FirstOrDefaultAsync(d => d.UserId == userId && d.Date == today);

        if (existing != null)
        {
            existing.AverageScore = Math.Round(
                (existing.AverageScore * existing.QuizCount + score) / (existing.QuizCount + 1), 2);
            existing.QuizCount++;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.DailyScores.Add(new DailyScore
            {
                UserId = userId,
                Date = today,
                AverageScore = Math.Round(score, 2),
                QuizCount = 1,
                SubjectId = subjectId,
            });
        }

        await _db.SaveChangesAsync();
    }

    public async Task<IEnumerable<DailyScoreEntry>> GetScoreHistoryAsync(int userId, string period)
    {
        var days = period switch
        {
            "7d"  =>  7,
            "90d" => 90,
            _     => 30,
        };
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));

        return await _db.DailyScores
            .Where(d => d.UserId == userId && d.Date >= cutoff)
            .OrderBy(d => d.Date)
            .Select(d => new DailyScoreEntry(
                d.Date.ToString("yyyy-MM-dd"),
                (double)Math.Round(d.AverageScore, 1),
                d.QuizCount))
            .ToListAsync();
    }
}
