namespace Backend.Models.Entities;
using System.Text.Json.Serialization;

/// <summary>
/// Subject entity - represents a course or educational subject
/// </summary>
public class Subject
{
    public int Id { get; set; }
    
    public required string Title { get; set; }
    
    public string? Description { get; set; }
    
    public string? Category { get; set; }
    
    public string? ThumbnailUrl { get; set; }
    
    public decimal Price { get; set; }
    
    public bool IsPublished { get; set; } = false;
    
    public int EnrollmentCount { get; set; } = 0;
    
    public bool IsFeatured { get; set; } = false;
    
    public decimal AverageRating { get; set; } = 0;
    
    public int TotalRatings { get; set; } = 0;

    public int? DownloadCount { get; set; } = 0;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; } = false;
    
    // Navigation properties
    public ICollection<CourseContent> Contents { get; set; } = new List<CourseContent>();
    
    // ✅ Mark as [JsonIgnore] to prevent circular reference in JSON serialization
    [JsonIgnore]
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    
    [JsonIgnore]
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
    
    [JsonIgnore]
    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();
    
    [JsonIgnore]
    public ICollection<LearningHistory> LearningHistories { get; set; } = new List<LearningHistory>();
    
    [JsonIgnore]
    public ICollection<Certificate> Certificates { get; set; } = new List<Certificate>();
    
    [JsonIgnore]
    public ICollection<Exam> Exams { get; set; } = new List<Exam>();
    
    [JsonIgnore]
    public ICollection<Quiz> Quizzes { get; set; } = new List<Quiz>();
    
    [JsonIgnore]
    public ICollection<Revision> Revisions { get; set; } = new List<Revision>();}