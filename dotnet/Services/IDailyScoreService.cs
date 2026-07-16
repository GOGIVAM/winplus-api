namespace Backend.Services;

public record DailyScoreEntry(string Date, double Score, int QuizCount);

public interface IDailyScoreService
{
    Task UpsertDailyScoreAsync(int userId, decimal score, int? subjectId = null);
    Task<IEnumerable<DailyScoreEntry>> GetScoreHistoryAsync(int userId, string period);
}
