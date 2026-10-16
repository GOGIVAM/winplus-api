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

    // ── Lot 9.7b, étape 3 : cycle d'abonnement (18.15, 18.20) ──────────────

    /// <summary>Montant TTC réellement payé pour la période en cours (après remise et prorata).</summary>
    public decimal? PricePaidTtc { get; set; }

    /// <summary>Dernière commande ayant payé une période (idempotence de l'activation).</summary>
    public int? OrderId { get; set; }

    /// <summary>Début de la période en cours (StartDate reste la date de souscription).</summary>
    public DateTime? CurrentPeriodStart { get; set; }

    /// <summary>Renouvellement automatique par le solde du portefeuille.</summary>
    public bool AutoRenew { get; set; }

    /// <summary>Résilié : accès jusqu'à EndDate, sans remboursement partiel, puis expiration.</summary>
    public bool CancelAtPeriodEnd { get; set; }
    public DateTime? CancelledAt { get; set; }

    /// <summary>Descente de plan ou changement de période programmé à l'échéance.</summary>
    public int? PendingPricingPlanId { get; set; }
    public string? PendingBillingPeriod { get; set; }

    /// <summary>Code promo dont la remise s'applique encore aux renouvellements.</summary>
    public int? PromoCodeId { get; set; }

    /// <summary>Périodes encore remisées (null = toutes, 0 = plus aucune).</summary>
    public int? RemainingDiscountedPeriods { get; set; }

    /// <summary>Fin de l'essai gratuit, si l'abonnement a commencé par un essai (un seul par compte et par public).</summary>
    public DateTime? TrialEndsAt { get; set; }

    /// <summary>Dernière tentative de renouvellement par le solde (échec compris).</summary>
    public DateTime? LastRenewalAttemptAt { get; set; }

    public PlanVersion? PlanVersion { get; set; }

    // Navigation properties
    public User? User { get; set; }

    public PricingPlan? PricingPlan { get; set; }
}
