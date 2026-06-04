namespace Backend.Models.Entities;

/// <summary>
/// Abonnement utilisateur aux plans de tarification
/// </summary>
public class Subscription
{
    public int Id { get; set; }
    
    /// <summary>
    /// Id de l'utilisateur
    /// </summary>
    public int UserId { get; set; }
    
    /// <summary>
    /// Id du plan de tarification
    /// </summary>
    public int PricingPlanId { get; set; }
    
    /// <summary>
    /// Date de début de l'abonnement
    /// </summary>
    public DateTime StartDate { get; set; }
    
    /// <summary>
    /// Date de fin (si résilié)
    /// </summary>
    public DateTime? EndDate { get; set; }
    
    /// <summary>
    /// État de l'abonnement (active, expired, cancelled)
    /// </summary>
    public string Status { get; set; } = "active";
    
    public int RenewalCount { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;

    // Navigation properties
    public User? User { get; set; }
    
    public PricingPlan? PricingPlan { get; set; }
}
