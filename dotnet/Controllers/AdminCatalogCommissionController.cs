using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Extensions;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Module 7, §7B : consultation et modification de la grille de commission
/// catalogue par un administrateur. Les bornes restent toujours contenues
/// dans l'intervalle 10%-60% fixé par le product owner, quelle que soit la
/// valeur envoyée (CatalogCommissionService.UpdateSettingsAsync les borne).
/// Un ajustement n'a d'effet que sur les ventes futures : les contenus déjà
/// évalués gardent leur PlatformCommissionRate figé, et les ventes déjà
/// enregistrées dans le journal ne sont jamais recalculées.
/// </summary>
[ApiController]
[Route("api/admin/catalog-commission")]
[Authorize(Policy = "AdminOnly")]
public class AdminCatalogCommissionController : ControllerBase
{
    private readonly ICatalogCommissionService _commission;
    private readonly ILogger<AdminCatalogCommissionController> _logger;
    private readonly IAdminAuditService _audit;

    public AdminCatalogCommissionController(ICatalogCommissionService commission, ILogger<AdminCatalogCommissionController> logger,
        IAdminAuditService audit)
    {
        _commission = commission;
        _logger = logger;
        _audit = audit;
    }

    public record UpdateCommissionRequest(decimal MinRatePercent, decimal MaxRatePercent);

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var s = await _commission.GetSettingsAsync();
        return Ok(new
        {
            s.MinRatePercent,
            s.MaxRatePercent,
            s.UpdatedAt,
            s.UpdatedByUserId,
            absoluteMinPercent = CatalogCommissionService.AbsoluteMinPercent,
            absoluteMaxPercent = CatalogCommissionService.AbsoluteMaxPercent,
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateCommissionRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            var current = await _commission.GetSettingsAsync();
            var before = new { current.MinRatePercent, current.MaxRatePercent };
            var s = await _commission.UpdateSettingsAsync(request.MinRatePercent, request.MaxRatePercent, userId);
            // Lot 9.7a (18.3, 18.6) : la commission du catalogue est un réglage sensible, journalisé.
            await this.AuditAsync(_audit, Backend.Models.Entities.AdminAuditActions.CatalogCommissionChanged, "CatalogCommission", 1,
                "Commission du catalogue", before, new { s.MinRatePercent, s.MaxRatePercent });
            return Ok(new { s.MinRatePercent, s.MaxRatePercent, s.UpdatedAt });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la mise à jour de la grille de commission catalogue");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
