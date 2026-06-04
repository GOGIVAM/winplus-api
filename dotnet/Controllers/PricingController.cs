using Microsoft.AspNetCore.Mvc;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Controller pour la gestion des plans de tarification
/// </summary>
[ApiController]
[Route("api/pricing")]
[Produces("application/json")]
public class PricingController : ControllerBase
{
    private readonly IPricingService _pricingService;
    private readonly ILogger<PricingController> _logger;

    public PricingController(IPricingService pricingService, ILogger<PricingController> logger)
    {
        _pricingService = pricingService ?? throw new ArgumentNullException(nameof(pricingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Récupère les plans de tarification (tous ou par catégorie)
    /// </summary>
    /// <param name="category">Catégorie optionnelle (students, teachers, parents)</param>
    /// <returns>Plans de la catégorie ou tous les plans</returns>
    /// <response code="200">Plans retournés</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("plans")]
    [ProducesResponseType(typeof(IEnumerable<PricingPlan>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPlans([FromQuery] string? category = null)
    {
        try
        {
            IEnumerable<PricingPlan> plans;
            
            if (string.IsNullOrWhiteSpace(category))
            {
                plans = await _pricingService.GetAllPlansAsync();
            }
            else
            {
                plans = await _pricingService.GetPlansByCategoryAsync(category);
            }
            
            return Ok(new { data = plans, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pricing plans");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère un plan par ID
    /// </summary>
    /// <param name="id">ID du plan</param>
    /// <returns>Le plan</returns>
    /// <response code="200">Plan retourné</response>
    /// <response code="404">Plan non trouvé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("plans/{id}")]
    [ProducesResponseType(typeof(PricingPlan), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPlanById(int id)
    {
        try
        {
            var plan = await _pricingService.GetPlanByIdAsync(id);
            if (plan == null)
                return NotFound(new { success = false, error = "Plan not found" });
            
            return Ok(new { data = plan, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pricing plan {PlanId}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
