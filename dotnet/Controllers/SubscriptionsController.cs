using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Models.DTOs;
using Backend.Services;
using System.Text.Json;

namespace Backend.Controllers;

public record SubscribeRequest(int PlanId, string Billing = "monthly");
public record PurchaseSubscriptionRequest(int PlanId, string Phone, string Billing = "monthly");

[ApiController]
[Route("api/subscriptions")]
[Authorize]
public class SubscriptionsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SubscriptionsController> _logger;
    private readonly IPaymentService _paymentService;
    private readonly IAiQuotaService _aiQuota;

    public SubscriptionsController(
        ApplicationDbContext db,
        ILogger<SubscriptionsController> logger,
        IPaymentService paymentService,
        IAiQuotaService aiQuota)
    {
        _db = db;
        _logger = logger;
        _paymentService = paymentService;
        _aiQuota = aiQuota;
    }

    /// <summary>GET /api/subscriptions/me  abonnement actif de l'utilisateur connecté.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrent()
    {
        try
        {
            var userId = User.GetUserId();
            var sub = await _db.Subscriptions
                .AsNoTracking()
                .Include(s => s.PricingPlan)
                .Where(s => s.UserId == userId && !s.IsDeleted && s.Status.ToLower() == "active")
                .OrderByDescending(s => s.StartDate)
                .FirstOrDefaultAsync();

            // Partie 8.3 / 8.10 : l'usage WinAI est exposé sous forme RELATIVE
            // (multiplicateur par rapport au plan gratuit) et par ÉTAT des
            // limites (quelle limite est atteinte, quand elle se réinitialise),
            // jamais en nombre brut de tokens. Le calcul est celui qu'applique
            // réellement le chat (IAiQuotaService), bonus parent/établissement
            // compris (8.6).
            //
            // Point C : cette route ne doit jamais tomber à cause du quota IA.
            // GetSnapshotAsync tolère déjà l'absence de la table du journal ;
            // toute autre erreur est rattrapée ici et donne l'état par défaut.
            AiQuotaSnapshot? ai = null;
            try
            {
                ai = await _aiQuota.GetSnapshotAsync(userId, HttpContext.RequestAborted);
            }
            catch (Exception aiEx) when (aiEx is not OperationCanceledException)
            {
                _logger.LogWarning(aiEx, "État du quota WinAI indisponible pour {UserId} : valeurs par défaut renvoyées", userId);
            }

            var multiplier = ai?.Multiplier ?? 1;
            var limitReached = ai?.LimitReached;
            var limitResetsAt = ai?.ResetsAt;
            var aiUsage = new
            {
                aiUsageMultiplier = multiplier,
                aiUsageLabel = multiplier <= 1
                    ? "Usage WinAI du plan gratuit"
                    : $"{multiplier}x plus d'usage WinAI que le plan gratuit",
                aiQuotaExhausted = limitReached is not null,
                aiHasParentBonus = (ai?.ParentBonusTokens ?? 0) > 0,
                aiHasInstitutionBonus = (ai?.InstitutionBonusTokens ?? 0) > 0,
                // 8.10 : "session" (5 h glissantes), "week" (7 jours glissants)
                // ou null si aucune limite n'est atteinte.
                aiLimitReached = limitReached switch
                {
                    AiLimitKind.Session => "session",
                    AiLimitKind.Week => "week",
                    _ => null,
                },
                // Heure (UTC) à laquelle l'utilisateur pourra de nouveau
                // écrire ; null si aucune limite n'est atteinte. Le champ
                // historique aiUsageResetAt porte la même valeur (il désignait
                // le début du mois suivant, qui n'a plus de sens depuis 8.10).
                aiLimitResetsAt = limitResetsAt,
                aiUsageResetAt = limitResetsAt,
                aiLimitMessage = limitReached is AiLimitKind kind
                    ? AiUsagePolicy.LimitMessage(kind, limitResetsAt)
                    : null,
            };

            if (sub == null)
            {
                return Ok(new
                {
                    id = 0,
                    planName = "Libre",
                    tier = "free",
                    status = "active",
                    expiresAt = DateTime.UtcNow.AddYears(10),
                    autoRenew = false,
                    downloadsUsed = 0,
                    downloadsLimit = 5,
                    quizUsedToday = 0,
                    quizDailyLimit = 3,
                    // Les anciens champs aiMessagesUsed/aiMessagesLimit
                    // (compteurs bruts) sont retirés : voir aiUsage.
                    aiUsage.aiUsageMultiplier,
                    aiUsage.aiUsageLabel,
                    aiUsage.aiQuotaExhausted,
                    aiUsage.aiHasParentBonus,
                    aiUsage.aiHasInstitutionBonus,
                    aiUsage.aiUsageResetAt,
                    aiUsage.aiLimitReached,
                    aiUsage.aiLimitResetsAt,
                    aiUsage.aiLimitMessage,
                });
            }

            var plan = sub.PricingPlan;
            var effectivePlanName = sub.PlanName ?? plan?.Name;
            return Ok(new
            {
                id = sub.Id,
                // Requis pour renouveler : POST /subscriptions/purchase prend un
                // PlanId (PricingPlan), pas l'id de la Subscription elle-même.
                pricingPlanId = sub.PricingPlanId,
                planName = effectivePlanName ?? "Standard",
                tier = effectivePlanName?.ToLower() ?? "standard",
                status = sub.Status,
                expiresAt = sub.EndDate ?? sub.StartDate.AddMonths(1),
                autoRenew = sub.EndDate == null,
                price = plan?.Price ?? 0,
                downloadsUsed = 0,
                downloadsLimit = plan?.MaxDownloads ?? 30,
                quizUsedToday = 0,
                quizDailyLimit = 20,
                aiUsage.aiUsageMultiplier,
                aiUsage.aiUsageLabel,
                aiUsage.aiQuotaExhausted,
                aiUsage.aiHasParentBonus,
                aiUsage.aiHasInstitutionBonus,
                aiUsage.aiUsageResetAt,
                aiUsage.aiLimitReached,
                aiUsage.aiLimitResetsAt,
                aiUsage.aiLimitMessage,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current subscription");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// POST /api/subscriptions  souscrire à un plan.
    ///
    /// ⚠ Correction §7.1 du suivi. Cet endpoint créait une ligne
    /// <c>Subscriptions</c> avec <c>Status = "active"</c> sans la moindre
    /// vérification de paiement : n'importe quel compte authentifié pouvait
    /// s'offrir le plan le plus cher d'un appel, ce qui contournait en entier
    /// le mur payant du Module 17 (lequel n'accorde l'accès au contenu payant
    /// qu'à l'appui, notamment, d'un abonnement actif).
    ///
    /// Il n'a pas été supprimé parce qu'il a deux appelants réels la modale
    /// d'abonnement web (<c>SubscribeModal.tsx</c>, appelée après confirmation
    /// du paiement pour « activer ») et l'écran de tarifs mobile
    /// (<c>pricing_screen.dart</c> via <c>SubscriptionService.subscribe</c>).
    /// Il ne crée plus rien pour un plan payant : il se contente de rendre
    /// l'abonnement que la confirmation de paiement a créé (Module 18, §7.3),
    /// ce qui garde la modale web fonctionnelle, et refuse explicitement le
    /// cas où aucun paiement n'a été confirmé. Seul un plan réellement gratuit
    /// (prix nul) reste directement souscriptible.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest req)
    {
        try
        {
            var userId = User.GetUserId();
            var plan = await _db.PricingPlans.FindAsync(req.PlanId);
            if (plan == null) return NotFound(new { error = "Plan introuvable" });

            if (!SubscriptionOrderIntent.IsKnownBilling(req.Billing))
                return BadRequest(new { error = "Période de facturation invalide (monthly, quarterly ou yearly)." });

            // Plan payant : aucune activation directe. L'abonnement ne peut
            // venir que de la confirmation d'un paiement.
            if (plan.Price > 0)
            {
                var activated = await _db.Subscriptions.AsNoTracking()
                    .Where(s => s.UserId == userId
                             && s.PricingPlanId == req.PlanId
                             && !s.IsDeleted
                             && s.Status.ToLower() == "active")
                    .OrderByDescending(s => s.StartDate)
                    .FirstOrDefaultAsync();

                if (activated != null)
                    return Ok(new { success = true, subscriptionId = activated.Id, alreadyActive = true });

                _logger.LogWarning(
                    "Activation directe refusée : l'utilisateur {UserId} demande le plan payant {PlanId} sans paiement confirmé",
                    userId, req.PlanId);

                return StatusCode(402, new
                {
                    error = "Ce plan est payant : l'abonnement est activé automatiquement "
                          + "à la confirmation du paiement. Utilisez POST /api/subscriptions/purchase.",
                    requiresPayment = true,
                });
            }

            // Plan gratuit : souscription directe légitime, rien n'est à
            // encaisser. Idempotent, pour qu'un double appel ne produise pas
            // deux lignes actives.
            //
            // ⚠ Régression corrigée : la résiliation portait sur TOUS les
            // abonnements actifs de l'utilisateur, sans regarder le prix de
            // leur plan. Souscrire au plan gratuit résiliait donc un
            // abonnement PAYANT en cours, silencieusement. On ne résilie plus
            // que ce qui est lui-même gratuit (Price == 0) ou déjà expiré ; un
            // abonnement payant encore en cours de validité fait refuser la
            // bascule (409) au lieu d'être annulé.
            var now = DateTime.UtcNow;

            var existingActive = await _db.Subscriptions
                .Where(s => s.UserId == userId && s.Status.ToLower() == "active" && !s.IsDeleted)
                .ToListAsync();

            var alreadyOnThisPlan = existingActive.FirstOrDefault(s => s.PricingPlanId == req.PlanId);
            if (alreadyOnThisPlan != null)
                return Ok(new { success = true, subscriptionId = alreadyOnThisPlan.Id, alreadyActive = true });

            // Prix du plan porté par chacun de ces abonnements : c'est la seule
            // façon de distinguer un abonnement gratuit d'un abonnement payé.
            //
            // IgnoreQueryFilters : un plan payant archivé (suppression logique)
            // reste le plan réellement payé par l'abonné. Filtré, il disparaissait
            // du dictionnaire, l'abonnement n'était plus reconnu comme payant et
            // la bascule vers le plan gratuit le résiliait (passe de clôture du
            // lot 0, même règle que ContentAccessService.HasActiveSubscriptionAsync).
            var activePlanIds = existingActive.Select(s => s.PricingPlanId).Distinct().ToList();
            var activePlanPrices = await _db.PricingPlans.IgnoreQueryFilters().AsNoTracking()
                .Where(p => activePlanIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Price);

            var paidInForce = existingActive.FirstOrDefault(s =>
                activePlanPrices.TryGetValue(s.PricingPlanId, out var price)
                && price > 0
                && (!s.EndDate.HasValue || s.EndDate.Value > now));

            if (paidInForce != null)
            {
                _logger.LogWarning(
                    "Bascule vers le plan gratuit refusée : l'utilisateur {UserId} a l'abonnement payant actif {SubscriptionId}",
                    userId, paidInForce.Id);

                return Conflict(new
                {
                    error = "Vous avez déjà un abonnement payant en cours. "
                          + "Il n'est pas remplacé par le plan gratuit : attendez son échéance "
                          + "ou résiliez-le avant de souscrire au plan gratuit.",
                    hasPaidSubscription = true,
                    subscriptionId = paidInForce.Id,
                });
            }

            // Ne restent ici que des abonnements gratuits ou déjà expirés.
            foreach (var s in existingActive)
            {
                s.Status = "cancelled";
                s.EndDate = now;
                s.IsActive = false;
                s.UpdatedAt = now;
            }

            var intent = new SubscriptionOrderIntent(req.PlanId, req.Billing.ToLowerInvariant());
            var newSub = new Subscription
            {
                UserId = userId,
                PricingPlanId = req.PlanId,
                PlanName = plan.Name,
                StartDate = now,
                EndDate = now.AddMonths(intent.DurationMonths),
                Status = "active",
                IsActive = true,
            };
            _db.Subscriptions.Add(newSub);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, subscriptionId = newSub.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>POST /api/subscriptions/purchase  initie un paiement NotchPay pour un abonnement.</summary>
    [HttpPost("purchase")]
    public async Task<IActionResult> Purchase([FromBody] PurchaseSubscriptionRequest req)
    {
        try
        {
            var userId = User.GetUserId();
            var plan = await _db.PricingPlans.FindAsync(req.PlanId);
            if (plan == null) return NotFound(new { error = "Plan introuvable" });

            // Période refusée plutôt que ramenée silencieusement au mois :
            // l'ancien `switch` facturait le mois pour toute valeur inconnue,
            // alors que l'intention relue à la confirmation (§7.3) doit
            // décrire exactement ce qui a été payé.
            if (!SubscriptionOrderIntent.IsKnownBilling(req.Billing))
                return BadRequest(new { error = "Période de facturation invalide (monthly, quarterly ou yearly)." });

            var billing = req.Billing.ToLowerInvariant();
            var baseAmount = plan.Price;
            var amount = billing switch
            {
                "yearly"    => Math.Round(baseAmount * 12 * 0.8m),
                "quarterly" => Math.Round(baseAmount * 3 * 0.9m),
                _           => baseAmount,
            };

            // Créer un order de type subscription. L'intention d'abonnement
            // (plan + période) est écrite par le type dédié, seul endroit qui
            // compose ce format, et seul endroit qui le relit à la
            // confirmation du paiement (SubscriptionActivationService).
            //
            // Statut en minuscules : la casse « Pending » ressortait de la
            // normalisation du Module 19 comme une valeur distincte pour
            // toutes les comparaisons non corrigées.
            var order = new Order
            {
                UserId = userId,
                OrderNumber = $"SUB-{userId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                TotalAmount = amount,
                Status = "pending",
                PaymentMethod = "notchpay",
                Notes = new SubscriptionOrderIntent(req.PlanId, billing).ToString(),
            };
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            var payReq = new InitiatePaymentRequest
            {
                OrderId = order.Id,
                Phone = req.Phone,
                Amount = amount,
                Description = $"Abonnement {plan.Name} ({req.Billing})",
            };
            var result = await _paymentService.InitiateNotchPayAsync(userId, payReq);

            return Ok(new { paymentId = result.PaymentId, amount, currency = "XAF" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating subscription purchase");
            return StatusCode(500, new { error = "Erreur lors du paiement de l'abonnement" });
        }
    }

    /// <summary>POST /api/subscriptions/me/cancel  résilier l'abonnement actif.</summary>
    [HttpPost("me/cancel")]
    public async Task<IActionResult> Cancel()
    {
        try
        {
            var userId = User.GetUserId();
            var sub = await _db.Subscriptions
                .Where(s => s.UserId == userId && s.Status.ToLower() == "active" && !s.IsDeleted)
                .OrderByDescending(s => s.StartDate)
                .FirstOrDefaultAsync();

            if (sub == null) return NotFound(new { error = "Aucun abonnement actif" });

            sub.Status = "cancelled";
            sub.EndDate = DateTime.UtcNow;
            sub.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling subscription");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
