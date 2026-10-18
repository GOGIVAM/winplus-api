namespace Backend.Models.Entities;

/// <summary>
/// Lot 10.3 : source d'acquisition captée par le web à l'arrivée du visiteur
/// (identifiants de clic Google Ads, paramètres utm, référent), avec l'état du
/// consentement au moment de la capture. Type possédé (colonnes préfixées) :
/// <c>Orders.Attribution_*</c>, <c>Users.FirstTouch_*</c> et
/// <c>Users.LastTouch_*</c>, posées par Migrations/SQL_AddMarketingAttribution.sql.
///
/// Aucune donnée personnelle : ni e-mail, ni nom, ni téléphone. Les
/// identifiants de clic ne sont conservés qu'avec le consentement publicité,
/// le reste qu'avec le consentement mesure ou publicité (voir
/// <see cref="Backend.Services.Marketing.AttributionRules"/>).
/// </summary>
public class MarketingAttribution
{
    public string? Gclid { get; set; }
    public string? Gbraid { get; set; }
    public string? Wbraid { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmTerm { get; set; }
    public string? UtmContent { get; set; }
    /// <summary>Domaine du site référent (jamais l'URL complète, qui peut porter des données).</summary>
    public string? Referrer { get; set; }
    /// <summary>Chemin de la page d'arrivée, sans paramètres.</summary>
    public string? LandingPage { get; set; }
    /// <summary>Identifiant client GA4 (cookie _ga), requis par le Measurement Protocol.</summary>
    public string? GaClientId { get; set; }
    public string? GaSessionId { get; set; }
    public bool ConsentAnalytics { get; set; }
    public bool ConsentAds { get; set; }
    public DateTime? CapturedAt { get; set; }

    public MarketingAttribution Clone() => (MarketingAttribution)MemberwiseClone();
}

/// <summary>
/// Lot 10.3 : registre des conversions (un achat confirmé = une ligne, clé
/// <see cref="TransactionId"/> unique). Source unique de l'envoi serveur vers
/// GA4 et de l'export CSV des conversions hors ligne Google Ads.
/// </summary>
public class ConversionEvent
{
    public int Id { get; set; }
    /// <summary>Numéro de commande ou référence NotchPay : identique au transaction_id du web.</summary>
    public string TransactionId { get; set; } = string.Empty;
    public string EventName { get; set; } = "purchase";
    /// <summary>epreuve, formation, abonnement, repetiteur ou mixte.</summary>
    public string Category { get; set; } = "mixte";
    /// <summary>order ou tutor_booking.</summary>
    public string SourceType { get; set; } = "order";
    public int SourceId { get; set; }
    public int? UserId { get; set; }
    /// <summary>Montant TTC en XAF.</summary>
    public decimal ValueXaf { get; set; }
    public string Currency { get; set; } = "XAF";
    public DateTime OccurredAt { get; set; }
    public string? Gclid { get; set; }
    public string? Gbraid { get; set; }
    public string? Wbraid { get; set; }
    public string? GaClientId { get; set; }
    public string? GaSessionId { get; set; }
    public bool ConsentAnalytics { get; set; }
    public bool ConsentAds { get; set; }
    /// <summary>Articles au format GA4 (item_id, item_name, item_category, price, quantity), en JSON.</summary>
    public string? ItemsJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ConversionDelivery> Deliveries { get; set; } = new List<ConversionDelivery>();
}

/// <summary>
/// Envoi d'une conversion vers une destination (ga4 aujourd'hui, meta plus
/// tard) : file persistée, reprise sur erreur, un seul envoi par couple
/// (conversion, destination).
/// </summary>
public class ConversionDelivery
{
    public int Id { get; set; }
    public int ConversionEventId { get; set; }
    public string Destination { get; set; } = string.Empty;
    /// <summary>pending, sent ou failed (abandon après le nombre maximal de tentatives ou refus définitif).</summary>
    public string Status { get; set; } = ConversionDeliveryStatus.Pending;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public string? LastError { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ConversionEvent? ConversionEvent { get; set; }
}

public static class ConversionDeliveryStatus
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Failed = "failed";
}
