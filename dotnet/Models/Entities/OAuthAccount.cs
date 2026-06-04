namespace Backend.Models.Entities;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// OAuth Account linking for social login (Google, Facebook, etc.)
/// </summary>
public class OAuthAccount
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Provider { get; set; } = string.Empty; // "google", "facebook", "github"

    [Required]
    [MaxLength(255)]
    public string ProviderUserId { get; set; } = string.Empty;

    public string? Email { get; set; }

    [MaxLength(255)]
    public string? DisplayName { get; set; }

    [MaxLength(500)]
    public string? ProfileImageUrl { get; set; }

    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DisconnectedAt { get; set; }

    public bool IsActive { get; set; } = true;

    [ForeignKey("UserId")]
    public virtual User User { get; set; } = null!;
}
