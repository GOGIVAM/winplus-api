namespace Backend.Models.Entities;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// User privacy preferences stored in database
/// </summary>
[Table("UserPrivacySettings")]
public class UserPrivacySettings
{
    [Key]
    public int Id { get; set; }

    [ForeignKey("User")]
    public int UserId { get; set; }

    public bool ProfileVisible { get; set; } = true;

    public bool ShowProgressPublic { get; set; } = false;

    public bool AllowMessages { get; set; } = true;

    public bool AllowFriends { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public User? User { get; set; }
}
