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
    public string Code { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>"Percentage" ou "FixedAmount" (forme historique) ; déduit de Promotion.Kind sinon.</summary>
    public string? DiscountType { get; set; }

    /// <summary>Pourcentage ou montant de la remise ; 0 pour un essai ou des tokens offerts.</summary>
    [Range(0, double.MaxValue)]
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

    /// <summary>Lot 9.7b : cibles séparées par des virgules (catalog, courses, subscriptions, token_topup, institution_license).</summary>
    [MaxLength(120)]
    public string? AppliesTo { get; set; }

    public PromotionFields? Promotion { get; set; }
}

/// <summary>
/// Lot 9.7b (étude section 5.2) : champs du modèle de promotion unifié,
/// communs à la création et à la modification d'une offre.
/// </summary>
public class PromotionFields
{
    public string? Kind { get; set; }
    public bool? RequiresCode { get; set; }
    public List<int>? PlanIds { get; set; }
    public string? Audiences { get; set; }
    public string? BillingPeriods { get; set; }
    public string? SubscriptionDuration { get; set; }
    public int? DurationPeriods { get; set; }
    public int? TrialDays { get; set; }
    public int? BonusTokens { get; set; }
    public string? TargetRoles { get; set; }
    public string? Eligibility { get; set; }
    public int? NewAccountDays { get; set; }
    public List<int>? EligibleUserIds { get; set; }
    public List<int>? EligibleInstitutionIds { get; set; }
    public bool? Stackable { get; set; }
}

/// <summary>
/// DTO for validating a promo code at checkout
/// </summary>
public class ValidatePromoCodeRequest
{
    [Required]
    public string Code { get; set; } = string.Empty;

    [Required]
    [Range(0.01, double.MaxValue)]
    public decimal CartTotal { get; set; }

    public List<int>? SubjectIds { get; set; }

    /// <summary>
    /// Lot 9.7b (F9) : formations du panier et total TTC de leurs lignes. La
    /// remise d'un code « courses » porte sur ce total ; celle d'un code
    /// « catalog » sur <see cref="CatalogTotal"/> (à défaut, CartTotal moins
    /// le total des formations). Supportée par la plateforme seule.
    /// </summary>
    public List<int>? CourseIds { get; set; }
    public decimal? CoursesTotal { get; set; }
    public decimal? CatalogTotal { get; set; }
}

/// <summary>
/// DTO for returning promo code data
/// </summary>
public class PromoCodeDto
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string DiscountType { get; set; } = string.Empty;

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

    /// <summary>Cibles (catalog, courses, subscriptions, token_topup, institution_license).</summary>
    public string AppliesTo { get; set; } = "catalog";

    public PromotionFields Promotion { get; set; } = new();

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// DTO for updating an existing promo code (admin only)
/// </summary>
public class UpdatePromoCodeRequest
{
    [MaxLength(500)]
    public string? Description { get; set; }

    public string? DiscountType { get; set; }

    [Range(0, double.MaxValue)]
    public decimal DiscountValue { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? MinimumPurchase { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? MaximumDiscount { get; set; }

    [Range(1, int.MaxValue)]
    public int? UsageLimit { get; set; }

    public DateTime? ValidUntil { get; set; }

    public List<int>? ApplicableSubjectIds { get; set; }

    /// <summary>Cibles séparées par des virgules ; null = inchangé.</summary>
    [MaxLength(120)]
    public string? AppliesTo { get; set; }

    public PromotionFields? Promotion { get; set; }

    public int? PerUserLimit { get; set; }
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
