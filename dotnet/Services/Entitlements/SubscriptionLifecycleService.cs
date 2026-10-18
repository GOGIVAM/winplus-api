using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Entitlements;

/// <summary>Nature d'un achat d'abonnement (18.15).</summary>
public static class SubscriptionChanges
{
    /// <summary>Premier abonnement (ou après expiration).</summary>
    public const string New = "new";
    /// <summary>Même plan et même période : une période de plus, ajoutée après l'échéance en cours.</summary>
    public const string Renew = "renew";
    /// <summary>Plan plus cher (au mois) : immédiat, au prorata du temps restant.</summary>
    public const string Upgrade = "upgrade";
    /// <summary>Plan moins cher ou autre période : programmé à l'échéance, rien à payer maintenant.</summary>
    public const string Downgrade = "downgrade";
    /// <summary>Essai gratuit ouvert par une promotion (18.14).</summary>
    public const string Trial = "trial";
}

public sealed record SubscriptionQuote(
    int PlanId,
    string PlanName,
    string Audience,
    int VersionId,
    string BillingPeriod,
    string Change,
    decimal ListPriceTtc,
    decimal DiscountTtc,
    decimal ProrataCreditTtc,
    decimal AmountDueTtc,
    DateTime StartsAt,
    DateTime EndsAt,
    int? TrialDays,
    int? PromoCodeId,
    string? PromoCode,
    string? PromoError,
    string Message);

public sealed class SubscriptionPurchaseRequest
{
    public int PlanId { get; set; }
    public string? BillingPeriod { get; set; }
    /// <summary>balance (solde du portefeuille) ou mobile_money.</summary>
    public string? Method { get; set; }
    public string? Phone { get; set; }
    public string? PromoCode { get; set; }
    public bool AutoRenew { get; set; }
    /// <summary>Lot 10.3 : source d'acquisition captée par le web (facultative, selon le consentement).</summary>
    public Backend.Services.Marketing.AttributionDto? Attribution { get; set; }
}

public sealed record SubscriptionPurchaseResult(
    string Status,
    int? SubscriptionId,
    int? OrderId,
    decimal AmountTtc,
    SubscriptionQuote Quote,
    string Message);

/// <summary>Intention portée par une commande d'abonnement (Order.Notes), étendue au 9.7b.</summary>
public sealed record SubscriptionIntent(int PlanId, string BillingPeriod, int VersionId, string Change, bool AutoRenew, int? PromoCodeId)
{
    private const string Prefix = "subscription:";

    public override string ToString() =>
        $"{Prefix}{PlanId}:{BillingPeriod}:v{VersionId}:{Change}:r{(AutoRenew ? 1 : 0)}:p{PromoCodeId ?? 0}";

    public static SubscriptionIntent? Parse(string? notes)
    {
        if (!Backend.Services.SubscriptionOrderIntent.TryParse(notes, out var legacy)) return null;
        var parts = notes!.Trim()[Prefix.Length..].Split(':');
        int ver = 0; string change = SubscriptionChanges.New; bool renew = false; int? promo = null;
        foreach (var p in parts.Skip(2))
        {
            if (p.StartsWith('v') && int.TryParse(p[1..], out var v)) ver = v;
            else if (p.StartsWith('r') && p.Length == 2) renew = p[1] == '1';
            else if (p.StartsWith('p') && int.TryParse(p[1..], out var pid)) promo = pid > 0 ? pid : null;
            else if (p is SubscriptionChanges.New or SubscriptionChanges.Renew or SubscriptionChanges.Upgrade
                     or SubscriptionChanges.Downgrade or SubscriptionChanges.Trial) change = p;
        }
        return new SubscriptionIntent(legacy.PricingPlanId, legacy.Billing, ver, change, renew, promo);
    }
}

public sealed class SubscriptionLifecycleException : Exception
{
    public int StatusCode { get; }
    public SubscriptionLifecycleException(string message, int statusCode = 400) : base(message) { StatusCode = statusCode; }
}

/// <summary>
/// Cycle d'abonnement du lot 9.7b (décisions 18.12, 18.14, 18.15, 18.20) :
///   - prix TTC de la version publiée, par période ; remise d'un code ou d'une
///     offre automatique (moteur unifié) ; essai gratuit une fois par compte
///     et par public ;
///   - un seul abonnement en vigueur par compte (et donc par public) ;
///   - montée de plan immédiate, au prorata du temps restant de la période
///     payée ; descente (ou changement de période) programmée à l'échéance ;
///   - renouvellement d'un même plan sans perte des jours restants ;
///   - résiliation : accès jusqu'à la fin de la période, sans remboursement
///     partiel ;
///   - renouvellement automatique par le solde, avec 3 jours de grâce puis
///     coupure, et notifications (voir <see cref="RenewDueAsync"/>).
/// </summary>
public class SubscriptionLifecycleService
{
    public const int GraceDays = 3;

    private readonly ApplicationDbContext _db;
    private readonly IEntitlementService _entitlements;
    private readonly IPromotionService _promotions;
    private readonly IWalletService _wallet;

    public SubscriptionLifecycleService(ApplicationDbContext db, IEntitlementService entitlements, IPromotionService promotions, IWalletService wallet)
    {
        _db = db;
        _entitlements = entitlements;
        _promotions = promotions;
        _wallet = wallet;
    }

    private static decimal PriceOf(PlanVersion v, string period) =>
        v.Prices.FirstOrDefault(p => p.Period == period)?.PriceTtc
        ?? throw new SubscriptionLifecycleException("Cette période n'est pas proposée pour ce plan.");

    private async Task<Subscription?> InForceAsync(int userId, DateTime now) =>
        await _db.Subscriptions.Where(s => s.UserId == userId).Where(SubscriptionRules.InForce(now))
            .OrderByDescending(s => s.StartDate).FirstOrDefaultAsync();

    // ── Devis ───────────────────────────────────────────────────────────────

    public async Task<SubscriptionQuote> QuoteAsync(int userId, int planId, string? billingPeriod, string? promoCode, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new SubscriptionLifecycleException("Compte introuvable.", 404);
        var audience = PlanAudiences.ForRole(user.Role)
            ?? throw new SubscriptionLifecycleException("Ce compte ne souscrit pas d'abonnement (les établissements passent par une licence).");
        var plan = await _db.PricingPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId && p.Audience != null, ct)
            ?? throw new SubscriptionLifecycleException("Plan introuvable ou retiré de la vente.", 404);
        if (plan.Audience != audience)
            throw new SubscriptionLifecycleException($"Le plan {plan.Name} est réservé au public « {PlanAudiences.Label(plan.Audience)} ».");
        if (plan.IsFreeTier) throw new SubscriptionLifecycleException("Le plan gratuit ne se souscrit pas : c'est l'état par défaut de votre compte.");
        var version = await _db.PlanVersions.AsNoTracking().Include(v => v.Prices)
            .FirstOrDefaultAsync(v => v.PricingPlanId == planId && v.Status == PlanVersionStatus.Published, ct)
            ?? throw new SubscriptionLifecycleException("Ce plan n'a pas de version publiée.");
        var period = string.IsNullOrWhiteSpace(billingPeriod) ? BillingPeriods.Monthly : billingPeriod.Trim().ToLowerInvariant();
        if (!BillingPeriods.IsValid(period)) throw new SubscriptionLifecycleException("Période invalide (monthly, quarterly ou yearly).");
        var listPrice = PriceOf(version, period);
        var months = BillingPeriods.Months(period);

        var current = await _db.Subscriptions.AsNoTracking().Include(s => s.PlanVersion).ThenInclude(v => v!.Prices)
            .Where(s => s.UserId == userId).Where(SubscriptionRules.InForce(now))
            .OrderByDescending(s => s.StartDate).FirstOrDefaultAsync(ct);

        string change;
        decimal credit = 0;
        DateTime start = now, end;
        if (current == null || current.PlanVersionId == null)
        {
            change = SubscriptionChanges.New;
            end = now.AddMonths(months);
        }
        else if (current.PricingPlanId == planId && (current.BillingPeriod ?? BillingPeriods.Monthly) == period
                 && current.Status.ToLowerInvariant() != SubscriptionStatuses.Trialing)
        {
            change = SubscriptionChanges.Renew;
            start = current.EndDate is DateTime e && e > now ? e : now;
            end = start.AddMonths(months);
        }
        else
        {
            var currentPeriod = current.BillingPeriod ?? BillingPeriods.Monthly;
            var currentMonthly = (current.PricePaidTtc ?? current.PlanVersion?.Prices.FirstOrDefault(p => p.Period == currentPeriod)?.PriceTtc ?? 0)
                                 / BillingPeriods.Months(currentPeriod);
            var newMonthly = listPrice / months;
            var isTrial = current.Status.ToLowerInvariant() == SubscriptionStatuses.Trialing;
            if (isTrial || newMonthly > currentMonthly)
            {
                change = SubscriptionChanges.Upgrade;
                end = now.AddMonths(months);
                if (!isTrial && current.EndDate is DateTime ce && ce > now)
                {
                    // Prorata : crédit du temps restant de la période payée.
                    var periodStart = current.CurrentPeriodStart ?? current.StartDate;
                    var total = (ce - periodStart).TotalSeconds;
                    var left = (ce - now).TotalSeconds;
                    if (total > 0 && current.PricePaidTtc is decimal paid && paid > 0)
                        credit = Math.Round(paid * (decimal)(left / total), 0, MidpointRounding.AwayFromZero);
                }
            }
            else
            {
                change = SubscriptionChanges.Downgrade;
                start = current.EndDate ?? now;
                end = start.AddMonths(months);
            }
        }

        // Offre : code saisi, sinon meilleure offre automatique (essai compris).
        var offer = await _promotions.QuoteAsync(userId, new PromotionContext(
            PromoCodeScopes.Subscriptions, listPrice, audience, planId, period), promoCode, ct);
        decimal discount = 0;
        int? trialDays = null;
        string? promoError = offer.Valid ? null : offer.Error;
        if (offer.Valid && offer.Promo != null)
        {
            if (offer.TrialDays is int days)
            {
                if (change == SubscriptionChanges.New)
                {
                    change = SubscriptionChanges.Trial;
                    trialDays = days;
                    end = now.AddDays(days);
                    discount = listPrice;
                }
                else promoError = "Un essai gratuit ne s'ouvre que sans abonnement en cours.";
            }
            else if (change != SubscriptionChanges.Downgrade)
                discount = offer.Discount;
        }

        var due = change == SubscriptionChanges.Downgrade || change == SubscriptionChanges.Trial
            ? 0
            : Math.Max(0, listPrice - discount - credit);
        var promoId = (trialDays != null || discount > 0) ? offer.Promo?.Id : null;
        var message = change switch
        {
            SubscriptionChanges.Trial => $"Essai gratuit de {trialDays} jours, puis {listPrice:0} XAF TTC par {BillingPeriods.Label(period)} si vous activez le renouvellement.",
            SubscriptionChanges.Upgrade => credit > 0
                ? $"Montée immédiate : {credit:0} XAF de votre période en cours sont déduits."
                : "Montée immédiate vers ce plan.",
            SubscriptionChanges.Downgrade => $"Changement programmé à votre échéance du {start:dd/MM/yyyy} : rien à payer maintenant.",
            SubscriptionChanges.Renew => $"Une période de plus, ajoutée après votre échéance actuelle (jusqu'au {end:dd/MM/yyyy}).",
            _ => $"Abonnement jusqu'au {end:dd/MM/yyyy}.",
        };
        return new SubscriptionQuote(planId, plan.Name, audience, version.Id, period, change, listPrice, discount, credit, due,
            start, end, trialDays, promoId, promoId != null ? offer.Promo!.Code : null, promoError, message);
    }

    // ── Souscription ────────────────────────────────────────────────────────

    /// <summary>
    /// Démarre l'achat. Montant nul (essai, remise totale, prorata couvrant
    /// tout) ou paiement par le solde : activé tout de suite. Mobile Money :
    /// commande créée, l'appelant initie le paiement, et l'activation a lieu à
    /// la confirmation (<see cref="ActivateFromOrderAsync"/>).
    /// </summary>
    public async Task<SubscriptionPurchaseResult> PurchaseAsync(int userId, SubscriptionPurchaseRequest req, CancellationToken ct = default)
    {
        var quote = await QuoteAsync(userId, req.PlanId, req.BillingPeriod, req.PromoCode, ct);
        if (!string.IsNullOrWhiteSpace(req.PromoCode) && quote.PromoError != null)
            throw new SubscriptionLifecycleException(quote.PromoError);
        var now = DateTime.UtcNow;

        if (quote.Change == SubscriptionChanges.Downgrade)
        {
            var current = await InForceAsync(userId, now) ?? throw new SubscriptionLifecycleException("Aucun abonnement en cours.");
            current.PendingPricingPlanId = quote.PlanId;
            current.PendingBillingPeriod = quote.BillingPeriod;
            current.AutoRenew = req.AutoRenew || current.AutoRenew;
            current.CancelAtPeriodEnd = false;
            current.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            _entitlements.Invalidate();
            return new SubscriptionPurchaseResult("scheduled", current.Id, null, 0, quote, quote.Message);
        }

        var method = (req.Method ?? "mobile_money").Trim().ToLowerInvariant();
        var intent = new SubscriptionIntent(quote.PlanId, quote.BillingPeriod, quote.VersionId, quote.Change, req.AutoRenew, quote.PromoCodeId);
        var order = new Order
        {
            UserId = userId,
            OrderNumber = $"SUB-{userId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            TotalAmount = quote.AmountDueTtc,
            DiscountAmount = quote.DiscountTtc,
            PromoCode = quote.PromoCode,
            Status = "pending",
            PaymentMethod = quote.AmountDueTtc <= 0 ? "free" : method == "balance" ? "balance" : "notchpay",
            Notes = intent.ToString(),
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        if (quote.AmountDueTtc <= 0)
        {
            order.Status = "completed";
            await _db.SaveChangesAsync(ct);
            var sub = await ActivateFromOrderAsync(order.Id, ct);
            return new SubscriptionPurchaseResult("activated", sub?.Id, order.Id, 0, quote, quote.Message);
        }

        if (method == "balance")
        {
            var role = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Role).FirstAsync(ct);
            try
            {
                await _wallet.RunLockedAsync(userId, async () =>
                {
                    await DebitAsync(userId, role, order.TotalAmount, $"subscription_order:{order.Id}",
                        $"Abonnement {quote.PlanName} ({BillingPeriods.Label(quote.BillingPeriod)})", "Order", order.Id);
                    order.Status = "completed";
                    await _db.SaveChangesAsync(ct);
                    return true;
                });
            }
            catch (InsufficientWalletBalanceException ex)
            {
                order.Status = "failed";
                await _db.SaveChangesAsync(ct);
                throw new SubscriptionLifecycleException(ex.Message, 402);
            }
            var sub = await ActivateFromOrderAsync(order.Id, ct);
            return new SubscriptionPurchaseResult("activated", sub?.Id, order.Id, order.TotalAmount, quote, quote.Message);
        }

        return new SubscriptionPurchaseResult("payment_pending", null, order.Id, order.TotalAmount, quote, quote.Message);
    }

    private async Task DebitAsync(int userId, string role, decimal amount, string key, string label, string sourceType, int sourceId)
    {
        if (role == "parent")
            await _wallet.DebitParentWalletAsync(userId, amount, key, label, sourceType, sourceId);
        else
            await _wallet.DebitAsync(new WalletEntry(userId, WalletEntryTypes.SubscriptionPayment, amount, key, label, sourceType, sourceId));
    }

    /// <summary>
    /// Applique une commande d'abonnement payée (ou gratuite). Idempotent par
    /// commande (Subscription.OrderId) : un rejeu de confirmation ne crée rien,
    /// et deux commandes distinctes du même plan donnent bien deux périodes.
    /// </summary>
    public async Task<Subscription?> ActivateFromOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order?.UserId is not int userId) return null;
        var intent = SubscriptionIntent.Parse(order.Notes);
        if (intent == null || !PaidOrderStatus.IsPaid(order.Status)) return null;
        if (await _db.Subscriptions.IgnoreQueryFilters().AnyAsync(s => s.OrderId == orderId, ct))
            return await _db.Subscriptions.IgnoreQueryFilters().FirstAsync(s => s.OrderId == orderId, ct);

        var now = DateTime.UtcNow;
        var plan = await _db.PricingPlans.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == intent.PlanId, ct);
        if (plan == null) return null;
        var versionId = intent.VersionId > 0 ? intent.VersionId
            : await _db.PlanVersions.Where(v => v.PricingPlanId == plan.Id && v.Status == PlanVersionStatus.Published).Select(v => v.Id).FirstOrDefaultAsync(ct);
        if (versionId == 0) return null; // abonnement antérieur au 9.7b : SubscriptionActivationService historique
        var months = BillingPeriods.Months(intent.BillingPeriod);
        var promo = intent.PromoCodeId is int pid ? await _db.PromoCodes.FirstOrDefaultAsync(p => p.Id == pid, ct) : null;
        int? remainingDiscounted = promo == null || promo.Kind == PromotionKinds.TrialDays ? 0 : promo.SubscriptionDuration switch
        {
            PromotionDurations.AllPeriods => null,
            PromotionDurations.NPeriods => Math.Max(0, (promo.DurationPeriods ?? 1) - 1),
            _ => 0,
        };

        var current = await InForceAsync(userId, now);
        Subscription sub;
        if (intent.Change == SubscriptionChanges.Renew && current != null && current.PricingPlanId == plan.Id)
        {
            sub = current;
            var from = sub.EndDate is DateTime e && e > now ? e : now;
            sub.CurrentPeriodStart = from;
            sub.EndDate = from.AddMonths(months);
            sub.RenewalCount += 1;
        }
        else if (intent.Change == SubscriptionChanges.Upgrade && current != null)
        {
            sub = current;
            sub.PricingPlanId = plan.Id;
            sub.CurrentPeriodStart = now;
            sub.EndDate = now.AddMonths(months);
        }
        else
        {
            foreach (var other in await _db.Subscriptions.Where(s => s.UserId == userId).Where(SubscriptionRules.InForce(now)).ToListAsync(ct))
                Backend.Services.SubscriptionTermination.EndNow(other, now);
            sub = new Subscription { UserId = userId, PricingPlanId = plan.Id, StartDate = now, CreatedAt = now };
            _db.Subscriptions.Add(sub);
            sub.CurrentPeriodStart = now;
            if (intent.Change == SubscriptionChanges.Trial)
            {
                var days = Math.Max(1, promo?.TrialDays ?? 7);
                sub.EndDate = now.AddDays(days);
                sub.TrialEndsAt = sub.EndDate;
            }
            else sub.EndDate = now.AddMonths(months);
        }

        sub.PlanVersionId = versionId;
        sub.PlanName = plan.Name;
        sub.Audience = plan.Audience;
        sub.BillingPeriod = intent.BillingPeriod;
        sub.Status = intent.Change == SubscriptionChanges.Trial ? SubscriptionStatuses.Trialing : SubscriptionStatuses.Active;
        sub.IsActive = true;
        sub.GraceUntil = null;
        sub.CancelAtPeriodEnd = false;
        sub.CancelledAt = null;
        sub.AutoRenew = intent.AutoRenew;
        sub.PendingPricingPlanId = null;
        sub.PendingBillingPeriod = null;
        sub.PricePaidTtc = order.TotalAmount;
        sub.OrderId = order.Id;
        if (promo != null)
        {
            sub.PromoCodeId = remainingDiscounted == 0 ? null : promo.Id;
            sub.RemainingDiscountedPeriods = remainingDiscounted;
        }
        else if (intent.Change != SubscriptionChanges.Renew)
        {
            sub.PromoCodeId = null;
            sub.RemainingDiscountedPeriods = null;
        }
        sub.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        if (promo != null)
            await _promotions.RecordUsageAsync(promo.Id, userId, order.Id, sub.Id, PromoCodeScopes.Subscriptions, order.DiscountAmount, ct);
        _entitlements.Invalidate();
        return sub;
    }

    // ── Résiliation, reprise, renouvellement automatique ────────────────────

    public async Task<Subscription> CancelAsync(int userId, CancellationToken ct = default)
    {
        var sub = await InForceAsync(userId, DateTime.UtcNow) ?? throw new SubscriptionLifecycleException("Aucun abonnement en cours.", 404);
        sub.CancelAtPeriodEnd = true;
        sub.CancelledAt = DateTime.UtcNow;
        sub.AutoRenew = false;
        sub.PendingPricingPlanId = null;
        sub.PendingBillingPeriod = null;
        sub.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return sub;
    }

    public async Task<Subscription> ResumeAsync(int userId, CancellationToken ct = default)
    {
        var sub = await InForceAsync(userId, DateTime.UtcNow) ?? throw new SubscriptionLifecycleException("Aucun abonnement en cours.", 404);
        sub.CancelAtPeriodEnd = false;
        sub.CancelledAt = null;
        sub.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return sub;
    }

    public async Task<Subscription> SetAutoRenewAsync(int userId, bool enabled, CancellationToken ct = default)
    {
        var sub = await InForceAsync(userId, DateTime.UtcNow) ?? throw new SubscriptionLifecycleException("Aucun abonnement en cours.", 404);
        sub.AutoRenew = enabled;
        if (enabled) { sub.CancelAtPeriodEnd = false; sub.CancelledAt = null; }
        sub.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return sub;
    }

    public async Task CancelPendingChangeAsync(int userId, CancellationToken ct = default)
    {
        var sub = await InForceAsync(userId, DateTime.UtcNow) ?? throw new SubscriptionLifecycleException("Aucun abonnement en cours.", 404);
        sub.PendingPricingPlanId = null;
        sub.PendingBillingPeriod = null;
        sub.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private void Notify(int userId, string title, string message, int subscriptionId) =>
        _db.Notifications.Add(new Notification
        {
            UserId = userId, Title = title, Message = message, Type = "Subscription",
            RelatedEntityType = "Subscription", RelatedEntityId = subscriptionId, CreatedAt = DateTime.UtcNow, User = null!,
        });

    public sealed record RenewalRunReport(int Renewed, int InGrace, int Expired);

    /// <summary>
    /// Passe périodique (SubscriptionExpirationService) : renouvelle par le
    /// solde les abonnements échus en renouvellement automatique (version du
    /// préavis 18.12, descente programmée, remise encore due), met en grâce
    /// 3 jours ceux dont le solde ne suffit pas (nouvel essai à chaque passe),
    /// puis coupe ; expire les autres (résiliés, sans renouvellement, essais).
    /// </summary>
    public async Task<RenewalRunReport> RenewDueAsync(DateTime now, CancellationToken ct = default)
    {
        int renewed = 0, grace = 0, expired = 0;
        var due = await _db.Subscriptions
            .Include(s => s.User)
            .Where(s => !s.IsDeleted && s.IsActive && s.EndDate != null && s.EndDate <= now
                     && (s.Status.ToLower() == SubscriptionStatuses.Active || s.Status.ToLower() == SubscriptionStatuses.Trialing
                         || s.Status.ToLower() == SubscriptionStatuses.PastDue))
            .ToListAsync(ct);

        foreach (var sub in due)
        {
            var status = sub.Status.ToLowerInvariant();
            if (sub.PlanVersionId == null || !sub.AutoRenew || sub.CancelAtPeriodEnd)
            {
                // Abonnement antérieur au 9.7b, résilié, essai sans renouvellement, ou renouvellement manuel.
                sub.Status = sub.CancelAtPeriodEnd ? SubscriptionStatuses.Cancelled : SubscriptionStatuses.Expired;
                sub.IsActive = false;
                sub.UpdatedAt = now;
                Notify(sub.UserId, status == SubscriptionStatuses.Trialing ? "Votre essai est terminé" : "Votre abonnement est terminé",
                    $"Votre plan {sub.PlanName} a pris fin. Vous repassez sur l'offre gratuite ; vous pouvez vous réabonner à tout moment.", sub.Id);
                expired++;
                continue;
            }

            if (status == SubscriptionStatuses.PastDue && sub.GraceUntil is DateTime gu && gu <= now)
            {
                sub.Status = SubscriptionStatuses.Expired;
                sub.IsActive = false;
                sub.UpdatedAt = now;
                Notify(sub.UserId, "Abonnement suspendu", $"Le renouvellement de votre plan {sub.PlanName} n'a pas pu être payé pendant les {GraceDays} jours de grâce : l'abonnement est coupé. Rechargez votre portefeuille puis réabonnez-vous.", sub.Id);
                expired++;
                continue;
            }

            // Plan et période de la nouvelle période : descente programmée, sinon le même.
            var planId = sub.PendingPricingPlanId ?? sub.PricingPlanId;
            var period = sub.PendingBillingPeriod ?? sub.BillingPeriod ?? BillingPeriods.Monthly;
            PlanVersion? version;
            if (sub.PendingPricingPlanId != null)
                version = await _db.PlanVersions.Include(v => v.Prices)
                    .FirstOrDefaultAsync(v => v.PricingPlanId == planId && v.Status == PlanVersionStatus.Published, ct);
            else
                version = await PlanCatalogService.VersionForRenewalAsync(_db, sub, sub.EndDate!.Value, ct);
            var priceRow = version?.Prices.FirstOrDefault(p => p.Period == period);
            if (version == null || priceRow == null)
            {
                sub.Status = SubscriptionStatuses.Expired;
                sub.IsActive = false;
                sub.UpdatedAt = now;
                Notify(sub.UserId, "Abonnement terminé", $"Le plan {sub.PlanName} n'est plus proposé pour cette période : votre abonnement a pris fin.", sub.Id);
                expired++;
                continue;
            }
            var price = priceRow.PriceTtc;
            PromoCode? promo = null;
            if (sub.PromoCodeId is int pid && (sub.RemainingDiscountedPeriods == null || sub.RemainingDiscountedPeriods > 0))
            {
                promo = await _db.PromoCodes.FirstOrDefaultAsync(p => p.Id == pid, ct);
                if (promo != null) price = _promotions.ApplyStoredDiscount(promo, price);
            }

            var periodEnd = sub.EndDate!.Value;
            var key = $"subscription_renewal:{sub.Id}:{periodEnd:yyyyMMddHHmm}";
            try
            {
                if (price > 0)
                    await _wallet.RunLockedAsync(sub.UserId, async () =>
                    {
                        await DebitAsync(sub.UserId, sub.User?.Role ?? "student", price, key,
                            $"Renouvellement {sub.PlanName} ({BillingPeriods.Label(period)})", "Subscription", sub.Id);
                        return true;
                    });
                var plan = await _db.PricingPlans.IgnoreQueryFilters().FirstAsync(p => p.Id == planId, ct);
                sub.PricingPlanId = planId;
                sub.PlanName = plan.Name;
                sub.PlanVersionId = version.Id;
                sub.BillingPeriod = period;
                sub.CurrentPeriodStart = periodEnd;
                sub.EndDate = periodEnd.AddMonths(BillingPeriods.Months(period));
                sub.PricePaidTtc = price;
                sub.Status = SubscriptionStatuses.Active;
                sub.GraceUntil = null;
                sub.PendingPricingPlanId = null;
                sub.PendingBillingPeriod = null;
                sub.RenewalCount += 1;
                sub.LastRenewalAttemptAt = now;
                if (promo != null && sub.RemainingDiscountedPeriods is int left)
                {
                    sub.RemainingDiscountedPeriods = left - 1;
                    if (left - 1 <= 0) sub.PromoCodeId = null;
                }
                sub.UpdatedAt = now;
                Notify(sub.UserId, "Abonnement renouvelé", $"Votre plan {sub.PlanName} est renouvelé jusqu'au {sub.EndDate:dd/MM/yyyy} : {price:0} XAF débités de votre portefeuille.", sub.Id);
                renewed++;
            }
            catch (InsufficientWalletBalanceException)
            {
                sub.LastRenewalAttemptAt = now;
                if (status != SubscriptionStatuses.PastDue)
                {
                    sub.Status = SubscriptionStatuses.PastDue;
                    sub.GraceUntil = periodEnd.AddDays(GraceDays);
                    Notify(sub.UserId, "Renouvellement en attente",
                        $"Votre solde ne couvre pas le renouvellement de {sub.PlanName} ({price:0} XAF). Vous gardez l'accès {GraceDays} jours, jusqu'au {sub.GraceUntil:dd/MM/yyyy} : rechargez votre portefeuille.", sub.Id);
                }
                sub.UpdatedAt = now;
                grace++;
            }
        }
        await _db.SaveChangesAsync(ct);
        if (renewed + grace + expired > 0) _entitlements.Invalidate();
        return new RenewalRunReport(renewed, grace, expired);
    }
}
