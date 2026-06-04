namespace Backend.Models.Entities;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Two-Factor Authentication settings for user
/// </summary>
[Table("UserTwoFactorAuthentication")]
public class UserTwoFactorAuthentication
{
    [Key]
    public int Id { get; set; }

    [ForeignKey("User")]
    public int UserId { get; set; }

    public bool IsEnabled { get; set; } = false;

    [MaxLength(50)]
    public string? Method { get; set; } // "email", "sms", "authenticator"

    public string? TotpSecret { get; set; } // Secret for authenticator app

    public DateTime? EnabledAt { get; set; }

    public DateTime? LastVerifiedAt { get; set; }

    [MaxLength(500)]
    public string? BackupCodes { get; set; } // JSON array of comma-separated codes

    public int BackupCodesUsed { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public User? User { get; set; }
}
