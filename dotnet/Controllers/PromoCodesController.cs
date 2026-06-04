using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Backend.Services;
using Backend.Models.DTOs;
using Backend.Extensions;

namespace Backend.Controllers;

[ApiController]
[Route("api/promo-codes")]
public class PromoCodesController : ControllerBase
{
    private readonly IPromoCodeService _promoCodeService;
    private readonly ILogger<PromoCodesController> _logger;

    public PromoCodesController(IPromoCodeService promoCodeService, ILogger<PromoCodesController> logger)
    {
        _promoCodeService = promoCodeService;
        _logger = logger;
    }

    /// <summary>
    /// Create a new promo code (admin only)
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> CreatePromoCode([FromBody] CreatePromoCodeRequest request)
    {
        try
        {
            var adminUserId = User.GetUserId();
            var promoCode = await _promoCodeService.CreatePromoCodeAsync(adminUserId, request);

            return CreatedAtAction(
                nameof(GetPromoCodeByCode),
                new { code = promoCode.Code },
                new
                {
                    success = true,
                    data = promoCode,
                    message = "Promo code created successfully",
                    timestamp = DateTime.UtcNow
                });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating promo code");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Validate a promo code without applying it
    /// </summary>
    [HttpPost("validate")]
    [Authorize]
    public async Task<IActionResult> ValidatePromoCode([FromBody] ValidatePromoCodeRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            var result = await _promoCodeService.ValidatePromoCodeAsync(userId, request);

            return Ok(new
            {
                success = true,
                data = result,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating promo code");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Apply a promo code to an order
    /// </summary>
    [HttpPost("apply")]
    [Authorize]
    public async Task<IActionResult> ApplyPromoCode([FromBody] dynamic request)
    {
        try
        {
            var userId = User.GetUserId();
            
            // Parse request
            string code = request.code;
            int orderId = request.orderId;

            if (string.IsNullOrEmpty(code))
                return BadRequest(new { error = "Promo code is required" });

            var result = await _promoCodeService.ApplyPromoCodeAsync(userId, orderId, code);

            if (!result)
                return BadRequest(new { error = "Failed to apply promo code" });

            return Ok(new
            {
                success = true,
                message = "Promo code applied successfully",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying promo code");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get all active promo codes
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAllPromoCodes()
    {
        try
        {
            var promoCodes = await _promoCodeService.GetAllPromoCodesAsync();

            return Ok(new
            {
                success = true,
                data = promoCodes,
                count = promoCodes.Count,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting promo codes");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get promo code by code
    /// </summary>
    [HttpGet("{code}")]
    public async Task<IActionResult> GetPromoCodeByCode(string code)
    {
        try
        {
            var promoCode = await _promoCodeService.GetPromoCodeByCodeAsync(code);

            if (promoCode == null)
                return NotFound(new { error = "Promo code not found" });

            return Ok(new
            {
                success = true,
                data = promoCode,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting promo code");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Deactivate a promo code (admin only)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeactivatePromoCode(int id)
    {
        try
        {
            var result = await _promoCodeService.DeactivatePromoCodeAsync(id);

            if (!result)
                return NotFound(new { error = "Promo code not found" });

            return Ok(new
            {
                success = true,
                message = "Promo code deactivated successfully",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating promo code");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
