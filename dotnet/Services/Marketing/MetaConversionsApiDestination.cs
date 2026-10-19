using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Marketing;

/// <summary>
/// Lot 10.3 bis : API Conversions Meta (Graph API, <c>POST /{pixel_id}/events</c>).
/// Événement <c>Purchase</c> avec <c>event_id</c> égal au <c>transaction_id</c>
/// (numéro de commande ou référence de paiement), le même que l'<c>eventID</c>
/// du pixel côté navigateur : Meta ne compte l'achat qu'une fois.
///
/// N'envoie qu'avec le consentement publicité. <c>user_data</c> : cookies
/// <c>_fbc</c> et <c>_fbp</c>, IP et user-agent relevés avec ce consentement,
/// e-mail et téléphone du compte hachés en SHA-256 après normalisation
/// (jamais en clair). Le jeton d'accès part dans le corps de la requête,
/// jamais dans l'adresse ni dans les journaux.
/// </summary>
public sealed class MetaConversionsApiDestination : IConversionDestination
{
    public const string HttpClientName = "MetaConversionsApi";
    public const string GraphHost = "https://graph.facebook.com";
    public const string SiteOrigin = "https://winplus.cm";

    /// <summary>Meta refuse les événements de plus de 7 jours.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private readonly MarketingConfig _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ApplicationDbContext _db;

    public MetaConversionsApiDestination(MarketingConfig config, IHttpClientFactory httpFactory, ApplicationDbContext db)
    {
        _config = config;
        _httpFactory = httpFactory;
        _db = db;
    }

    public string Name => "meta";

    public bool IsConfigured => _config.MetaConfigured;

    public bool Accepts(ConversionEvent conversion) => conversion.ConsentAds;

    // ── Normalisation et hachage (règles Meta « customer information parameters ») ──

    public static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>E-mail : espaces retirés, minuscules. Null si ce n'est pas une adresse.</summary>
    public static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var t = email.Trim().ToLowerInvariant();
        var at = t.IndexOf('@');
        return at > 0 && at < t.Length - 3 && !t.Contains(' ') && t.Contains('.') ? t : null;
    }

    /// <summary>
    /// Téléphone : chiffres seuls, avec l'indicatif pays, sans « + » ni « 00 ».
    /// Un numéro camerounais à 9 chiffres (6… mobile, 2… fixe) reçoit l'indicatif 237.
    /// </summary>
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00")) digits = digits[2..];
        if (digits.Length == 9 && (digits[0] == '6' || digits[0] == '2')) digits = "237" + digits;
        return digits.Length is >= 8 and <= 15 && digits[0] != '0' ? digits : null;
    }

    public static string BuildBody(ConversionEvent c, string? email, string? phone, string? testEventCode)
    {
        var userData = new Dictionary<string, object?>();
        if (c.ConsentAds)
        {
            var em = NormalizeEmail(email);
            var ph = NormalizePhone(phone);
            if (em != null) userData["em"] = new[] { Sha256(em) };
            if (ph != null) userData["ph"] = new[] { Sha256(ph) };
            if (!string.IsNullOrWhiteSpace(c.Fbc)) userData["fbc"] = c.Fbc;
            if (!string.IsNullOrWhiteSpace(c.Fbp)) userData["fbp"] = c.Fbp;
            if (!string.IsNullOrWhiteSpace(c.ClientIp)) userData["client_ip_address"] = c.ClientIp;
            if (!string.IsNullOrWhiteSpace(c.ClientUserAgent)) userData["client_user_agent"] = c.ClientUserAgent;
        }

        var contents = new List<Dictionary<string, object?>>();
        if (!string.IsNullOrWhiteSpace(c.ItemsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(c.ItemsJson);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var id = item.TryGetProperty("item_id", out var v) ? v.GetString() : null;
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    contents.Add(new()
                    {
                        ["id"] = id,
                        ["quantity"] = item.TryGetProperty("quantity", out var q) && q.TryGetInt32(out var qi) ? qi : 1,
                        ["item_price"] = item.TryGetProperty("price", out var p) && p.TryGetDecimal(out var pd) ? decimal.Round(pd, 0) : 0m,
                    });
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { /* articles illisibles */ }
        }

        var customData = new Dictionary<string, object?>
        {
            ["value"] = decimal.Round(c.ValueXaf, 0),
            ["currency"] = c.Currency,
            ["order_id"] = c.TransactionId,
            ["content_category"] = c.Category,
            ["content_type"] = "product",
        };
        if (contents.Count > 0)
        {
            customData["contents"] = contents;
            customData["content_ids"] = contents.Select(x => x["id"]).ToArray();
            customData["num_items"] = contents.Count;
        }

        // « website » exige le user-agent et l'adresse de la page ; sans
        // user-agent relevé, l'achat est déclaré comme hors site.
        var website = userData.ContainsKey("client_user_agent");
        var ev = new Dictionary<string, object?>
        {
            ["event_name"] = "Purchase",
            ["event_time"] = new DateTimeOffset(DateTime.SpecifyKind(c.OccurredAt, DateTimeKind.Utc)).ToUnixTimeSeconds(),
            ["event_id"] = c.TransactionId,
            ["action_source"] = website ? "website" : "other",
            ["user_data"] = userData,
            ["custom_data"] = customData,
        };
        if (website)
        {
            var path = string.IsNullOrWhiteSpace(c.LandingPage) || !c.LandingPage.StartsWith('/') ? "/" : c.LandingPage;
            ev["event_source_url"] = SiteOrigin + path;
        }

        var body = new Dictionary<string, object?> { ["data"] = new[] { ev } };
        if (!string.IsNullOrWhiteSpace(testEventCode)) body["test_event_code"] = testEventCode;
        return JsonSerializer.Serialize(body);
    }

    /// <summary>Erreur passagère selon Meta (champ <c>is_transient</c> ou codes de limite de débit).</summary>
    public static bool IsTransientError(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) return false;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("error", out var err)) return false;
            if (err.TryGetProperty("is_transient", out var t) && t.ValueKind == JsonValueKind.True) return true;
            return err.TryGetProperty("code", out var code) && code.TryGetInt32(out var n) && n is 1 or 2 or 4 or 17 or 32 or 341 or 613;
        }
        catch (JsonException) { return false; }
    }

    public async Task<ConversionSendResult> SendAsync(ConversionEvent conversion, CancellationToken ct)
    {
        if (!IsConfigured) return new ConversionSendResult(ConversionSendOutcome.Retry, "not_configured");
        if (!conversion.ConsentAds) return new ConversionSendResult(ConversionSendOutcome.Rejected, "no_ads_consent");
        if (DateTime.UtcNow - DateTime.SpecifyKind(conversion.OccurredAt, DateTimeKind.Utc) > MaxAge)
            return new ConversionSendResult(ConversionSendOutcome.Rejected, "older_than_7_days");

        string? email = null, phone = null;
        if (conversion.UserId is int uid)
        {
            var user = await _db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Id == uid).Select(u => new { u.Email, u.Phone }).FirstOrDefaultAsync(ct);
            email = user?.Email;
            phone = user?.Phone;
        }

        // Le jeton est ajouté au corps ici seulement : BuildBody ne le connaît pas.
        var json = BuildBody(conversion, email, phone, _config.MetaTestEventCode);
        var withToken = json[..^1] + ",\"access_token\":" + JsonSerializer.Serialize(_config.MetaAccessToken) + "}";
        var url = $"{GraphHost}/{_config.MetaGraphVersion}/{Uri.EscapeDataString(_config.MetaPixelId!)}/events";
        using var content = new StringContent(withToken, Encoding.UTF8, "application/json");
        try
        {
            var client = _httpFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsync(url, content, ct);
            if (response.IsSuccessStatusCode) return ConversionSendResult.Ok;
            var code = (int)response.StatusCode;
            var text = await response.Content.ReadAsStringAsync(ct);
            var error = $"HTTP {code}{ErrorSummary(text)}";
            return code == (int)HttpStatusCode.TooManyRequests || code >= 500 || IsTransientError(text)
                ? new ConversionSendResult(ConversionSendOutcome.Retry, error)
                : new ConversionSendResult(ConversionSendOutcome.Rejected, error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new ConversionSendResult(ConversionSendOutcome.Retry, ex.GetType().Name);
        }
    }

    /// <summary>Code et sous-code d'erreur Meta, sans le message (qui peut citer la requête).</summary>
    private static string ErrorSummary(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var err)) return "";
            var code = err.TryGetProperty("code", out var c) ? c.ToString() : "?";
            var sub = err.TryGetProperty("error_subcode", out var s) ? s.ToString() : null;
            return sub == null ? $" (code {code})" : $" (code {code}, sous-code {sub})";
        }
        catch (JsonException) { return ""; }
    }
}
