using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Services.Entitlements;

namespace Backend.Controllers;

public sealed class SubscriptionQuoteRequest
{
    public int PlanId { get; set; }
    public string? BillingPeriod { get; set; }
    public string? PromoCode { get; set; }
}

public sealed class AutoRenewRequest
{
    public bool Enabled { get; set; }
}

/// <summary>
/// Abonnements de l'utilisateur (lot 9.7b, décisions 18.12, 18.14, 18.15,
/// 18.19, 18.20). Toute la logique (prix, remises, essais, prorata, descente,
/// résiliation, renouvellement) est dans <see cref="SubscriptionLifecycleService"/> ;
/// ce contrôleur ne fait que la transporter.
///
/// Retiré au 9.7b : POST /api/subscriptions (souscription directe d'un plan
/// gratuit : le plan gratuit est l'état par défaut, sans ligne d'abonnement)
/// et le calcul de prix « plan.Price × période » (défaut 0.2 point 4 : le plan
/// Annuel était facturé 863 040 F sur un an).
/// </summary>
[ApiController]
[Route("api/subscriptions")]
[Authorize]
public class SubscriptionsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SubscriptionsController> _logger;
    private readonly IPaymentService _paymentService;
    private readonly IAiQuotaService _aiQuota;
    private readonly SubscriptionLifecycleService _lifecycle;
    private readonly IEntitlementService _entitlements;

    public SubscriptionsController(
        ApplicationDbContext db,
        ILogger<SubscriptionsController> logger,
        IPaymentService paymentService,
        IAiQuotaService aiQuota,
        SubscriptionLifecycleService lifecycle,
        IEntitlementService entitlements)
    {
        _db = db;
        _logger = logger;
        _paymentService = paymentService;
        _aiQuota = aiQuota;
        _lifecycle = lifecycle;
        _entitlements = entitlements;
    }

    private IActionResult Fail(SubscriptionLifecycleException ex) => StatusCode(ex.StatusCode, new { error = ex.Message });

    /// <summary>GET /api/subscriptions/me : abonnement en vigueur, cycle et droits effectifs.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrent()
    {
        try
        {
            var userId = User.GetUserId();
            var ent = await _entitlements.GetAsync(userId, HttpContext.RequestAborted);

            // 8.3 / 8.10 : usage WinAI en relatif, jamais en tokens. Ne doit
            // jamais faire tomber cette route (point C).
            AiQuotaSnapshot? ai = null;
            try { ai = await _aiQuota.GetSnapshotAsync(userId, HttpContext.RequestAborted); }
            catch (Exception aiEx) when (aiEx is not OperationCanceledException)
            {
                _logger.LogWarning(aiEx, "État du quota WinAI indisponible pour {UserId} : valeurs par défaut renvoyées", userId);
            }
            var multiplier = ai?.Multiplier ?? ent.AiMultiplier;
            var limitReached = ai?.LimitReached;
            var limitResetsAt = ai?.ResetsAt;

            var sub = ent.Plan == null ? null : await _db.Subscriptions.AsNoTracking()
                .Where(s => s.Id == ent.Plan.SubscriptionId)
                .Select(s => new
                {
                    s.AutoRenew, s.CancelAtPeriodEnd, s.PricePaidTtc, s.PendingPricingPlanId, s.PendingBillingPeriod,
                    s.TrialEndsAt, s.CurrentPeriodStart,
                })
                .FirstOrDefaultAsync();
            var pendingName = sub?.PendingPricingPlanId is int pp
                ? await _db.PricingPlans.IgnoreQueryFilters().Where(p => p.Id == pp).Select(p => p.Name).FirstOrDefaultAsync()
                : null;

            return Ok(new
            {
                id = ent.Plan?.SubscriptionId ?? 0,
                hasPlan = ent.Plan != null,
                pricingPlanId = ent.Plan?.PricingPlanId,
                planKey = ent.Plan?.PlanKey,
                planName = ent.Plan?.PlanName ?? "Gratuit",
                audience = ent.Audience,
                status = ent.Plan?.Status ?? "free",
                billingPeriod = ent.Plan?.BillingPeriod,
                startDate = ent.Plan?.StartDate,
                currentPeriodStart = sub?.CurrentPeriodStart,
                expiresAt = ent.Plan?.EndDate,
                graceUntil = ent.Plan?.GraceUntil,
                trialEndsAt = sub?.TrialEndsAt,
                autoRenew = sub?.AutoRenew ?? false,
                cancelAtPeriodEnd = sub?.CancelAtPeriodEnd ?? false,
                price = sub?.PricePaidTtc,
                pendingPlanId = sub?.PendingPricingPlanId,
                pendingPlanName = pendingName,
                pendingBillingPeriod = sub?.PendingBillingPeriod,
                entitlements = MeEntitlementsController.ToResponse(ent),
                aiUsageMultiplier = multiplier,
                aiUsageLabel = multiplier <= 1 ? "Usage WinAI du plan gratuit" : $"{multiplier}x plus d'usage WinAI que le plan gratuit",
                aiQuotaExhausted = limitReached is not null,
                aiHasParentBonus = ent.ParentBonusTokens > 0,
                aiHasInstitutionBonus = ent.LicenseBonusTokens > 0,
                aiLimitReached = limitReached switch { AiLimitKind.Session => "session", AiLimitKind.Week => "week", _ => null },
                aiLimitResetsAt = limitResetsAt,
                aiUsageResetAt = limitResetsAt,
                aiLimitMessage = limitReached is AiLimitKind kind ? AiUsagePolicy.LimitMessage(kind, limitResetsAt) : null,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current subscription");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>POST /api/subscriptions/quote : prix réellement facturé (remise, essai, prorata) avant paiement.</summary>
    [HttpPost("quote")]
    public async Task<IActionResult> Quote([FromBody] SubscriptionQuoteRequest req)
    {
        try { return Ok(await _lifecycle.QuoteAsync(User.GetUserId(), req.PlanId, req.BillingPeriod, req.PromoCode, HttpContext.RequestAborted)); }
        catch (SubscriptionLifecycleException ex) { return Fail(ex); }
    }

    /// <summary>
    /// POST /api/subscriptions/purchase : souscription, montée, descente ou
    /// essai. method = balance (solde) ou mobile_money (NotchPay, numéro requis).
    /// </summary>
    [HttpPost("purchase")]
    public async Task<IActionResult> Purchase([FromBody] SubscriptionPurchaseRequest req)
    {
        var userId = User.GetUserId();
        try
        {
            var method = (req.Method ?? (string.IsNullOrWhiteSpace(req.Phone) ? "balance" : "mobile_money")).ToLowerInvariant();
            if (method is not ("balance" or "mobile_money"))
                return BadRequest(new { error = "Moyen de paiement invalide (balance ou mobile_money)." });
            req.Method = method;
            if (method == "mobile_money" && string.IsNullOrWhiteSpace(req.Phone))
            {
                var quote = await _lifecycle.QuoteAsync(userId, req.PlanId, req.BillingPeriod, req.PromoCode, HttpContext.RequestAborted);
                if (quote.AmountDueTtc > 0) return BadRequest(new { error = "Numéro Mobile Money requis." });
            }

            var result = await _lifecycle.PurchaseAsync(userId, req, HttpContext.RequestAborted);
            if (result.Status != "payment_pending")
                return Ok(new { status = result.Status, subscriptionId = result.SubscriptionId, orderId = result.OrderId, amount = result.AmountTtc, currency = "XAF", quote = result.Quote, message = result.Message });

            var payment = await _paymentService.InitiateNotchPayAsync(userId, new InitiatePaymentRequest
            {
                OrderId = result.OrderId!.Value,
                Phone = req.Phone!,
                Amount = result.AmountTtc,
                Description = $"Abonnement {result.Quote.PlanName} ({BillingPeriods.Label(result.Quote.BillingPeriod)})",
            });
            return Ok(new { status = result.Status, orderId = result.OrderId, paymentId = payment.PaymentId, amount = result.AmountTtc, currency = "XAF", quote = result.Quote, message = result.Message });
        }
        catch (SubscriptionLifecycleException ex) { return Fail(ex); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating subscription purchase for {UserId}", userId);
            return StatusCode(500, new { error = "Erreur lors du paiement de l'abonnement" });
        }
    }

    /// <summary>POST /api/subscriptions/me/cancel : résiliation en fin de période (accès conservé, sans remboursement partiel).</summary>
    [HttpPost("me/cancel")]
    public async Task<IActionResult> Cancel()
    {
        try
        {
            var sub = await _lifecycle.CancelAsync(User.GetUserId(), HttpContext.RequestAborted);
            return Ok(new { success = true, accessUntil = sub.EndDate, message = $"Résilié : vous gardez l'accès jusqu'au {sub.EndDate:dd/MM/yyyy}, sans remboursement de la période en cours." });
        }
        catch (SubscriptionLifecycleException ex) { return Fail(ex); }
    }

    /// <summary>POST /api/subscriptions/me/resume : annule la résiliation avant l'échéance.</summary>
    [HttpPost("me/resume")]
    public async Task<IActionResult> Resume()
    {
        try { await _lifecycle.ResumeAsync(User.GetUserId(), HttpContext.RequestAborted); return Ok(new { success = true }); }
        catch (SubscriptionLifecycleException ex) { return Fail(ex); }
    }

    /// <summary>PUT /api/subscriptions/me/auto-renew : renouvellement automatique par le solde (3 jours de grâce si le solde manque).</summary>
    [HttpPut("me/auto-renew")]
    public async Task<IActionResult> AutoRenew([FromBody] AutoRenewRequest req)
    {
        try { var s = await _lifecycle.SetAutoRenewAsync(User.GetUserId(), req.Enabled, HttpContext.RequestAborted); return Ok(new { success = true, autoRenew = s.AutoRenew }); }
        catch (SubscriptionLifecycleException ex) { return Fail(ex); }
    }

    /// <summary>DELETE /api/subscriptions/me/pending : annule une descente de plan programmée.</summary>
    [HttpDelete("me/pending")]
    public async Task<IActionResult> CancelPending()
    {
        try { await _lifecycle.CancelPendingChangeAsync(User.GetUserId(), HttpContext.RequestAborted); return Ok(new { success = true }); }
        catch (SubscriptionLifecycleException ex) { return Fail(ex); }
    }
}
