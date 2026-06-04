using Backend.Models.Entities;
using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

/// <summary>
/// Interface pour le repository d'analytics
/// </summary>
    public interface IAnalyticsRepository
    {
        Task<List<AnalyticsEvent>> GetByUserIdAsync(int userId, int page = 1, int pageSize = 50);
        Task<List<AnalyticsEvent>> GetByEventTypeAsync(string eventType, int page = 1, int pageSize = 50);
        Task<List<AnalyticsEvent>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, int page = 1, int pageSize = 50);
        Task<int> GetEventCountByTypeAsync(string eventType);
        Task<int> GetTotalEventsAsync();
        Task<List<AnalyticsEvent>> GetRecentEventsAsync(int limit = 20);
        Task<AnalyticsEvent> CreateAsync(AnalyticsEvent analyticsEvent);
        Task<bool> DeleteAsync(int id);
        Task<Dictionary<string, int>> GetEventTypeBreakdownAsync();
        Task<List<AnalyticsEvent>> GetByUserAndDateRangeAsync(int userId, DateTime startDate, DateTime endDate);
    }

    /// <summary>
    /// Repository pour gérer les événements analytics
    /// </summary>
    public class AnalyticsRepository : IAnalyticsRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AnalyticsRepository> _logger;

        public AnalyticsRepository(ApplicationDbContext context, ILogger<AnalyticsRepository> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Récupère les événements d'un utilisateur
        /// </summary>
        public async Task<List<AnalyticsEvent>> GetByUserIdAsync(int userId, int page = 1, int pageSize = 50)
        {
            try
            {
                return await _context.AnalyticsEvents
                    .Where(e => e.UserId == userId)
                    .AsNoTracking()
                    .OrderByDescending(e => e.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting analytics events for user: {UserId}", userId);
                throw;
            }
        }

        /// <summary>
        /// Récupère les événements par type
        /// </summary>
        public async Task<List<AnalyticsEvent>> GetByEventTypeAsync(string eventType, int page = 1, int pageSize = 50)
        {
            try
            {
                return await _context.AnalyticsEvents
                    .Where(e => e.EventType == eventType)
                    .AsNoTracking()
                    .OrderByDescending(e => e.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting analytics events by type: {EventType}", eventType);
                throw;
            }
        }

        /// <summary>
        /// Récupère les événements par plage de dates
        /// </summary>
        public async Task<List<AnalyticsEvent>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, int page = 1, int pageSize = 50)
        {
            try
            {
                return await _context.AnalyticsEvents
                    .Where(e => e.CreatedAt >= startDate && e.CreatedAt <= endDate)
                    .OrderByDescending(e => e.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting analytics events by date range");
                throw;
            }
        }

        /// <summary>
        /// Compte les événements d'un type spécifique
        /// </summary>
        public async Task<int> GetEventCountByTypeAsync(string eventType)
        {
            try
            {
                return await _context.AnalyticsEvents
                    .Where(e => e.EventType == eventType)
                    .CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error counting analytics events by type: {EventType}", eventType);
                throw;
            }
        }

        /// <summary>
        /// Récupère le nombre total d'événements
        /// </summary>
        public async Task<int> GetTotalEventsAsync()
        {
            try
            {
                return await _context.AnalyticsEvents.CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting total event count");
                throw;
            }
        }

        /// <summary>
        /// Récupère les événements récents
        /// </summary>
        public async Task<List<AnalyticsEvent>> GetRecentEventsAsync(int limit = 20)
        {
            try
            {
                return await _context.AnalyticsEvents
                    .OrderByDescending(e => e.CreatedAt)
                    .Take(limit)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent analytics events");
                throw;
            }
        }

        /// <summary>
        /// Crée un nouvel événement analytics
        /// </summary>
        public async Task<AnalyticsEvent> CreateAsync(AnalyticsEvent analyticsEvent)
        {
            try
            {
                analyticsEvent.CreatedAt = DateTime.UtcNow;
                _context.AnalyticsEvents.Add(analyticsEvent);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Analytics event created: {EventName}", analyticsEvent.EventName);
                return analyticsEvent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating analytics event");
                throw;
            }
        }

        /// <summary>
        /// Supprime un événement
        /// </summary>
        public async Task<bool> DeleteAsync(int id)
        {
            try
            {
                var analyticsEvent = await _context.AnalyticsEvents.FindAsync(id);
                if (analyticsEvent == null)
                {
                    _logger.LogWarning("Analytics event not found for deletion: {Id}", id);
                    return false;
                }

                _context.AnalyticsEvents.Remove(analyticsEvent);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Analytics event deleted: {Id}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting analytics event: {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Récupère le breakdown des types d'événements
        /// </summary>
        public async Task<Dictionary<string, int>> GetEventTypeBreakdownAsync()
        {
            try
            {
                return await _context.AnalyticsEvents
                    .GroupBy(e => e.EventType)
                    .Select(g => new { Type = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.Type, x => x.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting event type breakdown");
                throw;
            }
        }

        /// <summary>
        /// Récupère les événements d'un utilisateur dans une plage de dates
        /// </summary>
        public async Task<List<AnalyticsEvent>> GetByUserAndDateRangeAsync(int userId, DateTime startDate, DateTime endDate)
        {
            try
            {
                return await _context.AnalyticsEvents
                    .Where(e => e.UserId == userId && e.CreatedAt >= startDate && e.CreatedAt <= endDate)
                    .OrderByDescending(e => e.CreatedAt)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting analytics events by user and date range");
                throw;
            }
        }
    }
