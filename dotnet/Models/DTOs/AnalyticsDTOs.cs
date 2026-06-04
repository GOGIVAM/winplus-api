using System;
using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

/// <summary>
/// DTO pour tracker un événement analytics
/// </summary>
public class TrackEventRequest
{
    [MaxLength(100)]
    public string? EventName { get; set; } // signup_attempt, login_success, etc.

    [MaxLength(100)]
    public string? EventType { get; set; } // page_view, button_click, purchase, etc.

    [MaxLength(255)]
    public string? EventCategory { get; set; }

    [MaxLength(45)]
    public string? IpAddress { get; set; }

    // Le frontend envoie un objet JSON, on le reçoit comme object
    public object? EventData { get; set; } // JSON data (peut être un objet ou un string)

    // Champs supplémentaires envoyés par le frontend
    public long? Timestamp { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    [MaxLength(500)]
    public string? Url { get; set; }
}

/// <summary>
/// DTO de réponse pour un événement analytics
/// </summary>
public class AnalyticsEventResponse
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string EventType { get; set; } = "";
    public string EventName { get; set; } = "";
    public string? EventCategory { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// DTO pour les statistiques de session
/// </summary>
public class SessionStatsResponse
{
    public int UserId { get; set; }
    public int TotalEvents { get; set; }
    public Dictionary<string, int> EventTypes { get; set; } = new();
    public DateTime SessionStartTime { get; set; }
    public DateTime SessionEndTime { get; set; }
    public int TotalDurationMinutes { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// DTO pour les analytics utilisateur
/// </summary>
public class UserAnalyticsResponse
{
    public int UserId { get; set; }
    public int TotalEvents { get; set; }
    public int TotalEventLast7Days { get; set; }
    public int AverageEventsPerDay { get; set; }
    public Dictionary<string, int> EventTypeBreakdown { get; set; } = new();
    public DateTime? FirstEventDate { get; set; }
    public DateTime? LastEventDate { get; set; }
    public string? MostCommonEventType { get; set; }
}

/// <summary>
/// DTO pour les analytics du dashboard admin
/// </summary>
public class DashboardAnalyticsResponse
{
    public int TotalEvents { get; set; }
    public int Events24h { get; set; }
    public Dictionary<string, int> EventTypeBreakdown { get; set; } = new();
    public List<AnalyticsEventResponse> RecentEvents { get; set; } = new();
    public Dictionary<string, int> TopEventTypes { get; set; } = new();
}
