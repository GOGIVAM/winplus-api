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

    /// <summary>
    /// ⚠ <b>Réinterprétée par la Partie 8 du suivi</b> : cette colonne porte
    /// désormais le <b>quota mensuel WinAI en tokens LLM réels</b>, et non plus
    /// un nombre de messages. Le modèle « 1 message = 1 unité » ne reflétait
    /// pas le coût réel (un message avec pièce jointe ou audio coûte bien plus
    /// qu'un message texte court) — décision 8.1.
    ///
    /// La colonne n'a délibérément pas été renommée : elle est lue par des
    /// sauvegardes, des scripts d'exploitation et l'administration existants.
    /// Utiliser <see cref="MonthlyAiTokenQuota"/> dans le code neuf.
    ///
    /// Valeurs posées par <c>Migrations/SQL_SeedPricingPlanTokenQuotas.sql</c>
    /// et répliquées en repli dans <c>Services/AiUsagePolicy.cs</c>.
    ///
    /// ⚠ <b>Depuis 8.10</b>, cette valeur n'est plus un plafond mensuel
    /// appliqué : c'est le quota de RÉFÉRENCE du plan, dont sont dérivées la
    /// limite hebdomadaire (÷ 4) et la limite de session (÷ 3 de la semaine),
    /// voir <c>AiUsagePolicy</c>.
    /// </summary>
    public int? MaxChatMessages { get; set; }

    /// <summary>
    /// Nom juste de <see cref="MaxChatMessages"/> depuis la Partie 8 : quota
    /// mensuel WinAI en tokens. Non mappé — même colonne, pas de redondance
    /// en base.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int? MonthlyAiTokenQuota
    {
        get => MaxChatMessages;
        set => MaxChatMessages = value;
    }

    /// <summary>
    /// Dotation de crédits mensuels en XAF pour les plans parents
    /// (Basique 10 000 / Complet 20 000 / Famille 40 000).
    /// Configurée en base par l'administrateur : aucune valeur codée côté API.
    /// </summary>
    public int? MonthlyCredits { get; set; }

    /// <summary>
    /// Part des revenus reversée à l'enseignant (0.70 / 0.75 / 0.80).
    /// </summary>
    public decimal? TeacherRevenueShare { get; set; }

    /// <summary>
    /// Nombre d'enfants suivis autorisés (plans parents).
    /// </summary>
    public int? MaxChildren { get; set; }

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
