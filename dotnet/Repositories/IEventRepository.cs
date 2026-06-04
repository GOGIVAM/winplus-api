using Backend.Models.Entities;

namespace Backend.Repositories;

public interface IEventRepository
{
    Task<Event?> GetByIdAsync(int id);
    Task<IEnumerable<Event>> GetAllAsync();
    Task<IEnumerable<Event>> GetAllAsync(int page, int limit);
    Task<IEnumerable<Event>> GetByTypeAsync(string eventType);
    Task<IEnumerable<Event>> GetUpcomingAsync(int limit = 10);
    Task<IEnumerable<Event>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<Event> CreateAsync(Event eventItem);
    Task<Event> UpdateAsync(Event eventItem);
    Task<bool> DeleteAsync(int id);
    Task<int> CountAsync();
}
