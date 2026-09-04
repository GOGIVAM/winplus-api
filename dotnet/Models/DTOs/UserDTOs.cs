using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

public class UpdateProfileRequest
{
    [MaxLength(100)] public string? FirstName { get; set; }
    [MaxLength(100)] public string? LastName { get; set; }
    [MaxLength(20)] public string? Phone { get; set; }
    [MaxLength(1000)] public string? Bio { get; set; }
    [MaxLength(100)] public string? Level { get; set; }
    [MaxLength(100)] public string? City { get; set; }
    [MaxLength(50)]  public string? LearningStyle { get; set; }
}

public class ProfileResponse
{
    public int Id { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? Bio { get; set; }
    public string? Level { get; set; }
    public string? City { get; set; }
    public string? AvatarUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string? Role { get; set; }
    public bool IsEmailVerified { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ProfileStatisticsResponse
{
    public int TotalCoursesEnrolled { get; set; }
    public int CompletedCourses { get; set; }
    public double AverageScore { get; set; }
    public int TotalTimeSeconds { get; set; }
    public int QuizCompleted { get; set; }

    // Cartes KPI du tableau de bord élève (Dashboard.jsx / Student.tsx).
    // Absentes jusqu'ici : le frontend les lisait déjà, mais l'API ne les
    // renvoyait pas, d'où des cartes vides malgré une activité réelle.
    public int? ScoreDelta { get; set; }
    public int TotalDownloads { get; set; }
    public int WeeklyDownloads { get; set; }
    public List<int> WeeklyDownloadTrend { get; set; } = new();
    public string StudyTimeFormatted { get; set; } = "0 min";
    public string? StudyGoal { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public List<int> StreakTrend { get; set; } = new();
    public List<double> WeeklyStudyHours { get; set; } = new();
    public List<double> MonthlyScores { get; set; } = new();
}

public class ProfileSubscriptionDto
{
    public int Id { get; set; }
    public string? PlanName { get; set; }
    public decimal Price { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Status { get; set; }
    public int RenewalCount { get; set; }
}

/// <summary>
/// DTO for requesting email change
/// </summary>
public class ChangeEmailRequest
{
    [Required]
    [EmailAddress]
    public string NewEmail { get; set; }
    
    [Required]
    [StringLength(100, MinimumLength = 6)]
    public string Password { get; set; } // Password confirmation for security
}

/// <summary>
/// DTO for confirming email change with verification code
/// </summary>
public class ConfirmEmailChangeRequest
{
    [Required]
    [StringLength(10, MinimumLength = 6)]
    public string VerificationCode { get; set; }
}
