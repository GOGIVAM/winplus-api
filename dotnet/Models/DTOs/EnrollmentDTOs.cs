using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

/// <summary>
/// DTO for enrollment progress response
/// </summary>
public class EnrollmentProgressDto
{
    public int EnrollmentId { get; set; }
    
    public int UserId { get; set; }
    
    public int SubjectId { get; set; }
    
    public string SubjectTitle { get; set; }
    
    public decimal ProgressPercentage { get; set; }
    
    public bool IsCompleted { get; set; }
    
    public DateTime EnrolledAt { get; set; }
    
    public DateTime? CompletedAt { get; set; }
    
    public int TotalContents { get; set; }
    
    public int CompletedContents { get; set; }
    
    public DateTime? LastAccessedAt { get; set; }
}
