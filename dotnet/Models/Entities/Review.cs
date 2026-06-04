using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Review entity - represents a user review/rating for a subject/course
/// </summary>
public class Review
{
    public int Id { get; set; }
    
    [Required]
    public int UserId { get; set; }
    
    [Required]
    public int SubjectId { get; set; }
    
    [Required]
    [Range(1, 5)]
    public int Rating { get; set; } // 1-5 stars
    
    [MaxLength(200)]
    public string? Title { get; set; }
    
    [MaxLength(2000)]
    public string? Comment { get; set; }
    
    public bool IsVerifiedPurchase { get; set; } = false;
    
    public int HelpfulCount { get; set; } = 0;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }
    
    public bool IsDeleted { get; set; } = false;
    
    // Navigation properties
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
    
    [ForeignKey(nameof(SubjectId))]
    public Subject? Subject { get; set; }
}
