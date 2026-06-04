using Backend.Models.Entities;
using Backend.Models.DTOs;
using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public interface ISettingsService
{
    Task<NotificationSettingsDto> GetNotificationSettingsAsync(int userId);
    Task<NotificationSettingsDto> SaveNotificationSettingsAsync(int userId, NotificationSettingsDto settings);
    Task<PrivacySettingsDto> GetPrivacySettingsAsync(int userId);
    Task<PrivacySettingsDto> SavePrivacySettingsAsync(int userId, PrivacySettingsDto settings);
}

public class SettingsService : ISettingsService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(ApplicationDbContext context, ILogger<SettingsService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<NotificationSettingsDto> GetNotificationSettingsAsync(int userId)
    {
        try
        {
            var settings = await _context.UserNotificationSettings
                .FirstOrDefaultAsync(s => s.UserId == userId);

            if (settings == null)
            {
                // Return defaults if not found
                return new NotificationSettingsDto
                {
                    UserId = userId,
                    EmailNotifications = true,
                    PushNotifications = true,
                    CourseCommunity = true,
                    Promotions = false,
                    Newsletters = true,
                    LearningReminders = true,
                    UpdatedAt = DateTime.UtcNow
                };
            }

            return new NotificationSettingsDto
            {
                UserId = settings.UserId,
                EmailNotifications = settings.EmailNotifications,
                PushNotifications = settings.PushNotifications,
                CourseCommunity = settings.CourseCommunity,
                Promotions = settings.Promotions,
                Newsletters = settings.Newsletters,
                LearningReminders = settings.LearningReminders,
                UpdatedAt = settings.UpdatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting notification settings for user {UserId}", userId);
            throw;
        }
    }

    public async Task<NotificationSettingsDto> SaveNotificationSettingsAsync(int userId, NotificationSettingsDto settings)
    {
        try
        {
            var existing = await _context.UserNotificationSettings
                .FirstOrDefaultAsync(s => s.UserId == userId);

            if (existing == null)
            {
                var newSettings = new UserNotificationSettings
                {
                    UserId = userId,
                    EmailNotifications = settings.EmailNotifications,
                    PushNotifications = settings.PushNotifications,
                    CourseCommunity = settings.CourseCommunity,
                    Promotions = settings.Promotions,
                    Newsletters = settings.Newsletters,
                    LearningReminders = settings.LearningReminders,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.UserNotificationSettings.Add(newSettings);
                await _context.SaveChangesAsync();

                return new NotificationSettingsDto
                {
                    UserId = newSettings.UserId,
                    EmailNotifications = newSettings.EmailNotifications,
                    PushNotifications = newSettings.PushNotifications,
                    CourseCommunity = newSettings.CourseCommunity,
                    Promotions = newSettings.Promotions,
                    Newsletters = newSettings.Newsletters,
                    LearningReminders = newSettings.LearningReminders,
                    UpdatedAt = newSettings.UpdatedAt
                };
            }

            // Update existing
            existing.EmailNotifications = settings.EmailNotifications;
            existing.PushNotifications = settings.PushNotifications;
            existing.CourseCommunity = settings.CourseCommunity;
            existing.Promotions = settings.Promotions;
            existing.Newsletters = settings.Newsletters;
            existing.LearningReminders = settings.LearningReminders;
            existing.UpdatedAt = DateTime.UtcNow;

            _context.UserNotificationSettings.Update(existing);
            await _context.SaveChangesAsync();

            return new NotificationSettingsDto
            {
                UserId = existing.UserId,
                EmailNotifications = existing.EmailNotifications,
                PushNotifications = existing.PushNotifications,
                CourseCommunity = existing.CourseCommunity,
                Promotions = existing.Promotions,
                Newsletters = existing.Newsletters,
                LearningReminders = existing.LearningReminders,
                UpdatedAt = existing.UpdatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving notification settings for user {UserId}", userId);
            throw;
        }
    }

    public async Task<PrivacySettingsDto> GetPrivacySettingsAsync(int userId)
    {
        try
        {
            var settings = await _context.UserPrivacySettings
                .FirstOrDefaultAsync(s => s.UserId == userId);

            if (settings == null)
            {
                // Return defaults if not found
                return new PrivacySettingsDto
                {
                    UserId = userId,
                    ProfileVisible = true,
                    ShowProgressPublic = false,
                    AllowMessages = true,
                    AllowFriends = true,
                    UpdatedAt = DateTime.UtcNow
                };
            }

            return new PrivacySettingsDto
            {
                UserId = settings.UserId,
                ProfileVisible = settings.ProfileVisible,
                ShowProgressPublic = settings.ShowProgressPublic,
                AllowMessages = settings.AllowMessages,
                AllowFriends = settings.AllowFriends,
                UpdatedAt = settings.UpdatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting privacy settings for user {UserId}", userId);
            throw;
        }
    }

    public async Task<PrivacySettingsDto> SavePrivacySettingsAsync(int userId, PrivacySettingsDto settings)
    {
        try
        {
            var existing = await _context.UserPrivacySettings
                .FirstOrDefaultAsync(s => s.UserId == userId);

            if (existing == null)
            {
                var newSettings = new UserPrivacySettings
                {
                    UserId = userId,
                    ProfileVisible = settings.ProfileVisible,
                    ShowProgressPublic = settings.ShowProgressPublic,
                    AllowMessages = settings.AllowMessages,
                    AllowFriends = settings.AllowFriends,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.UserPrivacySettings.Add(newSettings);
                await _context.SaveChangesAsync();

                return new PrivacySettingsDto
                {
                    UserId = newSettings.UserId,
                    ProfileVisible = newSettings.ProfileVisible,
                    ShowProgressPublic = newSettings.ShowProgressPublic,
                    AllowMessages = newSettings.AllowMessages,
                    AllowFriends = newSettings.AllowFriends,
                    UpdatedAt = newSettings.UpdatedAt
                };
            }

            // Update existing
            existing.ProfileVisible = settings.ProfileVisible;
            existing.ShowProgressPublic = settings.ShowProgressPublic;
            existing.AllowMessages = settings.AllowMessages;
            existing.AllowFriends = settings.AllowFriends;
            existing.UpdatedAt = DateTime.UtcNow;

            _context.UserPrivacySettings.Update(existing);
            await _context.SaveChangesAsync();

            return new PrivacySettingsDto
            {
                UserId = existing.UserId,
                ProfileVisible = existing.ProfileVisible,
                ShowProgressPublic = existing.ShowProgressPublic,
                AllowMessages = existing.AllowMessages,
                AllowFriends = existing.AllowFriends,
                UpdatedAt = existing.UpdatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving privacy settings for user {UserId}", userId);
            throw;
        }
    }
}
