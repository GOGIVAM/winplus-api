using Backend.Models.Entities;
using Backend.Repositories;

namespace Backend.Services;

public interface IAnnouncementService
{
    Task<IEnumerable<Announcement>> GetPublishedAnnouncementsAsync(int limit = 10);
    Task<IEnumerable<Announcement>> GetAllAnnouncementsAsync();
    Task<Announcement?> GetAnnouncementByIdAsync(int id);
    Task<Announcement> CreateAnnouncementAsync(Announcement announcement);
    Task<Announcement> UpdateAnnouncementAsync(Announcement announcement);
    Task<bool> DeleteAnnouncementAsync(int id);
    Task<bool> PublishAnnouncementAsync(int id);
}

public class AnnouncementService : IAnnouncementService
{
    private readonly IRepository<Announcement> _announcementRepository;
    private readonly ILogger<AnnouncementService> _logger;

    public AnnouncementService(IRepository<Announcement> announcementRepository, ILogger<AnnouncementService> logger)
    {
        _announcementRepository = announcementRepository ?? throw new ArgumentNullException(nameof(announcementRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IEnumerable<Announcement>> GetPublishedAnnouncementsAsync(int limit = 10)
    {
        try
        {
            var announcements = await _announcementRepository.GetAllAsync();
            var now = DateTime.UtcNow;
            
            return announcements
                .Where(a => !a.IsDeleted && 
                           a.IsPublished && 
                           a.PublishedAt <= now &&
                           (a.ExpiresAt == null || a.ExpiresAt > now))
                .OrderByDescending(a => a.PublishedAt)
                .Take(limit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting published announcements");
            return Enumerable.Empty<Announcement>();
        }
    }

    public async Task<IEnumerable<Announcement>> GetAllAnnouncementsAsync()
    {
        try
        {
            var announcements = await _announcementRepository.GetAllAsync();
            return announcements.Where(a => !a.IsDeleted).OrderByDescending(a => a.CreatedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all announcements");
            return Enumerable.Empty<Announcement>();
        }
    }

    public async Task<Announcement?> GetAnnouncementByIdAsync(int id)
    {
        try
        {
            var announcement = await _announcementRepository.GetByIdAsync(id);
            return announcement?.IsDeleted == false ? announcement : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting announcement {AnnouncementId}", id);
            return null;
        }
    }

    public async Task<Announcement> CreateAnnouncementAsync(Announcement announcement)
    {
        try
        {
            if (announcement == null)
                throw new ArgumentNullException(nameof(announcement));

            announcement.CreatedAt = DateTime.UtcNow;
            announcement.IsDeleted = false;
            
            return await _announcementRepository.CreateAsync(announcement);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating announcement");
            throw;
        }
    }

    public async Task<Announcement> UpdateAnnouncementAsync(Announcement announcement)
    {
        try
        {
            if (announcement == null)
                throw new ArgumentNullException(nameof(announcement));

            announcement.UpdatedAt = DateTime.UtcNow;
            return await _announcementRepository.UpdateAsync(announcement);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating announcement {AnnouncementId}", announcement.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAnnouncementAsync(int id)
    {
        try
        {
            var announcement = await _announcementRepository.GetByIdAsync(id);
            if (announcement == null)
                return false;

            announcement.IsDeleted = true;
            announcement.UpdatedAt = DateTime.UtcNow;
            await _announcementRepository.UpdateAsync(announcement);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting announcement {AnnouncementId}", id);
            return false;
        }
    }

    public async Task<bool> PublishAnnouncementAsync(int id)
    {
        try
        {
            var announcement = await _announcementRepository.GetByIdAsync(id);
            if (announcement == null)
                return false;

            announcement.IsPublished = true;
            announcement.PublishedAt = DateTime.UtcNow;
            announcement.UpdatedAt = DateTime.UtcNow;
            await _announcementRepository.UpdateAsync(announcement);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing announcement {AnnouncementId}", id);
            return false;
        }
    }
}
