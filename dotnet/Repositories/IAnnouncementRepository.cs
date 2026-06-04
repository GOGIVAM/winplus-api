using Backend.Models.Entities;

namespace Backend.Repositories;

public interface IAnnouncementRepository
{
    Task<Announcement?> GetByIdAsync(int id);
    Task<IEnumerable<Announcement>> GetAllAsync();
    Task<IEnumerable<Announcement>> GetAllAsync(int page, int limit);
    Task<IEnumerable<Announcement>> GetPublishedAsync();
    Task<IEnumerable<Announcement>> GetByPriorityAsync(int priority);
    Task<IEnumerable<Announcement>> GetRecentAsync(int limit = 10);
    Task<Announcement> CreateAsync(Announcement announcement);
    Task<Announcement> UpdateAsync(Announcement announcement);
    Task<bool> DeleteAsync(int id);
    Task<int> CountAsync();
}
