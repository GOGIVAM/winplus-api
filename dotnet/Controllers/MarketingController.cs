using System.Text;
using System.Text.RegularExpressions;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services.Marketing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Lot 10.3 : source d'acquisition du compte connectÃ©. Le web l'envoie aprÃ¨s
/// une connexion et Ã  chaque changement de consentement ; un consentement
/// retirÃ© efface la premiÃ¨re et la derniÃ¨re source du compte.
/// </summary>
[ApiController]
[Route("api/attribution")]
[Authorize]
public class AttributionController : ControllerBase
{
    private readonly IAttributionService _attribution;

    public AttributionController(IAttributionService attribution) => _attribution = attribution;

    [HttpPost]
    public async Task<IActionResult> Record([FromBody] AttributionDto? dto)
    {
        if (dto == null) return BadRequest(new { error = "Corps requis." });
        await _attribution.RecordForUserAsync(User.GetUserId(), dto, HttpContext.RequestAborted);
        return NoContent();
    }
}

/// <summary>
/// Lot 10.3 : suivi et export des conversions pour l'administrateur.
/// <c>GET /api/admin/conversions/google-ads.csv</c> produit le fichier Ã 
/// importer dans Google Ads (Objectifs, Conversions, Importer, Importer
/// des conversions Ã  partir de clics). Voir DOCUMENTATION/GUIDE_GOOGLE_ADS.md
/// du dÃ©pÃ´t web.
/// </summary>
[ApiController]
[Route("api/admin/conversions")]
[Authorize(Roles = "admin")]
public class AdminConversionsController : ControllerBase
{
    /// <summary>Nom par dÃ©faut de l'action de conversion Ã  crÃ©er dans Google Ads (doit Ãªtre identique).</summary>
    public const string DefaultConversionName = "Achat WinPlus";

    private readonly ApplicationDbContext _db;

    public AdminConversionsController(ApplicationDbContext db) => _db = db;

    /// <summary>
    /// RÃ©sumÃ© : conversions enregistrÃ©es, avec gclid ou identifiant Meta, Ã©tat
    /// des envois par destination, et ventilation par source et campagne utm
    /// (achats et inscriptions de la pÃ©riode). Une source vide regroupe les
    /// visites directes et celles sans paramÃ¨tre utm.
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (start, end) = Range(from, to);
        var conversions = _db.ConversionEvents.AsNoTracking().Where(c => c.OccurredAt >= start && c.OccurredAt < end);
        var total = await conversions.CountAsync();
        var withGclid = await conversions.CountAsync(c => c.ConsentAds && c.Gclid != null);
        var withMeta = await conversions.CountAsync(c => c.ConsentAds && (c.Fbc != null || c.Fbp != null));
        var valueXaf = await conversions.SumAsync(c => (decimal?)c.ValueXaf) ?? 0;
        var deliveries = await _db.ConversionDeliveries.AsNoTracking()
            .Where(d => d.ConversionEvent!.OccurredAt >= start && d.ConversionEvent.OccurredAt < end)
            .GroupBy(d => new { d.Destination, d.Status })
            .Select(g => new { g.Key.Destination, g.Key.Status, Count = g.Count() })
            .ToListAsync();
        var byCampaign = await conversions
            .GroupBy(c => new { c.UtmSource, c.UtmCampaign })
            .Select(g => new { utmSource = g.Key.UtmSource, utmCampaign = g.Key.UtmCampaign, conversions = g.Count(), valueXaf = g.Sum(c => c.ValueXaf) })
            .OrderByDescending(x => x.valueXaf)
            .Take(50)
            .ToListAsync();
        var signupsByCampaign = await _db.Users.AsNoTracking()
            .Where(u => u.CreatedAt >= start && u.CreatedAt < end && u.FirstTouch!.UtmSource != null)
            .GroupBy(u => new { u.FirstTouch!.UtmSource, u.FirstTouch.UtmCampaign })
            .Select(g => new { utmSource = g.Key.UtmSource, utmCampaign = g.Key.UtmCampaign, signups = g.Count() })
            .OrderByDescending(x => x.signups)
            .Take(50)
            .ToListAsync();
        return Ok(new { from = start, to = end, total, withGclid, withMeta, valueXaf, currency = "XAF", deliveries, byCampaign, signupsByCampaign });
    }

    /// <summary>
    /// Fichier CSV d'import des conversions hors ligne Google Ads : achats
    /// confirmÃ©s portant un gclid (consentement publicitÃ©), sur la pÃ©riode
    /// demandÃ©e (30 derniers jours par dÃ©faut, 90 jours au plus : Google
    /// refuse les clics plus anciens).
    /// </summary>
    [HttpGet("google-ads.csv")]
    public async Task<IActionResult> GoogleAdsCsv([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? conversionName)
    {
        var (start, end) = Range(from, to);
        var name = string.IsNullOrWhiteSpace(conversionName) ? DefaultConversionName : conversionName.Trim();
        var rows = await _db.ConversionEvents.AsNoTracking()
            .Where(c => c.OccurredAt >= start && c.OccurredAt < end && c.ConsentAds && c.Gclid != null)
            .OrderBy(c => c.OccurredAt)
            .ToListAsync();
        var csv = GoogleAdsOfflineExport.BuildCsv(rows, name);
        var fileName = $"google-ads-conversions_{start:yyyy-MM-dd}_{end.AddDays(-1):yyyy-MM-dd}.csv";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", fileName);
    }

    private static (DateTime start, DateTime end) Range(DateTime? from, DateTime? to)
    {
        var today = DateTime.UtcNow.Date;
        var end = (to?.Date ?? today).AddDays(1);
        var start = from?.Date ?? end.AddDays(-30);
        if (start < end.AddDays(-90)) start = end.AddDays(-90);
        if (start > end) start = end.AddDays(-1);
        return (DateTime.SpecifyKind(start, DateTimeKind.Utc), DateTime.SpecifyKind(end, DateTimeKind.Utc));
    }
}

/// <summary>
/// Lot 10.3 bis : historique du gÃ©nÃ©rateur de liens UTM de l'espace
/// administrateur. Le lien est construit par le web ; le serveur vÃ©rifie
/// qu'il pointe vers winplus.cm et que les paramÃ¨tres suivent la convention
/// (minuscules, sans accents, mots sÃ©parÃ©s par des tirets).
/// </summary>
[ApiController]
[Route("api/admin/utm-links")]
[Authorize(Roles = "admin")]
public class AdminUtmLinksController : ControllerBase
{
    private static readonly Regex Slug = new(@"^[a-z0-9]+(?:[-_][a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private readonly ApplicationDbContext _db;

    public AdminUtmLinksController(ApplicationDbContext db) => _db = db;

    public sealed class UtmLinkRequest
    {
        public string? Url { get; set; }
        public string? Label { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int take = 100)
    {
        take = Math.Clamp(take, 1, 500);
        var rows = await _db.UtmLinks.AsNoTracking().OrderByDescending(l => l.CreatedAt).Take(take).ToListAsync();
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UtmLinkRequest? req)
    {
        var link = Parse(req?.Url, out var error);
        if (link == null) return BadRequest(new { error });
        var label = req?.Label?.Trim();
        link.Label = string.IsNullOrEmpty(label) ? null : label.Length > 150 ? label[..150] : label;
        link.CreatedByUserId = User.GetUserId();
        _db.UtmLinks.Add(link);
        await _db.SaveChangesAsync();
        return Ok(link);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var link = await _db.UtmLinks.FirstOrDefaultAsync(l => l.Id == id);
        if (link == null) return NotFound();
        _db.UtmLinks.Remove(link);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>VÃ©rifie un lien de campagne : https://winplus.cm, utm_source, utm_medium et utm_campaign obligatoires.</summary>
    public static UtmLink? Parse(string? url, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(url) || url.Length > 1000 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        { error = "Lien invalide."; return null; }
        if (uri.Scheme != Uri.UriSchemeHttps || !(uri.Host == "winplus.cm" || uri.Host == "www.winplus.cm"))
        { error = "Le lien doit pointer vers https://winplus.cm."; return null; }
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        string? Get(string k) => query.TryGetValue(k, out var v) ? v.ToString() : null;
        var source = Get("utm_source");
        var medium = Get("utm_medium");
        var campaign = Get("utm_campaign");
        var content = Get("utm_content");
        foreach (var (name, value, required) in new[] { ("utm_source", source, true), ("utm_medium", medium, true), ("utm_campaign", campaign, true), ("utm_content", content, false) })
        {
            if (string.IsNullOrEmpty(value)) { if (required) { error = $"{name} manquant."; return null; } continue; }
            if (value.Length > 150 || !Slug.IsMatch(value)) { error = $"{name} : minuscules, chiffres et tirets seulement."; return null; }
        }
        return new UtmLink
        {
            Url = uri.AbsoluteUri,
            Destination = uri.AbsolutePath.Length > 300 ? uri.AbsolutePath[..300] : uri.AbsolutePath,
            UtmSource = source!,
            UtmMedium = medium!,
            UtmCampaign = campaign!,
            UtmContent = string.IsNullOrEmpty(content) ? null : content,
            CreatedAt = DateTime.UtcNow,
        };
    }
}
