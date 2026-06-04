using Backend.Models.Entities;

namespace Backend.Repositories;

public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(int id);
    Task<IEnumerable<Session>> GetAllAsync();
    Task<IEnumerable<Session>> GetAllAsync(int page, int limit);
    Task<IEnumerable<Session>> GetUpcomingAsync();
    Task<IEnumerable<Session>> GetByTeacherAsync(int teacherId);
    Task<IEnumerable<Session>> GetByStatusAsync(string status);
    Task<IEnumerable<Session>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<Session> CreateAsync(Session session);
    Task<Session> UpdateAsync(Session session);
    Task<bool> DeleteAsync(int id);
    Task<int> CountAsync();
}
