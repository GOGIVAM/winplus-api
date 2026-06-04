namespace Backend.Models.Entities;

/// <summary>
/// Plan de tarification pour les abonnements
/// </summary>
public class PricingPlan
{
    public int Id { get; set; }
    
    /// <summary>
    /// Nom du plan (Premium, Standard, etc.)
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Catégorie du plan (students, teachers, parents)
    /// </summary>
    public string Category { get; set; } = string.Empty;
    
    /// <summary>
    /// Prix du plan
    /// </summary>
    public decimal Price { get; set; }
    
    /// <summary>
    /// Période de facturation (/mois, /trimestre, /an)
    /// </summary>
    public string? Period { get; set; }
    
    /// <summary>
    /// Fonctionnalités incluses (JSON array)
    /// </summary>
    public string? Features { get; set; }
    
    /// <summary>
    /// Indicateur si le plan est populaire
    /// </summary>
    public bool IsPopular { get; set; } = false;

    /// <summary>
    /// Indicateur si le plan est archivé (non mappé en DB)
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsArchived { get; set; } = false;

    public string Currency { get; set; } = "XAF";
    public string? BillingPeriod { get; set; }
    public int? MaxDownloads { get; set; }
    public int? MaxChatMessages { get; set; }

    /// <summary>
    /// Icône du plan
    /// </summary>
    public string? Icon { get; set; }
    
    /// <summary>
    /// Description du plan
    /// </summary>
    public string? Description { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
}
