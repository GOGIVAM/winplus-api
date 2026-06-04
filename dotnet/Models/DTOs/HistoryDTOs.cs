using System;
using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

/// <summary>
/// DTO pour ajouter un événement à l'historique
/// </summary>
public class AddHistoryRequest
{
    [Required]
    [MaxLength(100)]
    public string EventType { get; set; } = ""; // course_started, course_completed, lesson_viewed, test_taken, etc.

    public int SubjectId { get; set; }

    [MaxLength(500)]
    public string? EventTitle { get; set; }

    [MaxLength(1000)]
    public string? EventDescription { get; set; }

    [Range(0, 100)]
    public decimal? Score { get; set; }

    [Range(0, int.MaxValue)]
    public int? DurationSeconds { get; set; }

    [Range(0, 100)]
    public decimal? ProgressPercentage { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    [MaxLength(2000)]
    public string? EventDetails { get; set; }

    public bool IsCompleted { get; set; } = false;
}

/// <summary>
/// DTO de réponse pour un événement d'historique
/// </summary>
public class HistoryResponse
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? SubjectId { get; set; }
    public string EventType { get; set; } = "";
    public string? EventTitle { get; set; }
    public string? EventDescription { get; set; }
    public decimal? Score { get; set; }
    public int? DurationSeconds { get; set; }
    public decimal? ProgressPercentage { get; set; }
    public string? Notes { get; set; }
    public string? EventDetails { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Related data
    public string? SubjectTitle { get; set; }
}

/// <summary>
/// DTO pour lister l'historique
/// </summary>
public class HistoryListResponse
{
    public List<HistoryResponse> History { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int Limit { get; set; }
    
    /// <summary>
    /// Statistiques agrégées
    /// </summary>
    public HistoryStatistics? Statistics { get; set; }
}

/// <summary>
/// Statistiques d'apprentissage
/// </summary>
public class HistoryStatistics
{
    public int TotalEvents { get; set; }
    public int CoursesStarted { get; set; }
    public int CoursesCompleted { get; set; }
    public int TotalTimeSpentMinutes { get; set; }
    public float AverageScore { get; set; }
    public DateTime FirstActivityAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public Dictionary<string, int> EventTypeBreakdown { get; set; } = new();
}

/// <summary>
/// Filtre pour l'historique
/// </summary>
public class HistoryFilterRequest
{
    public string? EventType { get; set; }
    public int? SubjectId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int Page { get; set; } = 1;
    public int Limit { get; set; } = 50;
}
