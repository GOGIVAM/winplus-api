namespace Backend.Models.Entities;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// User entity - represents a user profile in the application
/// </summary>
public class User
{
    public int Id { get; set; }
    
    public string? CognitoId { get; set; } // Optional - for Cognito users
    
    public required string Email { get; set; }
    
    public string? PasswordHash { get; set; } // For local authentication
    
    public string? FirstName { get; set; }
    
    public string? LastName { get; set; }

    public string? Phone { get; set; }

    [MaxLength(50)]
    public string Role { get; set; } = "student"; // student, teacher, parent, admin

    public bool IsEmailVerified { get; set; } = false;

    public DateTime? VerifiedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public string? VerificationCode { get; set; } // Code temporaire pour vérifier l'email

    public DateTime? VerificationCodeExpiredAt { get; set; } // Expiration du code
    
    public string? ProfileImageUrl { get; set; }
    
    [MaxLength(500)]
    public string? AvatarUrl { get; set; } // Avatar URL for profile picture
    
    public string? Bio { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; } = false;
    
    public string? DeletedBy { get; set; } // Audit: qui a supprimé
    
    public int? DeletedByUserId { get; set; } // UserId qui a supprimé (for audit trail)
    
    // Email change workflow
    public string? PendingEmail { get; set; } // New email pending verification
    public string? EmailChangeToken { get; set; } // Verification token for email change
    public DateTime? EmailChangeTokenExpiry { get; set; } // Expiry of verification token
    
    // Navigation properties
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
    
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    
    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();
    
    public ICollection<FavoriteCollection> FavoriteCollections { get; set; } = new List<FavoriteCollection>();
    
    public ICollection<LearningHistory> LearningHistories { get; set; } = new List<LearningHistory>();
    
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    
    public ICollection<AnalyticsEvent> AnalyticsEvents { get; set; } = new List<AnalyticsEvent>();
    
    public ICollection<PromoCode> CreatedPromoCodes { get; set; } = new List<PromoCode>();
    
    public ICollection<PromoCodeUsage> PromoCodeUsages { get; set; } = new List<PromoCodeUsage>();
    
    public ICollection<Certificate> Certificates { get; set; } = new List<Certificate>();

    // Learning Resources Navigation properties
    public ICollection<QuizAttempt> QuizAttempts { get; set; } = new List<QuizAttempt>();
    
    public ICollection<RevisionEnrollment> RevisionEnrollments { get; set; } = new List<RevisionEnrollment>();

    // Authentication & Security Navigation properties
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    
    public ICollection<DeviceInfo> DeviceInfos { get; set; } = new List<DeviceInfo>();
    
    public ICollection<EmailVerificationToken> EmailVerificationTokens { get; set; } = new List<EmailVerificationToken>();
    
    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
    
    public TwoFactorToken? TwoFactorToken { get; set; }
    
    public ICollection<OAuthAccount> OAuthAccounts { get; set; } = new List<OAuthAccount>();
    
    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();
    
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}
