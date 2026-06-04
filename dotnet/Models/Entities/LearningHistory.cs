namespace Backend.Models.Entities;

/// <summary>
/// LearningHistory entity - tracks user's learning activity and progress
/// </summary>
public class LearningHistory
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    
    public int SubjectId { get; set; }
    
    public int? ContentId { get; set; }
    
    // Event tracking properties
    public string EventType { get; set; } = "Viewed"; // Viewed, Completed, Quizzed, Assessment, etc.
    
    public string? EventTitle { get; set; }
    
    public string? EventDescription { get; set; }
    
    // Legacy/compatibility properties
    public string ActivityType { get; set; } = "Viewed"; // Viewed, Completed, Quizzed, etc.
    
    public int? TimeSpentSeconds { get; set; }
    
    public int? DurationSeconds { get; set; } // Alternative to TimeSpentSeconds
    
    public decimal? QuizScore { get; set; }
    
    public decimal? Score { get; set; } // Alternative to QuizScore
    
    public decimal? ProgressPercentage { get; set; }
    
    public bool IsCompleted { get; set; } = false;
    
    public string? Notes { get; set; }
    
    public string? EventDetails { get; set; } // JSON string for additional data
    
    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime ActivityAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public User? User { get; set; }
    
    public Subject? Subject { get; set; }
    
    public CourseContent? Content { get; set; }
}
