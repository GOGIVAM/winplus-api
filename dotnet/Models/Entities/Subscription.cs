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

    /// <summary>Indique si l'abonnement est actif.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Nom du plan tarifaire (libre, standard, premium, famille…).</summary>
    public string? PlanName { get; set; }

    /// <summary>Tokens IA consommés ce mois-ci.</summary>
    public int TokensUsedThisMonth { get; set; } = 0;

    /// <summary>Date de la dernière réinitialisation du compteur de tokens.</summary>
    public DateTime? TokensResetAt { get; set; }

    // Navigation properties
    public User? User { get; set; }

    public PricingPlan? PricingPlan { get; set; }
}
