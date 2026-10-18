using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Backend.Models.Entities;

namespace Backend.Services.Marketing;

/// <summary>
/// Lot 10.3 : configuration de la mesure serveur. Lue dans les variables
/// d'environnement <c>GA4_MEASUREMENT_ID</c> et <c>GA4_API_SECRET</c> (ou
/// <c>Ga4:MeasurementId</c> / <c>Ga4:ApiSecret</c> dans appsettings). Sans
/// ces deux valeurs, aucune conversion n'est envoyée à GA4 et rien n'est
/// journalisé.
/// </summary>
public sealed class MarketingConfig
{
    public string? Ga4MeasurementId { get; init; }
    public string? Ga4ApiSecret { get; init; }

    public bool Ga4Configured =>
        !string.IsNullOrWhiteSpace(Ga4MeasurementId) && Ga4MeasurementId.StartsWith("G-", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(Ga4ApiSecret);

    public static MarketingConfig From(IConfiguration configuration) => new()
    {
        Ga4MeasurementId = (configuration["GA4_MEASUREMENT_ID"] ?? configuration["Ga4:MeasurementId"])?.Trim(),
        Ga4ApiSecret = (configuration["GA4_API_SECRET"] ?? configuration["Ga4:ApiSecret"])?.Trim(),
    };
}

/// <summary>Issue d'un envoi : réussi, à retenter plus tard, ou refusé définitivement.</summary>
public enum ConversionSendOutcome { Sent, Retry, Rejected }

public sealed record ConversionSendResult(ConversionSendOutcome Outcome, string? Error = null)
{
    public static readonly ConversionSendResult Ok = new(ConversionSendOutcome.Sent);
}

/// <summary>
/// Destination de conversions serveur (décision Q17 c) : GA4 aujourd'hui ;
/// une API Conversions Meta s'ajoutera par une seconde implémentation, sans
/// toucher au registre ni à la file.
/// </summary>
public interface IConversionDestination
{
    /// <summary>Nom court, stocké dans ConversionDeliveries.Destination (ga4, meta…).</summary>
    string Name { get; }

    /// <summary>Faux tant que les identifiants de la destination ne sont pas configurés.</summary>
    bool IsConfigured { get; }

    /// <summary>Vrai si la conversion peut partir vers cette destination (consentement, identifiants requis).</summary>
    bool Accepts(ConversionEvent conversion);

    Task<ConversionSendResult> SendAsync(ConversionEvent conversion, CancellationToken ct);
}

/// <summary>
/// GA4 Measurement Protocol : événement <c>purchase</c> avec le même
/// <c>transaction_id</c> que le web (numéro de commande ou référence de
/// paiement), ce qui permet à GA4 de dédoublonner l'achat de secours envoyé
/// par le navigateur. N'envoie qu'avec le consentement mesure et
/// l'identifiant client GA4 capté par le web.
/// </summary>
public sealed class Ga4MeasurementProtocolDestination : IConversionDestination
{
    public const string HttpClientName = "Ga4MeasurementProtocol";
    public const string Endpoint = "https://www.google-analytics.com/mp/collect";

    /// <summary>GA4 n'accepte un horodatage rétroactif que sur 72 heures.</summary>
    private static readonly TimeSpan MaxBackdate = TimeSpan.FromHours(71);

    private readonly MarketingConfig _config;
    private readonly IHttpClientFactory _httpFactory;

    public Ga4MeasurementProtocolDestination(MarketingConfig config, IHttpClientFactory httpFactory)
    {
        _config = config;
        _httpFactory = httpFactory;
    }

    public string Name => "ga4";

    public bool IsConfigured => _config.Ga4Configured;

    public bool Accepts(ConversionEvent conversion) =>
        conversion.ConsentAnalytics && !string.IsNullOrWhiteSpace(conversion.GaClientId);

    public static string BuildBody(ConversionEvent c, DateTime nowUtc)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["transaction_id"] = c.TransactionId,
            ["value"] = decimal.Round(c.ValueXaf, 0),
            ["currency"] = c.Currency,
            ["item_category"] = c.Category,
            ["engagement_time_msec"] = 1,
            ["event_source"] = "server",
        };
        if (!string.IsNullOrWhiteSpace(c.GaSessionId) && long.TryParse(c.GaSessionId, out var sid))
            parameters["session_id"] = sid;
        if (!string.IsNullOrWhiteSpace(c.ItemsJson))
        {
            try { parameters["items"] = JsonSerializer.Deserialize<JsonElement>(c.ItemsJson); }
            catch (JsonException) { /* articles illisibles : l'achat part sans le détail */ }
        }

        var body = new Dictionary<string, object?>
        {
            ["client_id"] = c.GaClientId,
            ["non_personalized_ads"] = !c.ConsentAds,
            ["consent"] = new Dictionary<string, string>
            {
                ["ad_user_data"] = c.ConsentAds ? "GRANTED" : "DENIED",
                ["ad_personalization"] = c.ConsentAds ? "GRANTED" : "DENIED",
            },
            ["events"] = new[] { new Dictionary<string, object?> { ["name"] = c.EventName, ["params"] = parameters } },
        };
        var occurred = DateTime.SpecifyKind(c.OccurredAt, DateTimeKind.Utc);
        if (nowUtc - occurred <= MaxBackdate && occurred <= nowUtc)
            body["timestamp_micros"] = new DateTimeOffset(occurred).ToUnixTimeMilliseconds() * 1000;

        return JsonSerializer.Serialize(body);
    }

    public async Task<ConversionSendResult> SendAsync(ConversionEvent conversion, CancellationToken ct)
    {
        if (!IsConfigured) return new ConversionSendResult(ConversionSendOutcome.Retry, "not_configured");

        var url = $"{Endpoint}?measurement_id={Uri.EscapeDataString(_config.Ga4MeasurementId!)}&api_secret={Uri.EscapeDataString(_config.Ga4ApiSecret!)}";
        using var content = new StringContent(BuildBody(conversion, DateTime.UtcNow), Encoding.UTF8, "application/json");
        try
        {
            var client = _httpFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsync(url, content, ct);
            if (response.IsSuccessStatusCode) return ConversionSendResult.Ok;
            var code = (int)response.StatusCode;
            var error = $"HTTP {code}";
            return code == (int)HttpStatusCode.TooManyRequests || code >= 500
                ? new ConversionSendResult(ConversionSendOutcome.Retry, error)
                : new ConversionSendResult(ConversionSendOutcome.Rejected, error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new ConversionSendResult(ConversionSendOutcome.Retry, ex.GetType().Name);
        }
    }
}

/// <summary>
/// Lot 10.3 : export CSV des conversions hors ligne au format d'import
/// Google Ads (« Importer des conversions à partir de clics », modèle CSV).
/// Seules les conversions portant un gclid et le consentement publicité sont
/// exportées, dans la fenêtre de 90 jours après le clic qu'accepte Google.
/// </summary>
public static class GoogleAdsOfflineExport
{
    public const string TimeZone = "Africa/Douala";

    public static string BuildCsv(IEnumerable<ConversionEvent> conversions, string conversionName)
    {
        var sb = new StringBuilder();
        sb.Append("Parameters:TimeZone=").Append(TimeZone).Append("\r\n");
        sb.Append("Google Click ID,Conversion Name,Conversion Time,Conversion Value,Conversion Currency\r\n");
        foreach (var c in conversions)
        {
            if (!c.ConsentAds || string.IsNullOrWhiteSpace(c.Gclid)) continue;
            // Cameroun : UTC+1 toute l'année, sans heure d'été.
            var local = DateTime.SpecifyKind(c.OccurredAt, DateTimeKind.Utc).AddHours(1);
            sb.Append(Escape(c.Gclid)).Append(',')
              .Append(Escape(conversionName)).Append(',')
              .Append(local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(',')
              .Append(decimal.Round(c.ValueXaf, 0).ToString("0", CultureInfo.InvariantCulture)).Append(',')
              .Append(c.Currency).Append("\r\n");
        }
        return sb.ToString();
    }

    private static string Escape(string v) =>
        v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
}
