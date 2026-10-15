using Backend.Extensions;
using Backend.Services.Entitlements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// GET /api/me/entitlements (lot 9.7b) : droits effectifs du compte connecté.
/// Web et mobile ne testent plus jamais un nom de plan : ils lisent ces clés.
/// Les quotas WinAI ne sont jamais exposés en tokens (8.3) : seul le
/// multiplicateur l'est, et la clé ai.reference_tokens n'est pas renvoyée.
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
public class MeEntitlementsController : ControllerBase
{
    private static readonly HashSet<string> InternalKeys = new(StringComparer.Ordinal)
    {
        EntitlementCatalog.AiReferenceTokens,
        EntitlementCatalog.AiParentBonusPerChild,
        EntitlementCatalog.AiParentBonusShares,
    };

    private readonly IEntitlementService _entitlements;

    public MeEntitlementsController(IEntitlementService entitlements) => _entitlements = entitlements;

    [HttpGet("entitlements")]
    public async Task<IActionResult> Get()
    {
        var e = await _entitlements.GetAsync(User.GetUserId(), HttpContext.RequestAborted);
        return Ok(ToResponse(e));
    }

    public static object ToResponse(UserEntitlements e)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, raw) in e.Values)
        {
            if (InternalKeys.Contains(key)) continue;
            var def = EntitlementCatalog.Find(key);
            if (def == null) continue;
            values[key] = def.Type == EntitlementType.Boolean ? EntitlementCatalog.AsBool(raw) : EntitlementCatalog.AsLong(raw);
        }
        return new
        {
            audience = e.Audience,
            plan = e.Plan == null ? null : new
            {
                subscriptionId = e.Plan.SubscriptionId,
                planId = e.Plan.PricingPlanId,
                planKey = e.Plan.PlanKey,
                name = e.Plan.PlanName,
                versionId = e.Plan.PlanVersionId,
                billingPeriod = e.Plan.BillingPeriod,
                status = e.Plan.Status,
                startDate = e.Plan.StartDate,
                endDate = e.Plan.EndDate,
                graceUntil = e.Plan.GraceUntil,
            },
            aiMultiplier = e.AiMultiplier,
            aiHasParentBonus = e.ParentBonusTokens > 0,
            aiHasLicenseBonus = e.LicenseBonusTokens > 0,
            sources = e.Sources.Select(s => new { s.Kind, s.Label }),
            unlimited = EntitlementCatalog.Unlimited,
            entitlements = values,
        };
    }
}
