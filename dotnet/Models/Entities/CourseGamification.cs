using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Réglages de gamification par formation (Module 5, 5C). Une ligne par
/// Course, créée à la demande (comportement par défaut si absente : désactivée).
/// </summary>
public class CourseGamificationSettings
{
    public int Id { get; set; }
    public int CourseId { get; set; }

    public bool GamificationEnabled { get; set; } = false;
    public int PointsPerLesson { get; set; } = 10;
    public int PointsPerQuiz { get; set; } = 20;
    public int PointsBonusPerfectScore { get; set; } = 15;
    public bool LeaderboardVisible { get; set; } = false;

    [ForeignKey(nameof(CourseId))]
    public Course? Course { get; set; }
}

/// <summary>Points cumulés d'un élève sur une formation ("ElèveProgression.points" du référentiel).</summary>
public class StudentCoursePoints
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public int UserId { get; set; }
    public int Points { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Badge débloqué par un élève sur une formation.</summary>
public class StudentCourseBadge
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public int UserId { get; set; }

    /// <summary>first_quiz | halfway | perfect_score | completed.</summary>
    [MaxLength(30)]
    public string BadgeType { get; set; } = null!;

    public DateTime EarnedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Certificat de complétion pour une formation (Course), distinct de
/// Certificate.cs (lié à l'ancien modèle Subject/Enrollment). Le code de
/// vérification est ici une vraie colonne mappée — voir CertificateService.cs
/// où le champ équivalent est [NotMapped] et donc jamais persisté (bug connu,
/// non corrigé ici pour ne pas toucher au flux "épreuves" existant).
/// </summary>
public class CourseCertificate
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public int UserId { get; set; }

    [Required, MaxLength(40)]
    public string VerificationCode { get; set; } = null!;

    public decimal? Grade { get; set; }

    [MaxLength(500)]
    public string? FileUrl { get; set; }

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(CourseId))]
    public Course? Course { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
