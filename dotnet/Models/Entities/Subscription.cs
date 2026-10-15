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

    // ── Lot 9.7b (étude section 1.2) ───────────────────────────────────────

    /// <summary>Version du plan payée (prix et droits). Nulle pour un abonnement antérieur au 9.7b.</summary>
    public int? PlanVersionId { get; set; }

    /// <summary><see cref="BillingPeriods"/> : monthly, quarterly ou yearly.</summary>
    public string? BillingPeriod { get; set; }

    /// <summary>Public du plan (student, parent, teacher) : un seul abonnement actif par compte et par public.</summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Fin de la période de grâce (18.15 : renouvellement par le solde avec
    /// 3 jours de grâce). Un abonnement « past_due » reste en vigueur jusqu'à
    /// cette date.
    /// </summary>
    public DateTime? GraceUntil { get; set; }

    public PlanVersion? PlanVersion { get; set; }

    // Navigation properties
    public User? User { get; set; }

    public PricingPlan? PricingPlan { get; set; }
}
