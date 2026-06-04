namespace Backend.Models.Entities;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Email Verification Token entity for email verification flow
/// </summary>
public class EmailVerificationToken
{
    // ✅ Constante statique au lieu de propriété (évite la désynchronisation avec la DB)
    public const int MAX_ATTEMPTS = 55;

    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    [MaxLength(6)]
    public string VerificationCode { get; set; } = string.Empty;

    [Required]
    public DateTime ExpiresAt { get; set; }

    public bool IsVerified { get; set; } = false;

    public DateTime? VerifiedAt { get; set; }

    public int AttemptCount { get; set; } = 0;

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    public bool IsBlocked => AttemptCount >= MAX_ATTEMPTS;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("UserId")]
    public virtual User User { get; set; } = null!;
}
