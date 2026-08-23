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

    // La colonne SQL est INTEGER et RefreshToken.Id est un int : déclarer ce
    // champ en string faisait envoyer un paramètre texte à PostgreSQL, qui
    // rejetait tout INSERT (42804) et faisait donc échouer chaque connexion.
    public int? RefreshTokenId { get; set; } // Reference to RefreshToken.Id

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation property
    public User? User { get; set; }
}
