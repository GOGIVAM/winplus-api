using System.Text;
using Backend.Data;
using Backend.Extensions;
using Backend.Services.Marketing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Lot 10.3 : source d'acquisition du compte connecté. Le web l'envoie après
/// une connexion et à chaque changement de consentement ; un consentement
/// retiré efface la première et la dernière source du compte.
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
/// <c>GET /api/admin/conversions/google-ads.csv</c> produit le fichier à
/// importer dans Google Ads (Objectifs, Conversions, Importer, Importer
/// des conversions à partir de clics). Voir DOCUMENTATION/GUIDE_GOOGLE_ADS.md
/// du dépôt web.
/// </summary>
[ApiController]
[Route("api/admin/conversions")]
[Authorize(Roles = "admin")]
public class AdminConversionsController : ControllerBase
{
    /// <summary>Nom par défaut de l'action de conversion à créer dans Google Ads (doit être identique).</summary>
    public const string DefaultConversionName = "Achat WinPlus";

    private readonly ApplicationDbContext _db;

    public AdminConversionsController(ApplicationDbContext db) => _db = db;

    /// <summary>Résumé : conversions enregistrées, avec gclid, et état des envois par destination.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (start, end) = Range(from, to);
        var conversions = _db.ConversionEvents.AsNoTracking().Where(c => c.OccurredAt >= start && c.OccurredAt < end);
        var total = await conversions.CountAsync();
        var withGclid = await conversions.CountAsync(c => c.ConsentAds && c.Gclid != null);
        var valueXaf = await conversions.SumAsync(c => (decimal?)c.ValueXaf) ?? 0;
        var deliveries = await _db.ConversionDeliveries.AsNoTracking()
            .Where(d => d.ConversionEvent!.OccurredAt >= start && d.ConversionEvent.OccurredAt < end)
            .GroupBy(d => new { d.Destination, d.Status })
            .Select(g => new { g.Key.Destination, g.Key.Status, Count = g.Count() })
            .ToListAsync();
        return Ok(new { from = start, to = end, total, withGclid, valueXaf, currency = "XAF", deliveries });
    }

    /// <summary>
    /// Fichier CSV d'import des conversions hors ligne Google Ads : achats
    /// confirmés portant un gclid (consentement publicité), sur la période
    /// demandée (30 derniers jours par défaut, 90 jours au plus : Google
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
