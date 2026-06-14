using System;
using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

public class InitiatePaymentRequest
{
    [Required]
    public int OrderId { get; set; }

    [Required]
    [MaxLength(20)]
    public string Phone { get; set; } = string.Empty;

    [Required]
    [Range(100, double.MaxValue, ErrorMessage = "Le montant minimum est 100 XAF")]
    public decimal Amount { get; set; }

    [MaxLength(255)]
    public string? Email { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }
}

public class InitiatePaymentResponse
{
    public int PaymentId { get; set; }
    public string? NotchpayReference { get; set; }
    public string Status { get; set; } = "pending";
    public string? AuthorizationUrl { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "XAF";
    public string? Message { get; set; }
}

public class NotchPayWebhookPayload
{
    public string? Event { get; set; }
    public NotchPayWebhookTransaction? Transaction { get; set; }
}

public class NotchPayWebhookTransaction
{
    public string? Reference { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Operator { get; set; }
    public string? Phone { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
}

public class PaymentStatusResponse
{
    public int Id { get; set; }
    public string? NotchpayReference { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "XAF";
    public string? Operator { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime InitiatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
}

public class PaymentHistoryResponse
{
    public List<PaymentStatusResponse> Payments { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int Limit { get; set; }
}

// Legacy DTOs kept for backward compatibility with PaymentService
public class CreatePaymentRequest
{
    [Required]
    public int OrderId { get; set; }

    [Required]
    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "XAF";

    [MaxLength(100)]
    public string? PaymentMethod { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? Metadata { get; set; }
}

public class ConfirmPaymentRequest
{
    [MaxLength(255)]
    public string? TransactionId { get; set; }

    [MaxLength(500)]
    public string? ConfirmationData { get; set; }
}

public class RefundPaymentRequest
{
    [Range(0.01, double.MaxValue)]
    public decimal? Amount { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }
}

public class RetryPaymentRequest
{
    [MaxLength(100)]
    public string? PaymentMethod { get; set; }
}

public class PaymentResponse
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int? UserId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "XAF";
    public string Status { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public string? TransactionId { get; set; }
    public string? NotchpayReference { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Operator { get; set; }
    public string? Description { get; set; }
    public decimal? FeeAmount { get; set; }
    public DateTime InitiatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public int? RetryCount { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class PaymentListResponse
{
    public List<PaymentResponse> Payments { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int Limit { get; set; }
}
