using System;
using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

/// <summary>
/// DTO pour créer un paiement
/// </summary>
public class CreatePaymentRequest
{
    [Required]
    public int OrderId { get; set; }

    [Required]
    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "EUR";

    [MaxLength(100)]
    public string? PaymentMethod { get; set; } // credit_card, paypal, stripe

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Données additionnelles (JSON string)
    /// </summary>
    [MaxLength(500)]
    public string? Metadata { get; set; }
}

/// <summary>
/// DTO pour confirmer un paiement
/// </summary>
public class ConfirmPaymentRequest
{
    [MaxLength(255)]
    public string? TransactionId { get; set; }

    [MaxLength(500)]
    public string? ConfirmationData { get; set; }
}

/// <summary>
/// DTO pour demander un remboursement
/// </summary>
public class RefundPaymentRequest
{
    [Range(0.01, double.MaxValue)]
    public decimal? Amount { get; set; } // null = full refund

    [MaxLength(500)]
    public string? Reason { get; set; }
}

/// <summary>
/// DTO pour réessayer un paiement
/// </summary>
public class RetryPaymentRequest
{
    [MaxLength(100)]
    public string? PaymentMethod { get; set; }
}

/// <summary>
/// DTO de réponse pour un paiement
/// </summary>
public class PaymentResponse
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int UserId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Status { get; set; } = "";
    public string? PaymentMethod { get; set; }
    public string? TransactionId { get; set; }
    public string? Description { get; set; }
    public decimal? FeeAmount { get; set; }
    public DateTime InitiatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int? RetryCount { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// DTO pour lister les paiements
/// </summary>
public class PaymentListResponse
{
    public List<PaymentResponse> Payments { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int Limit { get; set; }
}
