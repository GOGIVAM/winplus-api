using Microsoft.AspNetCore.Mvc;
using Backend.Models.DTOs;
using Backend.Services;
using Backend.Extensions;

namespace Backend.Controllers;

/// <summary>
/// Controller pour gérer l'historique d'apprentissage
/// </summary>
[ApiController]
[Route("api/history")]
[Produces("application/json")]
public class HistoryController : ControllerBase
{
    private readonly IHistoryService _historyService;
    private readonly ILogger<HistoryController> _logger;

    public HistoryController(IHistoryService historyService, ILogger<HistoryController> logger)
    {
        _historyService = historyService;
        _logger = logger;
    }

    /// <summary>
    /// Ajoute un événement à l'historique d'apprentissage
    /// </summary>
    /// <param name="request">Détails de l'événement</param>
    /// <returns>L'événement créé</returns>
    /// <response code="200">Événement ajouté avec succès</response>
    /// <response code="400">Requête invalide</response>
    /// <response code="500">Erreur serveur</response>
    [HttpPost]
    [ProducesResponseType(typeof(HistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> AddToHistory([FromBody] AddHistoryRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = User.GetUserId();

            var response = await _historyService.AddToHistoryAsync(userId, request);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'ajout à l'historique");
            return StatusCode(500, new { error = "Erreur lors de l'ajout à l'historique" });
        }
    }

    /// <summary>
    /// Récupère l'historique complet de l'utilisateur
    /// </summary>
    /// <param name="page">Numéro de la page</param>
    /// <param name="limit">Nombre d'éléments par page</param>
    /// <returns>Liste de l'historique avec statistiques</returns>
    /// <response code="200">Historique récupéré avec succès</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet]
    [ProducesResponseType(typeof(HistoryListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetHistory([FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        try
        {
            if (page < 1) page = 1;
            if (limit < 1 || limit > 100) limit = 50;

            var userId = User.GetUserId();
            var response = await _historyService.GetUserHistoryAsync(userId, page, limit);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération de l'historique");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>
    /// Récupère l'historique filtré par type d'événement
    /// </summary>
    /// <param name="type">Type d'événement (course_started, course_completed, lesson_viewed, test_taken)</param>
    /// <param name="page">Numéro de la page</param>
    /// <param name="limit">Nombre d'éléments par page</param>
    /// <returns>Liste de l'historique filtré</returns>
    /// <response code="200">Historique récupéré avec succès</response>
    /// <response code="400">Type d'événement invalide</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("type/{type}")]
    [ProducesResponseType(typeof(HistoryListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetHistoryByType(string type, [FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(type))
                return BadRequest(new { error = "Type d'événement requis" });

            if (page < 1) page = 1;
            if (limit < 1 || limit > 100) limit = 50;

            var userId = User.GetUserId();

            var response = await _historyService.GetUserHistoryByTypeAsync(userId, type, page, limit);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération de l'historique filtré");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>
    /// Récupère l'historique filtré par cours
    /// </summary>
    /// <param name="subjectId">ID du cours</param>
    /// <param name="page">Numéro de la page</param>
    /// <param name="limit">Nombre d'éléments par page</param>
    /// <returns>Liste de l'historique du cours</returns>
    /// <response code="200">Historique récupéré avec succès</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("subject/{subjectId}")]
    [ProducesResponseType(typeof(HistoryListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetHistoryBySubject(int subjectId, [FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        try
        {
            if (subjectId < 1)
                return BadRequest(new { error = "ID de cours invalide" });

            if (page < 1) page = 1;
            if (limit < 1 || limit > 100) limit = 50;

            var userId = User.GetUserId();

            var response = await _historyService.GetUserHistoryBySubjectAsync(userId, subjectId, page, limit);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération de l'historique par cours");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>
    /// Récupère l'historique filtré par plage de dates
    /// </summary>
    /// <param name="startDate">Date de début (ISO 8601)</param>
    /// <param name="endDate">Date de fin (ISO 8601)</param>
    /// <param name="page">Numéro de la page</param>
    /// <param name="limit">Nombre d'éléments par page</param>
    /// <returns>Liste de l'historique de la période</returns>
    /// <response code="200">Historique récupéré avec succès</response>
    /// <response code="400">Requête invalide</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("range")]
    [ProducesResponseType(typeof(HistoryListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetHistoryByDateRange([FromQuery] string startDate, [FromQuery] string endDate, [FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        try
        {
            if (!DateTime.TryParse(startDate, out var start) || !DateTime.TryParse(endDate, out var end))
                return BadRequest(new { error = "Dates invalides" });

            if (start > end)
                return BadRequest(new { error = "La date de début doit être antérieure à la date de fin" });

            if (page < 1) page = 1;
            if (limit < 1 || limit > 100) limit = 50;

            var userId = User.GetUserId();

            var response = await _historyService.GetUserHistoryByDateRangeAsync(userId, start, end, page, limit);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération de l'historique par plage de dates");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>
    /// Récupère les statistiques d'apprentissage de l'utilisateur
    /// </summary>
    /// <returns>Statistiques d'apprentissage</returns>
    /// <response code="200">Statistiques récupérées avec succès</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("statistics")]
    [ProducesResponseType(typeof(HistoryStatistics), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetStatistics()
    {
        try
        {
            var userId = User.GetUserId();

            var statistics = await _historyService.GetUserStatisticsAsync(userId);
            return Ok(statistics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des statistiques");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>
    /// Récupère l'activité récente de l'utilisateur
    /// </summary>
    /// <param name="count">Nombre d'événements à récupérer</param>
    /// <returns>Liste des événements récents</returns>
    /// <response code="200">Activité récente récupérée avec succès</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("recent")]
    [ProducesResponseType(typeof(List<HistoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetRecentActivity([FromQuery] int count = 10)
    {
        try
        {
            if (count < 1 || count > 100) count = 10;

            var userId = User.GetUserId();

            var activity = await _historyService.GetRecentActivityAsync(userId, count);
            return Ok(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération de l'activité récente");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>
    /// Supprime un événement de l'historique
    /// </summary>
    /// <param name="id">ID de l'événement</param>
    /// <returns>Résultat de la suppression</returns>
    /// <response code="200">Événement supprimé avec succès</response>
    /// <response code="404">Événement non trouvé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteHistory(int id)
    {
        try
        {
            var result = await _historyService.DeleteHistoryAsync(id);
            if (!result)
                return NotFound(new { error = "Événement non trouvé" });

            return Ok(new { success = true, message = "Événement supprimé avec succès" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la suppression de l'événement");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>
    /// Efface tout l'historique de l'utilisateur
    /// </summary>
    /// <returns>Résultat de l'effacement</returns>
    /// <response code="200">Historique effacé avec succès</response>
    /// <response code="500">Erreur serveur</response>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ClearHistory()
    {
        try
        {
            var userId = User.GetUserId();

            var result = await _historyService.ClearUserHistoryAsync(userId);
            return Ok(new { success = result, message = "Historique effacé avec succès" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'effacement de l'historique");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }
}
