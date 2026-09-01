namespace Backend.Models.Entities;

public class Course
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? PreviewVideoUrl { get; set; }
    public string Language { get; set; } = "fr";
    public string Level { get; set; } = "debutant";
    public string? Category { get; set; }
    public List<string> Tags { get; set; } = new();
    public decimal Price { get; set; }
    public bool IsFree { get; set; }
    public bool IsIncludedInSub { get; set; }
    public string Status { get; set; } = "draft";
    public int InstructorId { get; set; }
    public User Instructor { get; set; } = null!;
    public int? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
    public int TotalDurationMin { get; set; }
    public int LessonsCount { get; set; }
    public int EnrolledCount { get; set; }
    public decimal? AvgRating { get; set; }
    public int ReviewsCount { get; set; }
    public List<string> Requirements { get; set; } = new();
    public List<string> Objectives { get; set; } = new();
    public bool CertificateEnabled { get; set; } = true;

    /// <summary>
    /// Date de publication, renseignee par AdminCourseController.Approve.
    ///
    /// La propriete etait utilisee dans le controleur (lignes 71 et 152) mais
    /// absente de l'entite : erreur de compilation CS1061. Nullable, car une
    /// formation en brouillon ou rejetee n'a pas de date de publication.
    ///
    /// La colonne SQL correspondante est ajoutee par
    /// sql/003_add_courses_publishedat.sql.
    /// </summary>
    public DateTime? PublishedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CourseSection> Sections { get; set; } = new List<CourseSection>();
    public ICollection<CourseEnrollment> CourseEnrollments { get; set; } = new List<CourseEnrollment>();
    public ICollection<CourseReview> CourseReviews { get; set; } = new List<CourseReview>();
}

public class CourseSection
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Position { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CourseLesson> Lessons { get; set; } = new List<CourseLesson>();
}

public class CourseLesson
{
    public int Id { get; set; }
    public int SectionId { get; set; }
    public CourseSection Section { get; set; } = null!;
    public int CourseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string LessonType { get; set; } = "video";
    public string? VideoUrl { get; set; }
    public int? VideoDurationSec { get; set; }
    public string? ArticleContent { get; set; }
    public string? FileUrl { get; set; }
    public string? FileName { get; set; }
    public int? QuizId { get; set; }
    public int Position { get; set; }
    public bool IsPreview { get; set; }
    public bool IsPublished { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CourseEnrollment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
    public string AccessType { get; set; } = "purchase";
    public int? OrderId { get; set; }
    public decimal ProgressPercent { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CertificateUrl { get; set; }
    public bool IsActive { get; set; } = true;
}

public class LessonProgress
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int LessonId { get; set; }
    public int CourseId { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int WatchTimeSec { get; set; }
    public int LastPositionSec { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CourseReview
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public short Rating { get; set; }
    public string? Comment { get; set; }
    public bool IsVerified { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
