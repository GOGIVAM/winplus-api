using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Backend.Services;
using Backend.Extensions;

namespace Backend.Controllers;

/// <summary>
/// Controller pour les fonctionnalités des parents
/// </summary>
[ApiController]
[Route("api/parent")]
[Produces("application/json")]
[Authorize]
public class ParentController : ControllerBase
{
    private readonly IParentService _parentService;
    private readonly ILogger<ParentController> _logger;

    public ParentController(IParentService parentService, ILogger<ParentController> logger)
    {
        _parentService = parentService;
        _logger = logger;
    }

    /// <summary>
    /// Récupère les statistiques de l'enfant
    /// </summary>
    /// <param name="parentId">ID du parent</param>
    /// <param name="childId">ID de l'enfant</param>
    /// <returns>Statistiques de l'enfant</returns>
    [HttpGet("children/{childId}/stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetChildStats([FromQuery] int parentId, [FromRoute] int childId)
    {
        try
        {
            var stats = await _parentService.GetChildStatsAsync(parentId, childId);
            return Ok(new { data = stats, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting child stats");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les activités récentes de l'enfant
    /// </summary>
    /// <param name="parentId">ID du parent</param>
    /// <param name="childId">ID de l'enfant</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Activités récentes</returns>
    [HttpGet("activities/recent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetRecentActivities([FromQuery] int parentId, [FromQuery] int childId, [FromQuery] int limit = 10)
    {
        try
        {
            var activities = await _parentService.GetChildActivitiesAsync(parentId, childId, limit);
            return Ok(new { data = activities, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent activities");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les paiements à venir
    /// </summary>
    /// <param name="parentId">ID du parent</param>
    /// <returns>Paiements à venir</returns>
    [HttpGet("payments/upcoming")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetUpcomingPayments([FromQuery] int parentId)
    {
        try
        {
            var payments = await _parentService.GetUpcomingPaymentsAsync(parentId);
            return Ok(new { data = payments, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting upcoming payments");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les événements à venir
    /// </summary>
    /// <param name="parentId">ID du parent</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Événements à venir</returns>
    [HttpGet("events/upcoming")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetUpcomingEvents([FromQuery] int parentId, [FromQuery] int limit = 10)
    {
        try
        {
            var events = await _parentService.GetUpcomingEventsAsync(parentId, limit);
            return Ok(new { data = events, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting upcoming events");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les quizzes de l'enfant
    /// </summary>
    /// <param name="parentId">ID du parent</param>
    /// <param name="childId">ID de l'enfant</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Quizzes de l'enfant</returns>
    [HttpGet("quizzes/available")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAvailableQuizzes([FromQuery] int parentId, [FromQuery] int childId, [FromQuery] int limit = 10)
    {
        try
        {
            var quizzes = await _parentService.GetChildQuizzesAsync(parentId, childId, limit);
            return Ok(new { data = quizzes, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available quizzes");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les révisions de l'enfant
    /// </summary>
    /// <param name="parentId">ID du parent</param>
    /// <param name="childId">ID de l'enfant</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Révisions de l'enfant</returns>
    [HttpGet("revisions/available")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAvailableRevisions([FromQuery] int parentId,[FromQuery] int childId, [FromQuery] int limit = 10)
    {
        try
        {
            var revisions = await _parentService.GetChildRevisionsAsync(parentId, childId, limit);
            return Ok(new { data = revisions, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available revisions");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère le profil du parent
    /// </summary>
    [HttpGet("profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            var parentId = User.GetUserId();
            var profile = await _parentService.GetParentProfileAsync(parentId);
            return Ok(new { data = profile, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting parent profile");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les objectifs d'un enfant
    /// </summary>
    [HttpGet("children/{childId}/goals")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetChildGoals([FromRoute] int childId, [FromQuery] int parentId)
    {
        try
        {
            var goals = await _parentService.GetChildGoalsAsync(parentId, childId);
            return Ok(new { data = goals, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting child goals");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
