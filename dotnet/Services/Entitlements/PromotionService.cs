using System.Text.Json;
using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Entitlements;

/// <summary>Contexte d'achat auquel une offre est confrontée.</summary>
public sealed record PromotionContext(
    string Target,
    decimal Amount,
    string? Audience = null,
    int? PlanId = null,
    string? BillingPeriod = null,
    IReadOnlyList<int>? SubjectIds = null,
    IReadOnlyList<int>? CourseIds = null,
    int? InstitutionId = null);

/// <summary>Résultat d'une offre appliquée à un montant TTC.</summary>
public sealed record OfferQuote(
    bool Valid,
    PromoCode? Promo,
    decimal BaseAmount,
    decimal Discount,
    decimal FinalAmount,
    int? TrialDays,
    long BonusTokens,
    string? Error)
{
    public static OfferQuote None(decimal amount) => new(false, null, amount, 0, amount, null, 0, null);
    public static OfferQuote Refused(decimal amount, string error) => new(false, null, amount, 0, amount, null, 0, error);
}

public interface IPromotionService
{
    /// <summary>
    /// Évalue un code saisi (ou, sans code, la meilleure offre automatique) pour
    /// un achat. Ne consomme rien : l'usage est enregistré au paiement
    /// (<see cref="RecordUsageAsync"/>, décision 5.5.O), ou à l'activation d'un essai.
    /// </summary>
    Task<OfferQuote> QuoteAsync(int userId, PromotionContext ctx, string? code, CancellationToken ct = default);

    Task RecordUsageAsync(int promoCodeId, int userId, int? orderId, int? subscriptionId, string target, decimal discount, CancellationToken ct = default);

    /// <summary>Un seul essai gratuit par compte et par public (étude 5.2).</summary>
    Task<bool> HasUsedTrialAsync(int userId, string audience, CancellationToken ct = default);

    /// <summary>Remise d'une période renouvelée (all_periods, n_periods) ; null si aucune.</summary>
    decimal ApplyStoredDiscount(PromoCode promo, decimal amount);
}

/// <summary>
/// Moteur de promotion unifié (lot 9.7b, étude section 5, décisions 18.7,
/// 18.14, 18.20, 18.21). Une seule entité, <see cref="PromoCode"/>, porte les
/// remises (pourcentage, montant), les essais gratuits (trial_days) et les
/// tokens offerts (bonus_tokens), sur toutes les cibles : catalogue,
/// formations, abonnements, recharges de tokens, licences. Une seule offre
/// par paiement (non cumulable par défaut).
///
/// Catalogue et formations (18.21) : la remise est supportée par la
/// plateforme seule ; elle réduit le total payé, jamais le prix de ligne
/// (OrderItem.PriceAtPurchase) sur lequel le professeur est payé.
/// </summary>
public class PromotionService : IPromotionService
{
    private readonly ApplicationDbContext _db;
    private readonly IPlatformSettings? _settings;

    public PromotionService(ApplicationDbContext db, IPlatformSettings? settings = null)
    {
        _db = db;
        _settings = settings;
    }

    private static List<int> Ids(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<int>>(json) ?? new(); } catch { return new(); }
    }

    private static HashSet<string> List(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant()).ToHashSet();

    public async Task<bool> HasUsedTrialAsync(int userId, string audience, CancellationToken ct = default) =>
        await _db.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(s => s.UserId == userId && s.TrialEndsAt != null && (s.Audience == audience || s.Audience == null), ct);

    public decimal ApplyStoredDiscount(PromoCode promo, decimal amount) => Math.Max(0, amount - DiscountOf(promo, amount));

    /// <summary>Nature effective : une ligne antérieure au 9.7b « FixedAmount » sans nature renseignée est une remise en montant.</summary>
    public static string EffectiveKind(PromoCode p) =>
        p.Kind == PromotionKinds.DiscountPercent && p.DiscountType == "FixedAmount" ? PromotionKinds.DiscountAmount : p.Kind;

    private static decimal DiscountOf(PromoCode p, decimal amount)
    {
        decimal d = EffectiveKind(p) switch
        {
            PromotionKinds.DiscountPercent => amount * p.DiscountValue / 100m,
            PromotionKinds.DiscountAmount => p.DiscountValue,
            PromotionKinds.TrialDays => amount,
            _ => 0m,
        };
        if (p.MaximumDiscount is decimal max && p.Kind != PromotionKinds.TrialDays && d > max) d = max;
        d = Math.Min(Math.Max(0, d), amount);
        return Math.Round(d, 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>Règles communes d'une offre ; renvoie l'erreur en français ou null.</summary>
    private async Task<string?> CheckAsync(PromoCode p, int userId, PromotionContext ctx, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (!p.IsActive) return "Cette offre n'est plus active.";
        if (p.ValidFrom > now) return $"Cette offre n'est valable qu'à partir du {p.ValidFrom:dd/MM/yyyy}.";
        if (p.ValidUntil.HasValue && p.ValidUntil < now) return "Cette offre a expiré.";
        if (!PromoCodeScopes.AppliesTo(p.AppliesTo, ctx.Target)) return "Cette offre ne s'applique pas à cet achat.";
        if (p.UsageLimit.HasValue && p.UsageCount >= p.UsageLimit.Value) return "Cette offre a atteint sa limite d'utilisation.";
        var used = await _db.PromoCodeUsages.CountAsync(u => u.PromoCodeId == p.Id && u.UserId == userId, ct);
        if (p.PerUserLimit.HasValue && used >= p.PerUserLimit.Value) return "Vous avez déjà utilisé cette offre.";
        if (p.MinimumPurchase.HasValue && ctx.Amount < p.MinimumPurchase.Value && p.Kind != PromotionKinds.TrialDays)
            return $"Montant minimum de {p.MinimumPurchase:0} XAF requis.";

        var user = await _db.Users.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return "Compte introuvable.";
        var roles = List(p.TargetRoles);
        if (roles.Count > 0 && !roles.Contains(user.Role.ToLowerInvariant())) return "Cette offre ne concerne pas votre type de compte.";

        if (ctx.Target == PromoCodeScopes.Subscriptions)
        {
            var aud = List(p.Audiences);
            if (aud.Count > 0 && (ctx.Audience == null || !aud.Contains(ctx.Audience))) return "Cette offre ne concerne pas ce plan.";
            var plans = Ids(p.PlanIds);
            if (plans.Count > 0 && (ctx.PlanId == null || !plans.Contains(ctx.PlanId.Value))) return "Cette offre ne concerne pas ce plan.";
            var periods = List(p.BillingPeriods);
            if (periods.Count > 0 && (ctx.BillingPeriod == null || !periods.Contains(ctx.BillingPeriod))) return "Cette offre ne concerne pas cette période.";
            if (p.Kind == PromotionKinds.TrialDays && ctx.Audience != null && await HasUsedTrialAsync(userId, ctx.Audience, ct))
                return "Vous avez déjà bénéficié d'un essai gratuit pour ce type de plan.";
        }
        else if (p.Kind == PromotionKinds.TrialDays && ctx.Target != PromoCodeScopes.InstitutionLicense)
            return "Un essai gratuit ne s'applique qu'à un abonnement ou à une licence.";

        if (ctx.Target == PromoCodeScopes.Catalog && !string.IsNullOrEmpty(p.ApplicableSubjectIds))
        {
            var allowed = Ids(p.ApplicableSubjectIds);
            if (!(ctx.SubjectIds?.Any(allowed.Contains) ?? false)) return "Ce code ne s'applique pas aux contenus de votre panier.";
        }

        switch (p.Eligibility)
        {
            case PromotionEligibility.NewAccounts:
                if (user.CreatedAt < now.AddDays(-(p.NewAccountDays ?? 30))) return "Cette offre est réservée aux nouveaux comptes.";
                break;
            case PromotionEligibility.NeverSubscribed:
                if (await _db.Subscriptions.IgnoreQueryFilters().AnyAsync(s => s.UserId == userId && (s.PricePaidTtc > 0 || s.PlanVersionId == null), ct))
                    return "Cette offre est réservée aux comptes qui n'ont jamais été abonnés.";
                break;
            case PromotionEligibility.LapsedSubscribers:
                {
                    var any = await _db.Subscriptions.IgnoreQueryFilters().AnyAsync(s => s.UserId == userId, ct);
                    var inForce = await _db.Subscriptions.IgnoreQueryFilters().Where(s => s.UserId == userId).AnyAsync(SubscriptionRules.InForce(now), ct);
                    if (!any || inForce) return "Cette offre est réservée aux anciens abonnés.";
                    break;
                }
            case PromotionEligibility.Listed:
                {
                    var users = Ids(p.EligibleUserIds);
                    var insts = Ids(p.EligibleInstitutionIds);
                    var ok = users.Contains(userId) || (ctx.InstitutionId is int iid && insts.Contains(iid))
                             || (user.InstitutionId is int uiid && insts.Contains(uiid));
                    if (!ok) return "Cette offre ne vous est pas destinée.";
                    break;
                }
        }
        return null;
    }

    private OfferQuote Build(PromoCode p, PromotionContext ctx)
    {
        var discount = DiscountOf(p, ctx.Amount);
        return new OfferQuote(true, p, ctx.Amount, discount, Math.Max(0, ctx.Amount - discount),
            p.Kind == PromotionKinds.TrialDays ? Math.Max(1, p.TrialDays ?? 7) : null,
            p.Kind == PromotionKinds.BonusTokens ? Math.Max(0, p.BonusTokens ?? 0) : 0, null);
    }

    public async Task<OfferQuote> QuoteAsync(int userId, PromotionContext ctx, string? code, CancellationToken ct = default)
    {
        if (_settings != null && !_settings.GetBool(PlatformSettingDefinitions.PromoCodesEnabled))
            return string.IsNullOrWhiteSpace(code) ? OfferQuote.None(ctx.Amount) : OfferQuote.Refused(ctx.Amount, "Les codes promo sont momentanément désactivés.");

        if (!string.IsNullOrWhiteSpace(code))
        {
            var normalized = code.Trim().ToUpperInvariant();
            var promo = await _db.PromoCodes.FirstOrDefaultAsync(p => p.Code == normalized, ct);
            if (promo == null) return OfferQuote.Refused(ctx.Amount, "Code promo inconnu.");
            var error = await CheckAsync(promo, userId, ctx, ct);
            return error == null ? Build(promo, ctx) : OfferQuote.Refused(ctx.Amount, error);
        }

        // Sans code : meilleure offre automatique (RequiresCode = faux).
        var now = DateTime.UtcNow;
        var automatic = await _db.PromoCodes.AsNoTracking()
            .Where(p => !p.RequiresCode && p.IsActive && p.ValidFrom <= now && (p.ValidUntil == null || p.ValidUntil > now))
            .ToListAsync(ct);
        OfferQuote best = OfferQuote.None(ctx.Amount);
        foreach (var p in automatic)
        {
            if (await CheckAsync(p, userId, ctx, ct) != null) continue;
            var q = Build(p, ctx);
            if (!best.Valid || q.Discount > best.Discount || (q.TrialDays ?? 0) > (best.TrialDays ?? 0)) best = q;
        }
        return best;
    }

    public async Task RecordUsageAsync(int promoCodeId, int userId, int? orderId, int? subscriptionId, string target, decimal discount, CancellationToken ct = default)
    {
        if (orderId is int oid && await _db.PromoCodeUsages.AnyAsync(u => u.OrderId == oid, ct)) return;
        if (orderId == null && subscriptionId is int sid
            && await _db.PromoCodeUsages.AnyAsync(u => u.SubscriptionId == sid && u.PromoCodeId == promoCodeId, ct)) return;
        var promo = await _db.PromoCodes.FirstOrDefaultAsync(p => p.Id == promoCodeId, ct);
        if (promo == null) return;
        _db.PromoCodeUsages.Add(new PromoCodeUsage
        {
            PromoCodeId = promoCodeId,
            UserId = userId,
            OrderId = orderId,
            SubscriptionId = subscriptionId,
            Target = target,
            DiscountAmount = discount,
            UsedAt = DateTime.UtcNow,
        });
        promo.UsageCount++;
        await _db.SaveChangesAsync(ct);
    }
}
