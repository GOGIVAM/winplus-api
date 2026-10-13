using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Administration du programme d'affiliation.
/// GET  /api/admin/affiliate/settings
/// PUT  /api/admin/affiliate/settings
/// GET  /api/admin/affiliate/accounts
/// PUT  /api/admin/affiliate/accounts/{id}/status
/// POST /api/admin/affiliate/recalculate-rates
///
/// Lot 9.7a (18.1) : politique AdminOnly à la place de la vérification de
/// rôle « faite main » ; modification des réglages et changement de statut
/// d'un affilié inscrits au journal d'audit (le changement de statut exige un motif).
/// </summary>
[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin/affiliate")]
public class AdminAffiliateController : ControllerBase
{
    private readonly IAffiliateService _affiliate;
    private readonly ILogger<AdminAffiliateController> _logger;
    private readonly IAdminAuditService _audit;

    public AdminAffiliateController(IAffiliateService affiliate, ILogger<AdminAffiliateController> logger, IAdminAuditService audit)
    {
        _affiliate = affiliate;
        _logger = logger;
        _audit = audit;
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings() => Ok(await _affiliate.GetSettingsAsync());

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateAffiliateSettingsRequest request)
    {
        try
        {
            var before = await _affiliate.GetSettingsAsync();
            var after = await _affiliate.UpdateSettingsAsync(request);
            await this.AuditAsync(_audit, AdminAuditActions.AffiliateSettingsChanged, "AffiliateSettings", 1, null, before, after);
            return Ok(after);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la mise à jour des réglages d'affiliation");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    [HttpGet("accounts")]
    public async Task<IActionResult> ListAccounts() => Ok(await _affiliate.ListAccountsAsync());

    [HttpPut("accounts/{id:int}/status")]
    public async Task<IActionResult> SetAccountStatus(int id, [FromBody] SetAffiliateStatusRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < AdminUsersController.MinReasonLength)
            return BadRequest(new { code = "reason_required", error = $"Un motif d'au moins {AdminUsersController.MinReasonLength} caractères est obligatoire." });

        var ok = await _affiliate.SetAccountStatusAsync(id, request.Status);
        if (!ok) return BadRequest(new { error = "Statut invalide ou compte introuvable" });
        await this.AuditAsync(_audit, AdminAuditActions.AffiliateStatusChanged, "AffiliateAccount", id,
            after: new { status = request.Status }, reason: request.Reason);
        return Ok(new { success = true });
    }

    [HttpPost("recalculate-rates")]
    public async Task<IActionResult> RecalculateRatesNow(CancellationToken ct)
    {
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

public record SetAffiliateStatusRequest(string Status, string? Reason = null);
