using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

/// <summary>
/// DTO for creating a new promo code (admin only)
/// </summary>
public class CreatePromoCodeRequest
{
    [Required]
    [MaxLength(50)]
    [RegularExpression(@"^[A-Z0-9_-]+$", ErrorMessage = "Code must contain only uppercase letters, numbers, hyphens and underscores")]
    public string Code { get; set; }
    
    [MaxLength(500)]
    public string? Description { get; set; }
    
    [Required]
    public string DiscountType { get; set; } // "Percentage" or "FixedAmount"
    
    [Required]
    [Range(0.01, double.MaxValue)]
    public decimal DiscountValue { get; set; }
    
    [Range(0, double.MaxValue)]
    public decimal? MinimumPurchase { get; set; }
    
    [Range(0, double.MaxValue)]
    public decimal? MaximumDiscount { get; set; }
    
    [Range(1, int.MaxValue)]
    public int? UsageLimit { get; set; }
    
    [Range(1, 100)]
    public int? PerUserLimit { get; set; }
    
    [Required]
    public DateTime ValidFrom { get; set; }
    
    public DateTime? ValidUntil { get; set; }
    
    public List<int>? ApplicableSubjectIds { get; set; }
}

/// <summary>
/// DTO for validating a promo code at checkout
/// </summary>
public class ValidatePromoCodeRequest
{
    [Required]
    public string Code { get; set; }
    
    [Required]
    [Range(0.01, double.MaxValue)]
    public decimal CartTotal { get; set; }
    
    public List<int>? SubjectIds { get; set; }
}

/// <summary>
/// DTO for returning promo code data
/// </summary>
public class PromoCodeDto
{
    public int Id { get; set; }
    
    public string Code { get; set; }
    
    public string? Description { get; set; }
    
    public string DiscountType { get; set; }
    
    public decimal DiscountValue { get; set; }
    
    public decimal? MinimumPurchase { get; set; }
    
    public decimal? MaximumDiscount { get; set; }
    
    public int? UsageLimit { get; set; }
    
    public int UsageCount { get; set; }
    
    public int? PerUserLimit { get; set; }
    
    public DateTime ValidFrom { get; set; }
    
    public DateTime? ValidUntil { get; set; }
    
    public bool IsActive { get; set; }
    
    public List<int>? ApplicableSubjectIds { get; set; }
    
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// DTO for promo code validation result
/// </summary>
public class PromoCodeValidationResult
{
    public bool IsValid { get; set; }
    
    public string? ErrorMessage { get; set; }
    
    public decimal DiscountAmount { get; set; }
    
    public decimal FinalAmount { get; set; }
    
    public PromoCodeDto? PromoCode { get; set; }
}
