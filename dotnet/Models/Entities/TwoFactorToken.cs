namespace Backend.Models.Entities;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Two-Factor Authentication Token entity
/// Supports both TOTP and backup codes
/// </summary>
public class TwoFactorToken
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [MaxLength(255)]
    public string? TotpSecret { get; set; }

    public bool IsTotpEnabled { get; set; } = false;

    public DateTime? TotpEnabledAt { get; set; }

    public int BackupCodesCount { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    [ForeignKey("UserId")]
    public virtual User User { get; set; } = null!;

    public virtual ICollection<BackupCode> BackupCodes { get; set; } = new List<BackupCode>();
}
