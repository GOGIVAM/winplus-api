namespace Backend.Models.Entities;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Device Information for tracking and Remember Me functionality
/// </summary>
public class DeviceInfo
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    [MaxLength(500)]
    public string UserAgent { get; set; } = string.Empty;

    [Required]
    [MaxLength(45)]
    public string IpAddress { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? DeviceFingerprint { get; set; }

    [MaxLength(100)]
    public string? DeviceName { get; set; }

    [MaxLength(100)]
    public string? BrowserName { get; set; }

    [MaxLength(50)]
    public string? BrowserVersion { get; set; }

    [MaxLength(100)]
    public string? OsName { get; set; }

    [MaxLength(50)]
    public string? OsVersion { get; set; }

    /// <summary>
    /// If set, device is remembered until this date
    /// If null, device must verify email on every login
    /// </summary>
    public DateTime? RememberUntil { get; set; }

    public bool IsRemembered => RememberUntil != null && DateTime.UtcNow < RememberUntil;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAt { get; set; }

    [ForeignKey("UserId")]
    public virtual User User { get; set; } = null!;
}
