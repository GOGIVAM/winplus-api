using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Programme d'affiliation côté affilié (professeur/tuteur).
/// GET  /api/affiliate/me              → mon compte (créé à la volée au premier appel)
/// GET  /api/affiliate/me/stats        → clics, conversions, gains par statut
/// GET  /api/affiliate/me/commissions  → historique détaillé
/// POST /api/affiliate/track-click     → public, appelé par le frontend quand ?ref=CODE est détecté
/// </summary>
[ApiController]
[Route("api/affiliate")]
public class AffiliateController : ControllerBase
{
    private readonly IAffiliateService _affiliate;
    private readonly ILogger<AffiliateController> _logger;

    public AffiliateController(IAffiliateService affiliate, ILogger<AffiliateController> logger)
    {
        _affiliate = affiliate;
        _logger = logger;
    }

    /// <summary>
    /// Fenêtre d'attribution courante (jours), publique : le frontend en a
    /// besoin dès la capture d'un ?ref=CODE, avant toute connexion — voir
    /// affiliateTracking.ts. Seule cette valeur est exposée, pas le reste des
    /// réglages admin (plafond de commission, etc.).
    /// </summary>
    [HttpGet("attribution-window")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAttributionWindow()
    {
        var settings = await _affiliate.GetSettingsAsync();
        return Ok(new { attributionWindowDays = settings.AttributionWindowDays });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMyAccount()
    {
        try
        {
            var account = await _affiliate.GetOrCreateMyAccountAsync(User.GetUserId());
            return Ok(account);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération du compte affilié");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    [HttpGet("me/stats")]
    [Authorize]
    public async Task<IActionResult> GetMyStats()
    {
        try
        {
            var stats = await _affiliate.GetMyStatsAsync(User.GetUserId());
            return stats == null ? NotFound() : Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des statistiques d'affiliation");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    [HttpGet("me/commissions")]
    [Authorize]
    public async Task<IActionResult> GetMyCommissions()
    {
        try
        {
            var commissions = await _affiliate.GetMyCommissionsAsync(User.GetUserId());
            return Ok(commissions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des commissions d'affiliation");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    [HttpPost("track-click")]
    [AllowAnonymous]
    public async Task<IActionResult> TrackClick([FromBody] TrackAffiliateClickRequest request)
    {
        // Best-effort : ne doit jamais bloquer la navigation du visiteur.
        try { await _affiliate.RecordClickAsync(request); }
        catch (Exception ex) { _logger.LogWarning(ex, "Suivi de clic d'affiliation échoué pour le code {Code}", request.Code); }
        return Ok();
    }
}
