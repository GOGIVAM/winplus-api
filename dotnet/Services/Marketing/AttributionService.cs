using System.Net;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Marketing;

/// <summary>
/// Lot 10.3 : attribution envoyée par le web (voir src/lib/measurement/attribution.ts).
/// Tous les champs sont facultatifs ; les deux indicateurs de consentement
/// reflètent le choix du visiteur au moment de l'envoi.
/// </summary>
public sealed class AttributionDto
{
    public string? Gclid { get; set; }
    public string? Gbraid { get; set; }
    public string? Wbraid { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmTerm { get; set; }
    public string? UtmContent { get; set; }
    public string? Referrer { get; set; }
    public string? LandingPage { get; set; }
    public string? GaClientId { get; set; }
    public string? GaSessionId { get; set; }
    /// <summary>Lot 10.3 bis : identifiant de clic Meta et cookies du pixel (_fbc, _fbp), avec le consentement publicité.</summary>
    public string? Fbclid { get; set; }
    public string? Fbc { get; set; }
    public string? Fbp { get; set; }
    /// <summary>Relevés par le serveur sur la requête, jamais lus dans le corps envoyé par le client.</summary>
    [JsonIgnore] public string? ClientIp { get; set; }
    [JsonIgnore] public string? ClientUserAgent { get; set; }
    public DateTime? CapturedAt { get; set; }
    public bool ConsentAnalytics { get; set; }
    public bool ConsentAds { get; set; }
    /// <summary>
    /// Première source connue du navigateur, si elle diffère de celle-ci :
    /// sert de première source du compte s'il n'en a pas encore. Les
    /// consentements de l'objet principal s'y appliquent.
    /// </summary>
    public AttributionDto? First { get; set; }
}

/// <summary>
/// Règles de conservation (décision Q15 b) : sans aucun consentement, rien
/// n'est gardé ; les identifiants de clic publicitaires (gclid, gbraid,
/// wbraid, fbclid), les cookies Meta (_fbc, _fbp), l'IP et le user-agent
/// exigent le consentement publicité ; l'identifiant client GA4
/// exige le consentement mesure. Les valeurs sont tronquées et nettoyées.
/// </summary>
public static class AttributionRules
{
    /// <summary>Fenêtre d'attribution d'un clic Google Ads (90 jours, comme l'import hors ligne).</summary>
    public static readonly TimeSpan ClickWindow = TimeSpan.FromDays(90);

    public static MarketingAttribution? Sanitize(AttributionDto? dto, DateTime nowUtc)
    {
        if (dto == null || (!dto.ConsentAnalytics && !dto.ConsentAds)) return null;

        var captured = dto.CapturedAt.HasValue ? ToUtc(dto.CapturedAt.Value) : nowUtc;
        if (captured > nowUtc.AddMinutes(5) || captured < nowUtc - ClickWindow) captured = nowUtc;

        var a = new MarketingAttribution
        {
            ConsentAnalytics = dto.ConsentAnalytics,
            ConsentAds = dto.ConsentAds,
            CapturedAt = captured,
            Gclid = dto.ConsentAds ? Token(dto.Gclid, 255) : null,
            Gbraid = dto.ConsentAds ? Token(dto.Gbraid, 255) : null,
            Wbraid = dto.ConsentAds ? Token(dto.Wbraid, 255) : null,
            UtmSource = Text(dto.UtmSource, 150),
            UtmMedium = Text(dto.UtmMedium, 150),
            UtmCampaign = Text(dto.UtmCampaign, 150),
            UtmTerm = Text(dto.UtmTerm, 150),
            UtmContent = Text(dto.UtmContent, 150),
            Referrer = Host(dto.Referrer),
            LandingPage = Path(dto.LandingPage),
            GaClientId = dto.ConsentAnalytics ? ClientId(dto.GaClientId) : null,
            GaSessionId = dto.ConsentAnalytics ? Digits(dto.GaSessionId, 50) : null,
            Fbclid = dto.ConsentAds ? Token(dto.Fbclid, 255) : null,
            Fbc = dto.ConsentAds ? MetaCookie(dto.Fbc, 300) : null,
            Fbp = dto.ConsentAds ? MetaCookie(dto.Fbp, 100) : null,
            ClientIp = dto.ConsentAds ? Ip(dto.ClientIp) : null,
            ClientUserAgent = dto.ConsentAds ? UserAgent(dto.ClientUserAgent) : null,
        };
        return a;
    }

    /// <summary>Vrai si l'attribution porte une source (clic, campagne ou référent), pas seulement un consentement.</summary>
    public static bool HasSource(MarketingAttribution a) =>
        a.Gclid != null || a.Gbraid != null || a.Wbraid != null || a.Fbclid != null || a.UtmSource != null
        || a.UtmMedium != null || a.UtmCampaign != null || a.Referrer != null;

    /// <summary>
    /// Valeur _fbc à envoyer à Meta : le cookie s'il existe, sinon reconstruit
    /// depuis le fbclid capté (format « fb.1.&lt;horodatage ms&gt;.&lt;fbclid&gt; »).
    /// </summary>
    public static string? FbcFor(MarketingAttribution a)
    {
        if (!a.ConsentAds) return null;
        if (!string.IsNullOrWhiteSpace(a.Fbc)) return a.Fbc;
        if (string.IsNullOrWhiteSpace(a.Fbclid)) return null;
        var at = a.CapturedAt.HasValue ? ToUtc(a.CapturedAt.Value) : DateTime.UtcNow;
        return $"fb.1.{new DateTimeOffset(at).ToUnixTimeMilliseconds()}.{a.Fbclid}";
    }

    /// <summary>Retire ce qui exige le consentement publicité.</summary>
    public static void StripAds(MarketingAttribution a)
    {
        a.Gclid = null; a.Gbraid = null; a.Wbraid = null;
        a.Fbclid = null; a.Fbc = null; a.Fbp = null; a.ClientIp = null; a.ClientUserAgent = null;
    }

    private static readonly Regex MetaCookiePattern = new(@"^fb\.[0-2]\.\d{10,14}\.[\w.\-]{1,255}$", RegexOptions.CultureInvariant);

    private static string? MetaCookie(string? v, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var t = v.Trim();
        return t.Length <= max && MetaCookiePattern.IsMatch(t) ? t : null;
    }

    private static string? Ip(string? v)
    {
        if (string.IsNullOrWhiteSpace(v) || !IPAddress.TryParse(v.Trim(), out var ip)) return null;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return null;
        var t = ip.ToString();
        return t.Length <= 64 ? t : null;
    }

    private static string? UserAgent(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var clean = new string(v.Trim().Where(c => !char.IsControl(c)).ToArray());
        return clean.Length > 500 ? clean[..500] : clean;
    }

    private static DateTime ToUtc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
    };

    private static string? Text(string? v, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var clean = new string(v.Trim().Where(c => !char.IsControl(c)).ToArray());
        // Une adresse e-mail glissée dans un paramètre utm n'est jamais gardée.
        if (clean.Contains('@')) return null;
        return clean.Length > max ? clean[..max] : clean;
    }

    private static string? Token(string? v, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var t = v.Trim();
        if (t.Length > max || !t.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.')) return null;
        return t;
    }

    private static string? Digits(string? v, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var t = v.Trim();
        return t.Length <= max && t.All(char.IsDigit) ? t : null;
    }

    /// <summary>Identifiant client GA4 : deux nombres séparés par un point (format du cookie _ga).</summary>
    private static string? ClientId(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var t = v.Trim();
        var parts = t.Split('.');
        return t.Length <= 100 && parts.Length == 2 && parts.All(p => p.Length > 0 && p.All(char.IsDigit)) ? t : null;
    }

    private static string? Host(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var t = v.Trim();
        if (Uri.TryCreate(t, UriKind.Absolute, out var uri)) return Text(uri.Host, 255);
        return Text(t.Split('/', '?', '#')[0], 255);
    }

    private static string? Path(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var t = v.Trim().Split('?', '#')[0];
        if (!t.StartsWith('/')) return null;
        return Text(t, 300);
    }
}

public interface IAttributionService
{
    /// <summary>
    /// Met à jour la première (si absente) et la dernière source du compte.
    /// Sans aucun consentement, efface les deux (retrait du consentement).
    /// Ne lève jamais d'exception.
    /// </summary>
    Task RecordForUserAsync(int userId, AttributionDto? dto, CancellationToken ct = default);

    /// <summary>
    /// Fige l'attribution sur la commande : celle envoyée par le web, sinon
    /// la dernière source connue du compte (si encore dans la fenêtre de
    /// 90 jours). Ne lève jamais d'exception.
    /// </summary>
    Task ApplyToOrderAsync(int orderId, int? userId, AttributionDto? dto, CancellationToken ct = default);
}

public sealed class AttributionService : IAttributionService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AttributionService> _logger;
    private readonly IHttpContextAccessor? _http;

    public AttributionService(ApplicationDbContext db, ILogger<AttributionService> logger, IHttpContextAccessor? http = null)
    {
        _db = db;
        _logger = logger;
        _http = http;
    }

    /// <summary>
    /// Lot 10.3 bis : IP et user-agent de la requête du visiteur (API
    /// Conversions Meta), jamais lus dans le corps. Les règles ne les gardent
    /// qu'avec le consentement publicité.
    /// </summary>
    private void StampRequest(AttributionDto dto)
    {
        dto.ClientIp = null;
        dto.ClientUserAgent = null;
        if (!dto.ConsentAds) return;
        var ctx = _http?.HttpContext;
        if (ctx == null) return;
        dto.ClientIp = ctx.Connection.RemoteIpAddress?.ToString();
        var ua = ctx.Request.Headers.UserAgent.ToString();
        dto.ClientUserAgent = string.IsNullOrWhiteSpace(ua) ? null : ua;
    }

    public async Task RecordForUserAsync(int userId, AttributionDto? dto, CancellationToken ct = default)
    {
        if (dto == null) return;
        try
        {
            StampRequest(dto);
            var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user == null) return;

            var now = DateTime.UtcNow;
            var a = AttributionRules.Sanitize(dto, now);
            if (a == null)
            {
                // Consentement retiré : on ne garde rien.
                if (user.FirstTouch == null && user.LastTouch == null) return;
                user.FirstTouch = null;
                user.LastTouch = null;
            }
            else
            {
                // Un simple renvoi du consentement (sans nouvelle source) ne
                // remplace pas la dernière source connue : il met à jour le
                // consentement et l'identifiant client, en retirant ce que le
                // nouveau consentement n'autorise plus.
                if (!AttributionRules.HasSource(a) && user.LastTouch != null)
                {
                    var merged = user.LastTouch.Clone();
                    merged.ConsentAnalytics = a.ConsentAnalytics;
                    merged.ConsentAds = a.ConsentAds;
                    merged.GaClientId = a.GaClientId ?? (a.ConsentAnalytics ? merged.GaClientId : null);
                    merged.GaSessionId = a.GaSessionId ?? (a.ConsentAnalytics ? merged.GaSessionId : null);
                    if (!a.ConsentAds) AttributionRules.StripAds(merged);
                    else
                    {
                        // Les cookies Meta, l'IP et le user-agent les plus récents.
                        merged.Fbc = a.Fbc ?? merged.Fbc;
                        merged.Fbp = a.Fbp ?? merged.Fbp;
                        merged.ClientIp = a.ClientIp ?? merged.ClientIp;
                        merged.ClientUserAgent = a.ClientUserAgent ?? merged.ClientUserAgent;
                    }
                    a = merged;
                }
                if (user.FirstTouch == null)
                {
                    MarketingAttribution? first = null;
                    if (dto.First != null)
                    {
                        dto.First.ConsentAnalytics = dto.ConsentAnalytics;
                        dto.First.ConsentAds = dto.ConsentAds;
                        dto.First.GaClientId ??= dto.GaClientId;
                        dto.First.ClientIp = dto.ClientIp;
                        dto.First.ClientUserAgent = dto.ClientUserAgent;
                        first = AttributionRules.Sanitize(dto.First, now);
                    }
                    user.FirstTouch = first ?? a.Clone();
                }
                user.LastTouch = a;
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Attribution du compte {UserId} non enregistrée", userId);
        }
    }

    public async Task ApplyToOrderAsync(int orderId, int? userId, AttributionDto? dto, CancellationToken ct = default)
    {
        try
        {
            var order = await _db.Orders.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (order == null) return;

            var now = DateTime.UtcNow;
            if (dto != null) StampRequest(dto);
            MarketingAttribution? a = AttributionRules.Sanitize(dto, now);

            if (a == null && dto == null && userId.HasValue)
            {
                // Rien d'envoyé (ancien client, application mobile, achat
                // lancé côté serveur) : dernière source connue du compte.
                var last = await _db.Users.IgnoreQueryFilters().AsNoTracking()
                    .Where(u => u.Id == userId.Value).Select(u => u.LastTouch).FirstOrDefaultAsync(ct);
                if (last != null && (last.CapturedAt ?? now) >= now - AttributionRules.ClickWindow)
                    a = last.Clone();
            }
            else if (dto != null && userId.HasValue)
            {
                // Met aussi à jour le compte (ou l'efface si le visiteur a
                // retiré son consentement).
                await RecordForUserAsync(userId.Value, dto, ct);
            }

            if (a == null) return;
            order.Attribution = a;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Attribution de la commande {OrderId} non enregistrée", orderId);
        }
    }
}
