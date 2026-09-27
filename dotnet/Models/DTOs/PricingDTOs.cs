namespace Backend.Models.DTOs;

public class PricingPlanResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "XAF";
    public string? Period { get; set; }
    public string? BillingPeriod { get; set; }
    public string[] Features { get; set; } = Array.Empty<string>();
    public bool IsPopular { get; set; }
    public string? Icon { get; set; }
    public string? Description { get; set; }
    public int? MaxDownloads { get; set; }
    /// <summary>
    /// Obsolète depuis la Partie 8 (la colonne porte des tokens) : toujours
    /// null. Utiliser <see cref="AiUsageMultiplier"/>.
    /// </summary>
    public int? MaxChatMessages { get; set; }

    /// <summary>Usage WinAI relatif au plan gratuit (1 = gratuit). Partie 8.3.</summary>
    public int AiUsageMultiplier { get; set; } = 1;
}

public class PromotionResponse
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
}

public class PricingCategoryGroup
{
    public string Category { get; set; } = string.Empty;
    public List<PricingPlanResponse> Plans { get; set; } = new();
}
