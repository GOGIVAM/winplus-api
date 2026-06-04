using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

/// <summary>
/// DTO for creating a new review
/// </summary>
public class CreateReviewRequest
{
    [Required]
    [Range(1, 5)]
    public int Rating { get; set; }
    
    [MaxLength(200)]
    public string? Title { get; set; }
    
    [MaxLength(2000)]
    public string? Comment { get; set; }
}

/// <summary>
/// DTO for updating an existing review
/// </summary>
public class UpdateReviewRequest
{
    [Range(1, 5)]
    public int? Rating { get; set; }
    
    [MaxLength(200)]
    public string? Title { get; set; }
    
    [MaxLength(2000)]
    public string? Comment { get; set; }
}

/// <summary>
/// DTO for returning review data
/// </summary>
public class ReviewDto
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    
    public string? UserName { get; set; }
    
    public string? UserAvatar { get; set; }
    
    public int SubjectId { get; set; }
    
    public int Rating { get; set; }
    
    public string? Title { get; set; }
    
    public string? Comment { get; set; }
    
    public bool IsVerifiedPurchase { get; set; }
    
    public int HelpfulCount { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// DTO for subject rating summary
/// </summary>
public class SubjectRatingSummary
{
    public int SubjectId { get; set; }
    
    public double AverageRating { get; set; }
    
    public int TotalReviews { get; set; }
    
    public Dictionary<int, int> RatingDistribution { get; set; } = new();
}
