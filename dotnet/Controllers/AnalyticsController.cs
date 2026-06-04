using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Backend.Models.DTOs;
using Backend.Services;
using Backend.Extensions;

namespace Backend.Controllers;

/// <summary>
/// Controller pour gérer les analytics
/// </summary>
[ApiController]
[Route("api/analytics")]
[Produces("application/json")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsService _analyticsService;
    private readonly ILogger<AnalyticsController> _logger;

    public AnalyticsController(IAnalyticsService analyticsService, ILogger<AnalyticsController> logger)
    {
        _analyticsService = analyticsService;
        _logger = logger;
    }

    /// <summary>
    /// Track un événement analytics
    /// </summary>
    /// <param name="request">Détails de l'événement</param>
    /// <returns>L'événement enregistré</returns>
    /// <response code="200">Événement enregistré avec succès</response>
    /// <response code="400">Requête invalide</response>
    /// <response code="500">Erreur serveur</response>
    [HttpPost("track")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AnalyticsEventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> TrackEvent([FromBody] TrackEventRequest? request)
    {
        try
        {
            // En dev, ne pas rejeter sur validation - accepter n'importe quoi
            if (request == null)
            {
                return BadRequest(new { error = "Request body is required" });
            }
            
            // Créer un objet minimal s'il manque des champs
            if (string.IsNullOrWhiteSpace(request.EventName))
            {
                request.EventName = "unknown_event";
            }

            // Ignorer les erreurs de validation du modèle (tout est optionnel sauf EventName)
            if (!ModelState.IsValid)
            {
                // Log mais ne pas rejeter en dev
                var errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage));
                _logger.LogWarning("ModelState validation issues (ignored in dev): {Errors}", string.Join(", ", errors));
            }

            // À remplacer par l'ID utilisateur authentifié
            // En dev, on utilise null si l'utilisateur n'est pas authentifié
            int? userId = null;

            var response = await _analyticsService.TrackEventAsync(userId, request);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument in track event");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracking event");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les statistiques de session
    /// </summary>
    /// <returns>Les statistiques de session</returns>
    /// <response code="200">Statistiques retournées</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("session")]
    [ProducesResponseType(typeof(SessionStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSessionStats()
    {
        try
        {
            // À remplacer par l'ID utilisateur authentifié
            var userId = 1;

            var response = await _analyticsService.GetSessionStatsAsync(userId);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session stats");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les analytics d'un utilisateur
    /// </summary>
    /// <param name="userId">ID de l'utilisateur</param>
    /// <returns>Les analytics utilisateur</returns>
    /// <response code="200">Analytics retournées</response>
    /// <response code="404">Utilisateur non trouvé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("user/{userId}")]
    [ProducesResponseType(typeof(UserAnalyticsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetUserAnalytics(int userId)
    {
        try
        {
            if (userId <= 0)
                return BadRequest(new { error = "Invalid user ID" });

            var response = await _analyticsService.GetUserAnalyticsAsync(userId);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user analytics for user: {UserId}", userId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les événements récents (admin only)
    /// </summary>
    /// <param name="limit">Nombre d'événements à retourner</param>
    /// <returns>Les événements récents</returns>
    /// <response code="200">Événements retournés</response>
    /// <response code="401">Non autorisé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("recent")]
    [ProducesResponseType(typeof(List<AnalyticsEventResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetRecentEvents([FromQuery] int limit = 20)
    {
        try
        {
            if (limit < 1) limit = 1;
            if (limit > 100) limit = 100;

            var response = await _analyticsService.GetRecentEventsAsync(limit);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent events");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
