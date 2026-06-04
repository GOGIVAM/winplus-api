namespace Backend.Models.Entities;

/// <summary>
/// AnalyticsEvent entity - tracks user behavior and platform analytics
/// </summary>
public class AnalyticsEvent
{
    public int Id { get; set; }
    
    public int? UserId { get; set; }
    
    public required string EventType { get; set; }
    
    public required string EventName { get; set; }
    
    public string? EventCategory { get; set; }
    
    public Dictionary<string, object>? EventData { get; set; }
    
    public string? IpAddress { get; set; }
    
    public string? UserAgent { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public User? User { get; set; }
}
