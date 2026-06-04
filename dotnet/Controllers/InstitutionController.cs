using Microsoft.AspNetCore.Mvc;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Controller pour la gestion des institutions
/// </summary>
[ApiController]
[Route("api/institutions")]
[Produces("application/json")]
public class InstitutionController : ControllerBase
{
    private readonly IInstitutionService _institutionService;
    private readonly ILogger<InstitutionController> _logger;

    public InstitutionController(IInstitutionService institutionService, ILogger<InstitutionController> logger)
    {
        _institutionService = institutionService ?? throw new ArgumentNullException(nameof(institutionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Récupère toutes les institutions
    /// </summary>
    /// <returns>Liste des institutions</returns>
    /// <response code="200">Institutions retournées</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<Institution>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var institutions = await _institutionService.GetAllInstitutionsAsync();
            return Ok(new { data = institutions, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all institutions");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les institutions par pays
    /// </summary>
    /// <param name="country">Code pays</param>
    /// <returns>Institutions du pays</returns>
    /// <response code="200">Institutions retournées</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<Institution>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetByCountry([FromQuery] string? country = null)
    {
        try
        {
            IEnumerable<Institution> institutions;
            
            if (string.IsNullOrWhiteSpace(country))
            {
                institutions = await _institutionService.GetAllInstitutionsAsync();
            }
            else
            {
                institutions = await _institutionService.GetInstitutionsByCountryAsync(country);
            }
            
            return Ok(new { data = institutions, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institutions by country {Country}", country);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère une institution par ID
    /// </summary>
    /// <param name="id">ID de l'institution</param>
    /// <returns>L'institution</returns>
    /// <response code="200">Institution retournée</response>
    /// <response code="404">Institution non trouvée</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(Institution), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var institution = await _institutionService.GetInstitutionByIdAsync(id);
            if (institution == null)
                return NotFound(new { success = false, error = "Institution not found" });
            
            return Ok(new { data = institution, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institution {InstitutionId}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
