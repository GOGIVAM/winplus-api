using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Lot 9.7b (étude 9.7b section 1.2, décisions 18.10, 18.12, 18.19, 18.21) :
/// une <b>version</b> d'un plan. La famille (<see cref="PricingPlan"/>) reste
/// stable (clé, public, nom) ; la version porte les prix par période et les
/// droits. C'est ce qui permet d'appliquer une hausse de prix ou une baisse de
/// droits au prochain renouvellement, avec préavis, sans toucher aux abonnés en
/// cours (18.12) : un abonnement pointe sur la version qu'il a payée.
///
/// Cycle : <c>draft</c> (modifiable) → <c>published</c> (une seule par famille,
/// c'est celle qui se vend) → <c>archived</c> (remplacée ; reste lisible pour
/// les abonnés qui y sont encore rattachés).
/// </summary>
public class PlanVersion
{
    public int Id { get; set; }

    public int PricingPlanId { get; set; }

    /// <summary>Numéro croissant dans la famille (1, 2, 3…).</summary>
    public int VersionNumber { get; set; }

    /// <summary><see cref="PlanVersionStatus"/> : draft, published ou archived (VARCHAR(20)).</summary>
    [MaxLength(20)]
    public string Status { get; set; } = PlanVersionStatus.Draft;

    /// <summary>Note interne de l'administrateur (motif de la version).</summary>
    [MaxLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }

    public DateTime? PublishedAt { get; set; }
    public int? PublishedByUserId { get; set; }

    /// <summary>
    /// Date à partir de laquelle les abonnés d'une version antérieure basculent
    /// sur celle-ci, à leur prochain renouvellement. Égale à la publication pour
    /// une amélioration (bascule immédiate, 18.12), publication + préavis pour
    /// une hausse de prix ou une baisse de droits.
    /// </summary>
    public DateTime? ExistingSubscribersFrom { get; set; }

    /// <summary>Vrai si la publication n'a été qu'une amélioration (prix égal ou plus bas, droits égaux ou meilleurs).</summary>
    public bool IsImprovement { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public PricingPlan? PricingPlan { get; set; }
    public ICollection<PlanPrice> Prices { get; set; } = new List<PlanPrice>();
    public ICollection<PlanEntitlement> Entitlements { get; set; } = new List<PlanEntitlement>();
}

public static class PlanVersionStatus
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";
}

/// <summary>Prix TTC d'une version pour une période (18.19 : saisi par l'admin, le prix saisi est le prix payé).</summary>
public class PlanPrice
{
    public int Id { get; set; }
    public int PlanVersionId { get; set; }

    /// <summary><see cref="BillingPeriods"/> : monthly, quarterly ou yearly (VARCHAR(20)).</summary>
    [MaxLength(20)]
    public string Period { get; set; } = BillingPeriods.Monthly;

    [Column(TypeName = "numeric(12,0)")]
    public decimal PriceTtc { get; set; }

    public PlanVersion? PlanVersion { get; set; }
}

/// <summary>
/// Valeur d'un droit pour une version (clé typée du catalogue
/// <c>Services/Entitlements/EntitlementCatalog.cs</c>). La valeur est stockée
/// sous forme canonique texte (« true », « 800000 », « -1 » pour illimité,
/// « 75 » pour un pourcentage) et validée à l'écriture contre le catalogue.
/// </summary>
public class PlanEntitlement
{
    public int Id { get; set; }
    public int PlanVersionId { get; set; }

    [MaxLength(80)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Value { get; set; } = string.Empty;

    public PlanVersion? PlanVersion { get; set; }
}

/// <summary>Périodes de facturation d'un abonnement, et leur durée en mois.</summary>
public static class BillingPeriods
{
    public const string Monthly = "monthly";
    public const string Quarterly = "quarterly";
    public const string Yearly = "yearly";

    public static readonly string[] All = { Monthly, Quarterly, Yearly };

    public static bool IsValid(string? period) => period is Monthly or Quarterly or Yearly;

    public static int Months(string? period) => period switch
    {
        Quarterly => 3,
        Yearly => 12,
        _ => 1,
    };

    public static string Label(string? period) => period switch
    {
        Quarterly => "trimestre",
        Yearly => "an",
        _ => "mois",
    };
}

/// <summary>
/// Publics des plans (un plan par public, 18.15). La colonne historique
/// <c>PricingPlans.Category</c> porte « students », « parents », « teachers » ;
/// la colonne <c>Audience</c> du 9.7b porte la forme canonique ci-dessous.
/// </summary>
public static class PlanAudiences
{
    public const string Student = "student";
    public const string Parent = "parent";
    public const string Teacher = "teacher";

    public static readonly string[] All = { Student, Parent, Teacher };

    public static bool IsValid(string? audience) => audience is Student or Parent or Teacher;

    /// <summary>Public d'un compte selon son rôle. L'institution n'a pas de plan (licences, 18.11).</summary>
    public static string? ForRole(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        "student" => Student,
        "parent" => Parent,
        "teacher" => Teacher,
        _ => null,
    };

    public static string LegacyCategory(string audience) => audience + "s";

    public static string Label(string? audience) => audience switch
    {
        Parent => "Parent",
        Teacher => "Professeur",
        _ => "Élève",
    };
}
