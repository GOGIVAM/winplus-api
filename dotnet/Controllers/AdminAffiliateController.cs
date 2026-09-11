using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Administration du programme d'affiliation.
/// GET  /api/admin/affiliate/settings
/// PUT  /api/admin/affiliate/settings
/// GET  /api/admin/affiliate/accounts
/// PUT  /api/admin/affiliate/accounts/{id}/status
/// POST /api/admin/affiliate/recalculate-rates
/// </summary>
[ApiController]
[Authorize]
[Route("api/admin/affiliate")]
public class AdminAffiliateController : ControllerBase
{
    private readonly IAffiliateService _affiliate;
    private readonly ILogger<AdminAffiliateController> _logger;

    public AdminAffiliateController(IAffiliateService affiliate, ILogger<AdminAffiliateController> logger)
    {
        _affiliate = affiliate;
        _logger = logger;
    }

    private IActionResult? ForbidIfNotAdmin()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
                ?? User.FindFirst("role")?.Value;
        return role == "admin" ? null : Forbid();
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        if (ForbidIfNotAdmin() is { } guard) return guard;
        return Ok(await _affiliate.GetSettingsAsync());
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateAffiliateSettingsRequest request)
    {
        if (ForbidIfNotAdmin() is { } guard) return guard;
        try
        {
            return Ok(await _affiliate.UpdateSettingsAsync(request));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la mise à jour des réglages d'affiliation");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    [HttpGet("accounts")]
    public async Task<IActionResult> ListAccounts()
    {
        if (ForbidIfNotAdmin() is { } guard) return guard;
        return Ok(await _affiliate.ListAccountsAsync());
    }

    [HttpPut("accounts/{id:int}/status")]
    public async Task<IActionResult> SetAccountStatus(int id, [FromBody] SetAffiliateStatusRequest request)
    {
        if (ForbidIfNotAdmin() is { } guard) return guard;
        var ok = await _affiliate.SetAccountStatusAsync(id, request.Status);
        return ok ? Ok(new { success = true }) : BadRequest(new { error = "Statut invalide ou compte introuvable" });
    }

    [HttpPost("recalculate-rates")]
    public async Task<IActionResult> RecalculateRatesNow(CancellationToken ct)
    {
        if (ForbidIfNotAdmin() is { } guard) return guard;
        try
        {
            await _affiliate.RecalculateAllRatesAsync(ct);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du recalcul manuel des taux d'affiliation");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }
}

public record SetAffiliateStatusRequest(string Status);
