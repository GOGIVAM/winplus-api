namespace Backend.Models.Entities;

/// <summary>
/// Notification entity - represents a user notification
/// </summary>
public class Notification
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    
    public required string Title { get; set; }
    
    public required string Message { get; set; }
    
    public string Type { get; set; } = "General"; // General, Course, Order, System
    
    public string? RelatedEntityType { get; set; }
    
    public int? RelatedEntityId { get; set; }
    
    public bool IsRead { get; set; } = false;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? ReadAt { get; set; }
    
    // Navigation properties
    public required User User { get; set; }
}
