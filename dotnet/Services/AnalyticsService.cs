using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Repositories;

namespace Backend.Services;

/// <summary>
/// Interface pour le service analytics
/// </summary>
public interface IAnalyticsService
{
    Task<AnalyticsEventResponse> TrackEventAsync(int? userId, TrackEventRequest request);
    Task<SessionStatsResponse> GetSessionStatsAsync(int userId);
    Task<UserAnalyticsResponse> GetUserAnalyticsAsync(int userId);
    Task<List<AnalyticsEventResponse>> GetRecentEventsAsync(int limit = 20);
    Task<Dictionary<string, int>> GetEventTypeBreakdownAsync();
    Task<DashboardAnalyticsResponse> GetDashboardAnalyticsAsync();
}

/// <summary>
/// Service pour la gestion des analytics
/// </summary>
public class AnalyticsService : IAnalyticsService
{
    private readonly IAnalyticsRepository _repository;
    private readonly ILogger<AnalyticsService> _logger;

    public AnalyticsService(IAnalyticsRepository repository, ILogger<AnalyticsService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Enregistre un événement analytics
    /// </summary>
    public async Task<AnalyticsEventResponse> TrackEventAsync(int? userId, TrackEventRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.EventName))
                throw new ArgumentException("EventName is required");

            // EventType est optionnel - défaut à EventName si non fourni
            var eventType = !string.IsNullOrWhiteSpace(request.EventType) ? request.EventType : request.EventName;

            var analyticsEvent = new AnalyticsEvent
            {
                UserId = userId,
                EventType = eventType,
                EventName = request.EventName,
                EventCategory = request.EventCategory,
                IpAddress = request.IpAddress,
                EventData = null // EventData expected as Dictionary, request.EventData is string
            };

            var created = await _repository.CreateAsync(analyticsEvent);
            _logger.LogInformation("Event tracked for user {UserId}: {EventName}", userId, request.EventName);

            return MapToResponse(created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracking event for user: {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Récupère les statistiques de session d'un utilisateur
    /// </summary>
    public async Task<SessionStatsResponse> GetSessionStatsAsync(int userId)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            var todayEvents = await _repository.GetByUserAndDateRangeAsync(userId, today, today.AddDays(1));

            var eventCounts = todayEvents
                .GroupBy(e => e.EventType)
                .ToDictionary(g => g.Key, g => g.Count());

            var totalDuration = todayEvents.Count() * 5; // Assume 5 min per event

            return new SessionStatsResponse
            {
                UserId = userId,
                TotalEvents = todayEvents.Count,
                EventTypes = eventCounts,
                SessionStartTime = today,
                SessionEndTime = DateTime.UtcNow,
                TotalDurationMinutes = totalDuration,
                IsActive = todayEvents.Any(e => e.CreatedAt > DateTime.UtcNow.AddHours(-1))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session stats for user: {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Récupère les analytics complets d'un utilisateur
    /// </summary>
    public async Task<UserAnalyticsResponse> GetUserAnalyticsAsync(int userId)
    {
        try
        {
            var allEvents = await _repository.GetByUserIdAsync(userId, 1, int.MaxValue);
            var last7Days = await _repository.GetByUserAndDateRangeAsync(
                userId, 
                DateTime.UtcNow.AddDays(-7), 
                DateTime.UtcNow
            );

            var eventTypeBreakdown = last7Days
                .GroupBy(e => e.EventType)
                .ToDictionary(g => g.Key, g => g.Count());

            var averageEventsPerDay = last7Days.Count / 7;

            return new UserAnalyticsResponse
            {
                UserId = userId,
                TotalEvents = allEvents.Count,
                TotalEventLast7Days = last7Days.Count,
                AverageEventsPerDay = averageEventsPerDay,
                EventTypeBreakdown = eventTypeBreakdown,
                FirstEventDate = allEvents.LastOrDefault()?.CreatedAt,
                LastEventDate = allEvents.FirstOrDefault()?.CreatedAt,
                MostCommonEventType = eventTypeBreakdown.OrderByDescending(x => x.Value).FirstOrDefault().Key
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user analytics for user: {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Récupère les événements récents
    /// </summary>
    public async Task<List<AnalyticsEventResponse>> GetRecentEventsAsync(int limit = 20)
    {
        try
        {
            var events = await _repository.GetRecentEventsAsync(limit);
            return events.Select(MapToResponse).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent events");
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
            return await _repository.GetEventTypeBreakdownAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting event type breakdown");
            throw;
        }
    }

    /// <summary>
    /// Récupère les analytics du dashboard
    /// </summary>
    public async Task<DashboardAnalyticsResponse> GetDashboardAnalyticsAsync()
    {
        try
        {
            var totalEvents = await _repository.GetTotalEventsAsync();
            var breakdown = await _repository.GetEventTypeBreakdownAsync();
            var recentEvents = await _repository.GetRecentEventsAsync(10);

            // Calcul des dernières 24h
            var last24h = await _repository.GetByDateRangeAsync(
                DateTime.UtcNow.AddDays(-1),
                DateTime.UtcNow,
                1,
                int.MaxValue
            );

            return new DashboardAnalyticsResponse
            {
                TotalEvents = totalEvents,
                Events24h = last24h.Count,
                EventTypeBreakdown = breakdown,
                RecentEvents = recentEvents.Select(MapToResponse).ToList(),
                TopEventTypes = breakdown.OrderByDescending(x => x.Value).Take(5).ToDictionary(x => x.Key, x => x.Value)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard analytics");
            throw;
        }
    }

    private AnalyticsEventResponse MapToResponse(AnalyticsEvent analyticsEvent)
    {
        return new AnalyticsEventResponse
        {
            Id = analyticsEvent.Id,
            UserId = analyticsEvent.UserId,
            EventType = analyticsEvent.EventType,
            EventName = analyticsEvent.EventName,
            EventCategory = analyticsEvent.EventCategory,
            IpAddress = analyticsEvent.IpAddress,
            CreatedAt = analyticsEvent.CreatedAt
        };
    }
}
