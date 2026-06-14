using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

public class Payment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [ForeignKey("Order")]
    public int OrderId { get; set; }

    [ForeignKey("User")]
    public int? UserId { get; set; }

    [MaxLength(255)]
    public string? GuestEmail { get; set; }

    [Required]
    [Column(TypeName = "decimal(10,2)")]
    public decimal Amount { get; set; }

    [Required]
    [MaxLength(3)]
    public string Currency { get; set; } = "XAF";

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "pending"; // pending, completed, failed, cancelled, expired

    [MaxLength(50)]
    public string? PaymentMethod { get; set; } // notchpay

    [MaxLength(255)]
    public string? TransactionId { get; set; }

    [MaxLength(255)]
    public string? NotchpayReference { get; set; }

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    [MaxLength(20)]
    public string? Operator { get; set; } // mtn, orange, wave

    [MaxLength(500)]
    public string? Description { get; set; }

    public decimal? FeeAmount { get; set; }

    public DateTime InitiatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    [MaxLength(500)]
    public string? ErrorMessage { get; set; }

    [MaxLength(100)]
    public string? ErrorCode { get; set; }

    public int? RetryCount { get; set; } = 0;

    public DateTime? NextRetryAt { get; set; }

    [MaxLength(500)]
    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Order? Order { get; set; }
    public virtual User? User { get; set; }
}
