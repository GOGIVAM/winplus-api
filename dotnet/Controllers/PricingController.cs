using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Controllers;

[ApiController]
[Route("api/pricing")]
[Produces("application/json")]
public class PricingController : ControllerBase
{
    private readonly IPricingService _pricingService;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<PricingController> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public PricingController(IPricingService pricingService, ApplicationDbContext db, ILogger<PricingController> logger)
    {
        _pricingService = pricingService;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Retourne tous les plans actifs, Features parsé en string[]
    /// </summary>
    [HttpGet("plans")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlans([FromQuery] string? category = null)
    {
        try
        {
            var query = _db.PricingPlans.Where(p => !p.IsDeleted);
            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(p => p.Category == category);

            var plans = await query.OrderBy(p => p.Price).ToListAsync();
            var response = plans.Select(MapToResponse).ToList();
            return Ok(new { data = response, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pricing plans");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Retourne les promotions actives non expirées
    /// </summary>
    [HttpGet("promotions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPromotions()
    {
        try
        {
            var now = DateTime.UtcNow;
            var promotions = await _db.Promotions
                .Where(p => p.IsActive && (p.ValidUntil == null || p.ValidUntil > now))
                .OrderByDescending(p => p.DiscountPercent)
                .Select(p => new PromotionResponse
                {
                    Id = p.Id,
                    Code = p.Code,
                    DiscountPercent = p.DiscountPercent,
                    ValidFrom = p.ValidFrom,
                    ValidUntil = p.ValidUntil
                })
                .ToListAsync();

            return Ok(new { data = promotions, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting promotions");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Tableau comparatif groupé par catégorie
    /// </summary>
    [HttpGet("compare")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCompare()
    {
        try
        {
            var plans = await _db.PricingPlans
                .Where(p => !p.IsDeleted)
                .OrderBy(p => p.Category)
                .ThenBy(p => p.Price)
                .ToListAsync();

            var grouped = plans
                .GroupBy(p => p.Category)
                .Select(g => new PricingCategoryGroup
                {
                    Category = g.Key,
                    Plans = g.Select(MapToResponse).ToList()
                })
                .ToList();

            return Ok(new { data = grouped, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pricing comparison");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère un plan par ID
    /// </summary>
    [HttpGet("plans/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlanById(int id)
    {
        try
        {
            var plan = await _db.PricingPlans.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
            if (plan == null)
                return NotFound(new { success = false, error = "Plan not found" });

            return Ok(new { data = MapToResponse(plan), success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pricing plan {PlanId}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    private static PricingPlanResponse MapToResponse(Models.Entities.PricingPlan plan)
    {
        string[] features = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(plan.Features))
        {
            try
            {
                features = JsonSerializer.Deserialize<string[]>(plan.Features, _jsonOpts)
                           ?? Array.Empty<string>();
            }
            catch { /* malformed JSON — keep empty */ }
        }

        return new PricingPlanResponse
        {
            Id = plan.Id,
            Name = plan.Name,
            Category = plan.Category,
            Price = plan.Price,
            Currency = plan.Currency,
            Period = plan.Period,
            BillingPeriod = plan.BillingPeriod,
            Features = features,
            IsPopular = plan.IsPopular,
            Icon = plan.Icon,
            Description = plan.Description,
            MaxDownloads = plan.MaxDownloads,
            MaxChatMessages = plan.MaxChatMessages
        };
    }
}
