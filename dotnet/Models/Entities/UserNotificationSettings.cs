namespace Backend.Models.Entities;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// User notification preferences stored in database
/// </summary>
[Table("UserNotificationSettings")]
public class UserNotificationSettings
{
    [Key]
    public int Id { get; set; }

    [ForeignKey("User")]
    public int UserId { get; set; }

    public bool EmailNotifications { get; set; } = true;

    public bool PushNotifications { get; set; } = true;

    public bool CourseCommunity { get; set; } = true;

    public bool Promotions { get; set; } = false;

    public bool Newsletters { get; set; } = true;

    public bool LearningReminders { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public User? User { get; set; }
}
