using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
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
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;

    public ParentController(IParentService parentService, ILogger<ParentController> logger, ApplicationDbContext db, IHttpClientFactory httpClientFactory)
    {
        _parentService = parentService;
        _logger = logger;
        _db = db;
        _httpClientFactory = httpClientFactory;
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

    /// <summary>
    /// Récupère les enfants du parent via la table ParentStudentLinks
    /// </summary>
    [HttpGet("children")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetChildren()
    {
        try
        {
            var parentId = User.GetUserId();
            var children = await _db.ParentStudentLinks
                .Where(l => l.ParentId == parentId)
                .Select(l => new
                {
                    studentId  = l.StudentId,
                    firstName  = l.Student != null ? l.Student.FirstName : null,
                    lastName   = l.Student != null ? l.Student.LastName  : null,
                    email      = l.Student != null ? l.Student.Email     : null,
                    level      = l.Student != null ? l.Student.Level     : null,
                    avatarUrl  = l.Student != null ? l.Student.AvatarUrl : null,
                    linkedAt   = l.CreatedAt
                })
                .ToListAsync();

            return Ok(new { data = children, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting children for parent");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les analytiques d'un enfant — vérifie que l'enfant appartient bien au parent
    /// </summary>
    [HttpGet("analytics/{childId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetChildAnalytics([FromRoute] int childId)
    {
        try
        {
            var parentId = User.GetUserId();

            var linked = await _db.ParentStudentLinks
                .AnyAsync(l => l.ParentId == parentId && l.StudentId == childId);

            if (!linked)
                return StatusCode(403, new { success = false, error = "Accès refusé : cet enfant n'est pas lié à votre compte" });

            var stats = await _parentService.GetChildStatsAsync(parentId, childId);
            return Ok(new { data = stats, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting child analytics");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les messages reçus par le parent (messagerie directe)
    /// </summary>
    [HttpGet("messages")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetMessages([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var parentId = User.GetUserId();
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var messages = await _db.DirectMessages
                .Where(m => m.ToUserId == parentId)
                .OrderByDescending(m => m.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(m => new
                {
                    m.Id,
                    m.Content,
                    m.IsRead,
                    m.CreatedAt,
                    m.ReadAt,
                    from = m.From == null ? null : new
                    {
                        id        = m.FromUserId,
                        firstName = m.From.FirstName,
                        lastName  = m.From.LastName,
                        avatarUrl = m.From.AvatarUrl
                    }
                })
                .ToListAsync();

            var unreadCount = await _db.DirectMessages
                .CountAsync(m => m.ToUserId == parentId && !m.IsRead);

            return Ok(new { data = messages, unreadCount, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting messages for parent");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// GET /api/parent/ai-alerts/{childId}
    /// Proxy vers Python /api/parent-alerts/{childId} — détection d'anomalies + messages WinAI
    /// </summary>
    [HttpGet("ai-alerts/{childId:int}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetAIAlerts([FromRoute] int childId)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient("FastApiClient");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/parent-alerts/{childId}");
            var auth = HttpContext.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(auth))
                req.Headers.TryAddWithoutValidation("Authorization", auth);

            var res = await httpClient.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogError("Python parent-alerts returned {Status}: {Body}", res.StatusCode, content);
                return StatusCode((int)res.StatusCode, new { message = "AI alert service unavailable" });
            }

            return Content(content, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching AI alerts for child {ChildId}", childId);
            return StatusCode(502, new { message = "Could not reach AI service" });
        }
    }
}
