namespace Backend.Models.Entities;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Backup Code entity for 2FA recovery
/// </summary>
public class BackupCode
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int TwoFactorTokenId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Code { get; set; } = string.Empty;

    public bool IsUsed { get; set; } = false;

    public DateTime? UsedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("TwoFactorTokenId")]
    public virtual TwoFactorToken TwoFactorToken { get; set; } = null!;
}
