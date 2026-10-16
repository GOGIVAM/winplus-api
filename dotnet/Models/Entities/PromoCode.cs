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
    [MaxLength(120)]
    public string AppliesTo { get; set; } = PromoCodeScopes.Catalog;

    // ── Lot 9.7b, étape 3 : modèle de promotion unifié (étude section 5.2) ──
    // Une seule entité pour les remises, les essais gratuits et les tokens
    // offerts, sur tous les achats (catalogue, formations, abonnements,
    // recharges de tokens, licences). L'ancienne entité Promotion est supprimée.

    /// <summary><see cref="PromotionKinds"/> : discount_percent, discount_amount, trial_days, bonus_tokens.</summary>
    [MaxLength(20)]
    public string Kind { get; set; } = PromotionKinds.DiscountPercent;

    /// <summary>Faux : offre automatique (sans code saisi), ex. essai pour tout nouvel élève.</summary>
    public bool RequiresCode { get; set; } = true;

    /// <summary>Plans visés (JSON [ids]) ; null = tous les plans des publics visés.</summary>
    public string? PlanIds { get; set; }

    /// <summary>Publics visés pour un abonnement (student,parent,teacher) ; null = tous.</summary>
    [MaxLength(60)]
    public string? Audiences { get; set; }

    /// <summary>Périodes acceptées (monthly,quarterly,yearly) ; null = toutes.</summary>
    [MaxLength(60)]
    public string? BillingPeriods { get; set; }

    /// <summary><see cref="PromotionDurations"/> (18.20) : first_payment, all_periods, n_periods.</summary>
    [MaxLength(20)]
    public string SubscriptionDuration { get; set; } = PromotionDurations.FirstPayment;

    /// <summary>Nombre de périodes remisées pour n_periods.</summary>
    public int? DurationPeriods { get; set; }

    /// <summary>Durée de l'essai gratuit (trial_days).</summary>
    public int? TrialDays { get; set; }

    /// <summary>Tokens offerts (bonus_tokens).</summary>
    public int? BonusTokens { get; set; }

    /// <summary>Rôles visés (student,parent,teacher,institution) ; null = tous.</summary>
    [MaxLength(80)]
    public string? TargetRoles { get; set; }

    /// <summary><see cref="PromotionEligibility"/> : everyone, new_accounts, never_subscribed, lapsed_subscribers, listed.</summary>
    [MaxLength(30)]
    public string Eligibility { get; set; } = PromotionEligibility.Everyone;

    /// <summary>Ancienneté maximale du compte (jours) pour new_accounts.</summary>
    public int? NewAccountDays { get; set; }

    /// <summary>Comptes visés (JSON [ids]) pour listed.</summary>
    public string? EligibleUserIds { get; set; }

    /// <summary>Établissements visés (JSON [ids]) pour listed (licences).</summary>
    public string? EligibleInstitutionIds { get; set; }

    /// <summary>Cumulable avec une autre offre (non par défaut : une seule offre par paiement).</summary>
    public bool Stackable { get; set; }

    public int CreatedBy { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation
    [ForeignKey(nameof(CreatedBy))]
    public User? Creator { get; set; }
    
    public ICollection<PromoCodeUsage> Usages { get; set; } = new List<PromoCodeUsage>();
}

/// <summary>
/// Cibles d'une offre (lot 9.7a 18.7, étendu au 9.7b section 5.2). La colonne
/// AppliesTo porte une liste séparée par des virgules ; les trois valeurs
/// historiques restent comprises : « catalog » (catalogue et formations),
/// « subscription » (abonnements) et « all ».
/// </summary>
public static class PromoCodeScopes
{
    public const string Catalog = "catalog";
    public const string Courses = "courses";
    public const string Subscriptions = "subscriptions";
    public const string TokenTopUp = "token_topup";
    public const string InstitutionLicense = "institution_license";

    // Valeurs historiques (9.7a).
    public const string Subscription = "subscription";
    public const string All = "all";

    public static readonly string[] Targets = { Catalog, Courses, Subscriptions, TokenTopUp, InstitutionLicense };

    /// <summary>Cibles effectives d'une valeur stockée (historique compris).</summary>
    public static HashSet<string> Expand(string? stored)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(stored)) { set.Add(Catalog); set.Add(Courses); return set; }
        foreach (var raw in stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var v = raw.ToLowerInvariant();
            if (v == All) { foreach (var t in Targets) set.Add(t); }
            else if (v == Subscription) set.Add(Subscriptions);
            else if (v == Catalog) { set.Add(Catalog); set.Add(Courses); }
            else if (Targets.Contains(v)) set.Add(v);
        }
        return set;
    }

    /// <summary>Forme canonique à stocker ; null si une valeur est inconnue.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return Catalog;
        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.ToLowerInvariant()).ToList();
        if (parts.Any(p => !Targets.Contains(p) && p is not (All or Subscription))) return null;
        return string.Join(',', Expand(string.Join(',', parts)).OrderBy(t => Array.IndexOf(Targets, t)));
    }

    public static bool IsValid(string? scope) => Normalize(scope) != null;

    public static bool AppliesTo(string? stored, string target) => Expand(stored).Contains(target);

    /// <summary>Le code peut-il réduire une commande du catalogue (contenus) ?</summary>
    public static bool AppliesToCatalog(string? scope) => AppliesTo(scope, Catalog);
}

/// <summary>Nature d'une offre (étude 9.7b section 5.2).</summary>
public static class PromotionKinds
{
    public const string DiscountPercent = "discount_percent";
    public const string DiscountAmount = "discount_amount";
    public const string TrialDays = "trial_days";
    public const string BonusTokens = "bonus_tokens";
    public static bool IsValid(string? k) => k is DiscountPercent or DiscountAmount or TrialDays or BonusTokens;
}

/// <summary>Durée d'une remise d'abonnement, réglable par code (18.20).</summary>
public static class PromotionDurations
{
    public const string FirstPayment = "first_payment";
    public const string AllPeriods = "all_periods";
    public const string NPeriods = "n_periods";
    public static bool IsValid(string? d) => d is FirstPayment or AllPeriods or NPeriods;
}

/// <summary>Éligibilité d'une offre (étude 9.7b section 5.2).</summary>
public static class PromotionEligibility
{
    public const string Everyone = "everyone";
    public const string NewAccounts = "new_accounts";
    public const string NeverSubscribed = "never_subscribed";
    public const string LapsedSubscribers = "lapsed_subscribers";
    public const string Listed = "listed";
    public static bool IsValid(string? e) => e is Everyone or NewAccounts or NeverSubscribed or LapsedSubscribers or Listed;
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
    
    /// <summary>Commande payée ; nulle pour un essai gratuit ou un octroi sans paiement (lot 9.7b).</summary>
    public int? OrderId { get; set; }

    /// <summary>Lot 9.7b : abonnement auquel l'offre a été appliquée (essai, remise d'abonnement).</summary>
    public int? SubscriptionId { get; set; }

    /// <summary>Lot 9.7b : cible de l'usage (catalog, courses, subscriptions, token_topup, institution_license).</summary>
    [MaxLength(30)]
    public string? Target { get; set; }
    
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
