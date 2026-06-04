namespace Backend.Models.Entities;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Active user sessions for device management
/// </summary>
[Table("UserSessions")]
public class UserSession
{
    [Key]
    public int Id { get; set; }

    [ForeignKey("User")]
    public int UserId { get; set; }

    [MaxLength(255)]
    public string? DeviceName { get; set; }

    [MaxLength(100)]
    public string? DeviceType { get; set; } // "Windows", "iOS", "Android", "Mac", "Linux"

    [MaxLength(50)]
    public string? IpAddress { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    [MaxLength(100)]
    public string? Location { get; set; } // "City, Country"

    public string? RefreshTokenId { get; set; } // Reference to RefreshToken

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation property
    public User? User { get; set; }
}
