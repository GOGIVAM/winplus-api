using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// PromoCode entity - represents a promotional code for discounts
/// </summary>
public class PromoCode
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string Code { get; set; }
    
    [MaxLength(500)]
    public string? Description { get; set; }
    
    [Required]
    [MaxLength(20)]
    public string DiscountType { get; set; } // "Percentage" or "FixedAmount"
    
    [Required]
    [Column(TypeName = "numeric(18,2)")]
    public decimal DiscountValue { get; set; }
    
    [Column(TypeName = "numeric(18,2)")]
    public decimal? MinimumPurchase { get; set; }
    
    [Column(TypeName = "numeric(18,2)")]
    public decimal? MaximumDiscount { get; set; }
    
    public int? UsageLimit { get; set; }
    
    public int UsageCount { get; set; } = 0;
    
    public int? PerUserLimit { get; set; } = 1;
    
    public DateTime ValidFrom { get; set; }
    
    public DateTime? ValidUntil { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    public string? ApplicableSubjectIds { get; set; } // JSON: [1,2,3]

    /// <summary>
    /// Lot 9.7a (suivi 18.7) : périmètre du code, « catalog » (catalogue et
    /// formations, appliqué dès ce lot), « subscription » (abonnements :
    /// enregistré, appliqué au lot 9.7b) ou « all ». Un code « subscription »
    /// est refusé sur une commande du catalogue.
    /// </summary>
    [MaxLength(20)]
    public string AppliesTo { get; set; } = PromoCodeScopes.Catalog;

    public int CreatedBy { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation
    [ForeignKey(nameof(CreatedBy))]
    public User? Creator { get; set; }
    
    public ICollection<PromoCodeUsage> Usages { get; set; } = new List<PromoCodeUsage>();
}

/// <summary>Périmètres d'un code promo (lot 9.7a, suivi 18.7).</summary>
public static class PromoCodeScopes
{
    public const string Catalog = "catalog";
    public const string Subscription = "subscription";
    public const string All = "all";

    public static bool IsValid(string? scope) => scope is Catalog or Subscription or All;

    /// <summary>Le code peut-il réduire une commande du catalogue (contenus, formations) ?</summary>
    public static bool AppliesToCatalog(string? scope) => string.IsNullOrWhiteSpace(scope) || scope == Catalog || scope == All;
}

/// <summary>
/// PromoCodeUsage entity - tracks usage of promo codes
/// </summary>
public class PromoCodeUsage
{
    public int Id { get; set; }
    
    [Required]
    public int PromoCodeId { get; set; }
    
    [Required]
    public int UserId { get; set; }
    
    [Required]
    public int OrderId { get; set; }
    
    [Required]
    [Column(TypeName = "numeric(18,2)")]
    public decimal DiscountAmount { get; set; }
    
    public DateTime UsedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    [ForeignKey(nameof(PromoCodeId))]
    public PromoCode? PromoCode { get; set; }
    
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
    
    [ForeignKey(nameof(OrderId))]
    public Order? Order { get; set; }
}
