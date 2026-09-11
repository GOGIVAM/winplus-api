namespace Backend.Models.Entities;

/// <summary>
/// Compte d'affiliation d'un enseignant/tuteur (seuls rôles autorisés à en
/// créer un, voir AffiliateService.GetOrCreateMyAccountAsync). Un lien
/// "?ref=Code" partagé par l'affilié rapporte une commission sur N'IMPORTE
/// QUEL achat effectué sur la plateforme dans la fenêtre d'attribution
/// (AffiliateSettings.AttributionWindowDays), pas seulement sur son propre
/// contenu — décision produit du 2026-09-11.
/// </summary>
public class AffiliateAccount
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Code court unique utilisé dans le lien de parrainage (?ref=CODE).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Taux de commission courant (%), recalculé périodiquement par
    /// AffiliateRateRecalculationService à partir des métriques réelles de
    /// l'affilié (WinAI si disponible, heuristique locale en repli) — voir
    /// AffiliateService.ComputeHeuristicRate. Ne peut jamais dépasser
    /// AffiliateSettings.CommissionRateCapPercent.
    /// </summary>
    public decimal CommissionRate { get; set; }

    /// <summary>"active" ou "suspended" (fraude, abus — décision admin).</summary>
    public string Status { get; set; } = "active";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastRateUpdateAt { get; set; }

    public ICollection<AffiliateClick> Clicks { get; set; } = new List<AffiliateClick>();
    public ICollection<AffiliateCommission> Commissions { get; set; } = new List<AffiliateCommission>();
}

/// <summary>
/// Un clic sur un lien de parrainage. Sert à la fois à l'attribution (via
/// VisitorToken, capturé côté frontend et renvoyé à la commande) et de
/// signal de qualité pour le calcul du taux de commission (taux de
/// conversion clics → achats).
/// </summary>
public class AffiliateClick
{
    public int Id { get; set; }
    public int AffiliateAccountId { get; set; }
    public AffiliateAccount AffiliateAccount { get; set; } = null!;

    /// <summary>Identifiant anonyme stable côté navigateur (localStorage), pas un compte.</summary>
    public string VisitorToken { get; set; } = string.Empty;

    public string? LandingPath { get; set; }
    public DateTime ClickedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Commission générée par une commande attribuée à un affilié.
/// "pending" à la création (le temps du délai de rétractation/remboursement
/// commande, AffiliateSettings.HoldPeriodDays), puis "confirmed" par
/// AffiliateCommissionMaturityService (incluse dans le solde retirable du
/// professeur, voir TeacherService.GetSpendableBalanceAsync), ou "reversed"
/// si la commande a été remboursée/annulée entre-temps.
/// </summary>
public class AffiliateCommission
{
    public int Id { get; set; }
    public int AffiliateAccountId { get; set; }
    public AffiliateAccount AffiliateAccount { get; set; } = null!;

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    /// <summary>Nullable : une commande invité (guest) n'a pas de UserId.</summary>
    public int? BuyerUserId { get; set; }

    public decimal OrderAmount { get; set; }
    public decimal CommissionRateApplied { get; set; }
    public decimal CommissionAmount { get; set; }

    /// <summary>"pending" | "confirmed" | "paid" | "reversed".</summary>
    public string Status { get; set; } = "pending";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }
}

/// <summary>
/// Réglages globaux du programme d'affiliation — ligne singleton (Id = 1).
/// </summary>
public class AffiliateSettings
{
    public int Id { get; set; } = 1;

    /// <summary>Plafond (%) que WinAI (ou l'heuristique de repli) ne peut jamais dépasser en fixant le taux d'un affilié.</summary>
    public decimal CommissionRateCapPercent { get; set; } = 15m;

    /// <summary>Durée de validité d'un clic de parrainage avant qu'un achat ne lui soit plus attribué.</summary>
    public int AttributionWindowDays { get; set; } = 30;

    /// <summary>Délai avant qu'une commission "pending" ne devienne "confirmed" (mêmes garde-fous qu'un remboursement de commande).</summary>
    public int HoldPeriodDays { get; set; } = 14;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
