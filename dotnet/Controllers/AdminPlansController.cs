using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Services.Entitlements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

public sealed class AdminReasonBody
{
    public string? Reason { get; set; }
}

/// <summary>
/// Gestion des plans d'abonnement (lot 9.7b, décisions 18.10, 18.12, 18.13,
/// 18.19, 18.21). Ouverte à l'administrateur et au rôle dédié
/// « subscription_manager » (gestion des abonnements, 18.13). Chaque écriture
/// est inscrite au journal d'audit du 9.7a ; publication, archivage et octroi
/// exigent un motif.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = AuthorizationPolicies.SubscriptionManagement)]
public class AdminPlansController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly PlanCatalogService _plans;
    private readonly IEntitlementService _entitlements;
    private readonly IAdminAuditService _audit;

    public AdminPlansController(ApplicationDbContext db, PlanCatalogService plans, IEntitlementService entitlements, IAdminAuditService audit)
    {
        _db = db;
        _plans = plans;
        _entitlements = entitlements;
        _audit = audit;
    }

    private int ActorId => User.GetUserId();

    private IActionResult Fail(PlanServiceException ex) => StatusCode(ex.StatusCode, new { error = ex.Message });

    private static string? RequiredReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3 ? null : reason.Trim();

    /// <summary>Catalogue des droits (clés typées) pour l'éditeur.</summary>
    [HttpGet("plans/catalog")]
    public IActionResult Catalog() => Ok(new
    {
        periods = BillingPeriods.All,
        audiences = PlanAudiences.All,
        changeNoticeDays = PlanCatalogService.ChangeNoticeDays,
        entitlements = EntitlementCatalog.All.Select(d => new
        {
            d.Key,
            type = d.Type.ToString().ToLowerInvariant(),
            d.Unit,
            d.DefaultValue,
            d.Label,
            d.Description,
            d.Audiences,
            d.AllowUnlimited,
            d.ShowOnPricing,
            d.Group,
        }),
    });

    /// <summary>Familles de plans par public, versions, prix, droits et nombre d'abonnés par version.</summary>
    [HttpGet("plans")]
    public async Task<IActionResult> List([FromQuery] string? audience = null) =>
        Ok(await _plans.ListAsync(audience, HttpContext.RequestAborted));

    [HttpPost("plans")]
    public async Task<IActionResult> Create([FromBody] CreatePlanFamilyRequest req)
    {
        try
        {
            var plan = await _plans.CreateFamilyAsync(req, ActorId);
            await this.AuditAsync(_audit, AdminAuditActions.PlanCreated, "PricingPlan", plan.Id, plan.Name,
                after: new { plan.PlanKey, plan.Audience, plan.IsFreeTier });
            return Ok(new { id = plan.Id });
        }
        catch (PlanServiceException ex) { return Fail(ex); }
    }

    [HttpPut("plans/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePlanFamilyRequest req)
    {
        try
        {
            var plan = await _plans.UpdateFamilyAsync(id, req);
            await this.AuditAsync(_audit, AdminAuditActions.PlanUpdated, "PricingPlan", id, plan.Name, after: req);
            return Ok(new { id });
        }
        catch (PlanServiceException ex) { return Fail(ex); }
    }

    [HttpPost("plans/{id:int}/archive")]
    public async Task<IActionResult> Archive(int id, [FromBody] AdminReasonBody body) => await SetArchived(id, true, body);

    [HttpPost("plans/{id:int}/restore")]
    public async Task<IActionResult> Restore(int id, [FromBody] AdminReasonBody body) => await SetArchived(id, false, body);

    private async Task<IActionResult> SetArchived(int id, bool archived, AdminReasonBody body)
    {
        var reason = RequiredReason(body?.Reason);
        if (reason == null) return BadRequest(new { error = "Motif obligatoire." });
        try
        {
            var plan = await _plans.SetArchivedAsync(id, archived);
            await this.AuditAsync(_audit, archived ? AdminAuditActions.PlanArchived : AdminAuditActions.PlanRestored,
                "PricingPlan", id, plan.Name, reason: reason);
            return Ok(new { id, archived });
        }
        catch (PlanServiceException ex) { return Fail(ex); }
    }

    /// <summary>Ouvre (ou renvoie) le brouillon de la famille.</summary>
    [HttpPost("plans/{id:int}/versions")]
    public async Task<IActionResult> CreateDraft(int id)
    {
        try
        {
            var draft = await _plans.CreateDraftAsync(id, ActorId);
            await this.AuditAsync(_audit, AdminAuditActions.PlanVersionDrafted, "PlanVersion", draft.Id, $"plan {id} v{draft.VersionNumber}");
            return Ok(new { id = draft.Id, versionNumber = draft.VersionNumber });
        }
        catch (PlanServiceException ex) { return Fail(ex); }
    }

    [HttpPut("plan-versions/{versionId:int}")]
    public async Task<IActionResult> UpdateDraft(int versionId, [FromBody] UpdatePlanVersionRequest req)
    {
        try
        {
            var before = await _db.PlanVersions.AsNoTracking().Include(v => v.Prices).Include(v => v.Entitlements)
                .Where(v => v.Id == versionId)
                .Select(v => new
                {
                    prices = v.Prices.Select(p => new { p.Period, p.PriceTtc }),
                    entitlements = v.Entitlements.Select(e => new { e.Key, e.Value }),
                })
                .FirstOrDefaultAsync();
            var v = await _plans.UpdateDraftAsync(versionId, req);
            await this.AuditAsync(_audit, AdminAuditActions.PlanVersionUpdated, "PlanVersion", versionId,
                $"plan {v.PricingPlanId} v{v.VersionNumber}", before: before, after: req);
            return Ok(new { id = versionId });
        }
        catch (PlanServiceException ex) { return Fail(ex); }
    }

    [HttpDelete("plan-versions/{versionId:int}")]
    public async Task<IActionResult> DiscardDraft(int versionId)
    {
        try
        {
            await _plans.DiscardDraftAsync(versionId);
            await this.AuditAsync(_audit, AdminAuditActions.PlanVersionDiscarded, "PlanVersion", versionId);
            return Ok(new { id = versionId });
        }
        catch (PlanServiceException ex) { return Fail(ex); }
    }

    [HttpPost("plan-versions/{versionId:int}/publish")]
    public async Task<IActionResult> Publish(int versionId, [FromBody] AdminReasonBody body)
    {
        var reason = RequiredReason(body?.Reason);
        if (reason == null) return BadRequest(new { error = "Motif obligatoire." });
        try
        {
            var result = await _plans.PublishAsync(versionId, ActorId);
            await this.AuditAsync(_audit, AdminAuditActions.PlanVersionPublished, "PlanVersion", versionId, null,
                after: result, reason: reason);
            return Ok(result);
        }
        catch (PlanServiceException ex) { return Fail(ex); }
    }

    /// <summary>Aperçu de la carte publique d'une version (brouillon compris).</summary>
    [HttpGet("plan-versions/{versionId:int}/card")]
    public async Task<IActionResult> Card(int versionId)
    {
        try { return Ok(await _plans.BuildCardAsync(versionId, HttpContext.RequestAborted)); }
        catch (PlanServiceException ex) { return Fail(ex); }
    }

    /// <summary>Simulation : droits effectifs d'un compte, avec leur origine (fusion gratuit → plan → donateurs).</summary>
    [HttpGet("entitlements/simulate/{userId:int}")]
    public async Task<IActionResult> Simulate(int userId)
    {
        var exists = await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId);
        if (!exists) return NotFound(new { error = "Utilisateur introuvable." });
        var e = await _entitlements.ComputeAsync(userId, HttpContext.RequestAborted);
        return Ok(new
        {
            e.UserId,
            e.Role,
            e.Audience,
            plan = e.Plan,
            sources = e.Sources,
            aiMultiplier = e.AiMultiplier,
            ai = new { personal = e.PersonalReferenceTokens, parentBonus = e.ParentBonusTokens, licenseBonus = e.LicenseBonusTokens, free = e.FreeReferenceTokens },
            entitlements = e.Values.OrderBy(kv => kv.Key).Select(kv => new
            {
                key = kv.Key,
                value = kv.Value,
                label = EntitlementCatalog.Find(kv.Key)?.Label ?? kv.Key,
                display = EntitlementCatalog.Display(kv.Key, kv.Value, e.FreeReferenceTokens),
            }),
        });
    }

    /// <summary>
    /// Audit de bascule (lecture seule) : droits appliqués par l'ancien code
    /// comparés aux droits de la nouvelle grille après migration (étude 6.3).
    /// </summary>
    [HttpGet("entitlements/audit")]
    public async Task<IActionResult> Audit() =>
        Ok(await new EntitlementMigrationAudit(_db, _entitlements).RunAsync(HttpContext.RequestAborted));

    /// <summary>Octroi manuel d'un plan (contrôle du public, version publiée, motif obligatoire).</summary>
    [HttpPost("plans/grant")]
    public async Task<IActionResult> Grant([FromBody] GrantPlanRequest req)
    {
        var reason = RequiredReason(req?.Reason);
        if (req == null || reason == null) return BadRequest(new { error = "Motif obligatoire." });
        try
        {
            var sub = await _plans.GrantAsync(req);
            this.AuditAdd(_audit, AdminAuditActions.UserSubscriptionGranted, "User", req.UserId, null,
                after: new { planId = req.PlanId, sub.PlanName, sub.BillingPeriod, sub.EndDate }, reason: reason);
            await _db.SaveChangesAsync();
            _entitlements.Invalidate();
            return Ok(new { subscriptionId = sub.Id, endDate = sub.EndDate, message = $"Plan {sub.PlanName} accordé jusqu'au {sub.EndDate:dd/MM/yyyy}." });
        }
        catch (PlanServiceException ex) { return Fail(ex); }
    }

    /// <summary>Indicateurs d'abonnements (abonnés en vigueur par période, essais, défauts de paiement).</summary>
    [HttpGet("plans/stats")]
    public async Task<IActionResult> Stats()
    {
        var now = DateTime.UtcNow;
        var inForce = await _db.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(SubscriptionRules.InForce(now))
            .Select(s => new { s.Status, s.BillingPeriod, s.PlanVersionId })
            .ToListAsync();
        var all = await _db.Subscriptions.AsNoTracking().CountAsync();
        var ended = await _db.Subscriptions.AsNoTracking()
            .CountAsync(s => s.Status.ToLower() == SubscriptionStatuses.Cancelled || s.Status.ToLower() == SubscriptionStatuses.Expired);
        return Ok(new
        {
            active = inForce.Count,
            monthly = inForce.Count(s => s.BillingPeriod == BillingPeriods.Monthly),
            quarterly = inForce.Count(s => s.BillingPeriod == BillingPeriods.Quarterly),
            yearly = inForce.Count(s => s.BillingPeriod == BillingPeriods.Yearly),
            trialing = inForce.Count(s => s.Status.ToLower() == SubscriptionStatuses.Trialing),
            pastDue = inForce.Count(s => s.Status.ToLower() == SubscriptionStatuses.PastDue),
            legacy = inForce.Count(s => s.PlanVersionId == null),
            churnRate = all == 0 ? 0 : Math.Round(100.0 * ended / all, 1),
        });
    }
}
