namespace Backend.Models.Entities;

/// <summary>
/// Enrollment entity - represents a user's enrollment in a course
/// </summary>
public class Enrollment
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    
    public int SubjectId { get; set; }
    
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? CompletedAt { get; set; }
    
    public decimal ProgressPercentage { get; set; } = 0;
    
    public bool IsCompleted { get; set; } = false;
    
    public string? CertificateUrl { get; set; }
    
    // Navigation properties
    public User? User { get; set; }
    
    public Subject? Subject { get; set; }
    
    public Certificate? Certificate { get; set; }
}
