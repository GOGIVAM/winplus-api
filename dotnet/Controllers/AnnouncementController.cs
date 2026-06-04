using Microsoft.AspNetCore.Mvc;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Controller pour la gestion des annonces
/// </summary>
[ApiController]
[Route("api/announcements")]
[Produces("application/json")]
public class AnnouncementController : ControllerBase
{
    private readonly IAnnouncementService _announcementService;
    private readonly ILogger<AnnouncementController> _logger;

    public AnnouncementController(IAnnouncementService announcementService, ILogger<AnnouncementController> logger)
    {
        _announcementService = announcementService ?? throw new ArgumentNullException(nameof(announcementService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Récupère les annonces publiées
    /// </summary>
    /// <param name="limit">Nombre maximum d'annonces</param>
    /// <returns>Annonces publiées</returns>
    /// <response code="200">Annonces retournées</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<Announcement>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPublished([FromQuery] int limit = 4)
    {
        try
        {
            var announcements = await _announcementService.GetPublishedAnnouncementsAsync(limit);
            return Ok(new { data = announcements, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting published announcements");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère une annonce par ID
    /// </summary>
    /// <param name="id">ID de l'annonce</param>
    /// <returns>L'annonce</returns>
    /// <response code="200">Annonce retournée</response>
    /// <response code="404">Annonce non trouvée</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(Announcement), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var announcement = await _announcementService.GetAnnouncementByIdAsync(id);
            if (announcement == null)
                return NotFound(new { success = false, error = "Announcement not found" });
            
            return Ok(new { data = announcement, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting announcement {AnnouncementId}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
