using Microsoft.AspNetCore.Mvc;
using Backend.Services;
using Backend.Models.DTOs;

namespace Backend.Controllers;

/// <summary>
/// Controller pour les données de la page d'accueil (HomePage)
/// </summary>
[ApiController]
[Route("api/home")]
[Produces("application/json")]
public class HomeController : ControllerBase
{
    private readonly IHomeService _homeService;
    private readonly ILogger<HomeController> _logger;

    public HomeController(IHomeService homeService, ILogger<HomeController> logger)
    {
        _homeService = homeService ?? throw new ArgumentNullException(nameof(homeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Récupère les statistiques globales de la plateforme
    /// </summary>
    /// <response code="200">Statistiques retournées</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(HomeStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetStats()
    {
        try
        {
            var stats = await _homeService.GetHomeStatsAsync();
            return Ok(new { data = stats, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting home statistics");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les fonctionnalités affichées sur la HomePage
    /// </summary>
    /// <response code="200">Fonctionnalités retournées</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("features")]
    [ProducesResponseType(typeof(IEnumerable<HomeFeatureDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFeatures()
    {
        try
        {
            var features = await _homeService.GetHomeFeaturesAsync();
            return Ok(new { data = features, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting home features");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les informations de contact
    /// </summary>
    /// <response code="200">Informations de contact retournées</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("contact")]
    [ProducesResponseType(typeof(IEnumerable<ContactInfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetContact()
    {
        try
        {
            var contact = await _homeService.GetContactInfoAsync();
            return Ok(new { data = contact, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting contact information");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère le contenu "À propos"
    /// </summary>
    /// <response code="200">Contenu retourné</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("about")]
    [ProducesResponseType(typeof(PageContentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAbout()
    {
        try
        {
            var about = await _homeService.GetAboutContentAsync();
            return Ok(new { data = about, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting about content");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Compte les épreuves publiées par type d'examen
    /// GET /api/home/exam-counts → [{ examId: "bepc", count: 96 }, ...]
    /// </summary>
    [HttpGet("exam-counts")]
    [ProducesResponseType(typeof(IEnumerable<ExamCountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetExamCounts()
    {
        try
        {
            var counts = await _homeService.GetExamCountsAsync();
            return Ok(new { data = counts, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting exam counts");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les données du footer
    /// </summary>
    /// <response code="200">Données du footer retournées</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("footer")]
    [ProducesResponseType(typeof(FooterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFooter()
    {
        try
        {
            var footer = await _homeService.GetFooterAsync();
            return Ok(new { data = footer, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting footer data");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
