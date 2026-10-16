using System.Text.Json;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Models.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public interface IPromoCodeService
{
    Task<PromoCodeDto> CreatePromoCodeAsync(int adminUserId, CreatePromoCodeRequest request);
    Task<PromoCodeValidationResult> ValidatePromoCodeAsync(int userId, ValidatePromoCodeRequest request);
    Task<bool> ApplyPromoCodeAsync(int userId, int orderId, string code);
    Task<List<PromoCodeDto>> GetAllPromoCodesAsync(bool includeInactive = false);
    Task<PromoCodeDto?> GetPromoCodeByCodeAsync(string code);
    Task<bool> DeactivatePromoCodeAsync(int id);
    Task<bool> ActivatePromoCodeAsync(int id);
    Task<PromoCodeDto?> UpdatePromoCodeAsync(int id, UpdatePromoCodeRequest request);
}

public class PromoCodeService : IPromoCodeService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<PromoCodeService> _logger;

    private readonly IPlatformSettings? _platformSettings;

    public PromoCodeService(ApplicationDbContext context, ILogger<PromoCodeService> logger, IPlatformSettings? platformSettings = null)
    {
        _context = context;
        _logger = logger;
        _platformSettings = platformSettings;
    }

    private static string NormalizeScope(string? scope) =>
        PromoCodeScopes.Normalize(scope)
        ?? throw new InvalidOperationException("Cibles invalides : catalog, courses, subscriptions, token_topup, institution_license.");

    /// <summary>
    /// Lot 9.7b : champs du modèle de promotion unifié, validés ici (seul point
    /// d'écriture des offres). La nature déduit le type historique de remise.
    /// </summary>
    private static void ApplyPromotionFields(PromoCode p, PromotionFields? f, string? discountType)
    {
        if (f?.Kind != null)
        {
            if (!PromotionKinds.IsValid(f.Kind)) throw new InvalidOperationException("Nature d'offre invalide.");
            p.Kind = f.Kind;
        }
        else if (discountType != null)
            p.Kind = discountType == "FixedAmount" ? PromotionKinds.DiscountAmount : PromotionKinds.DiscountPercent;
        p.DiscountType = p.Kind == PromotionKinds.DiscountAmount ? "FixedAmount" : "Percentage";
        if (p.Kind == PromotionKinds.DiscountPercent && (p.DiscountValue <= 0 || p.DiscountValue > 100))
            throw new InvalidOperationException("Une remise en pourcentage est comprise entre 0 et 100.");
        if (p.Kind == PromotionKinds.DiscountAmount && p.DiscountValue <= 0)
            throw new InvalidOperationException("Une remise en montant doit être positive.");
        if (f == null) return;
        if (f.RequiresCode.HasValue) p.RequiresCode = f.RequiresCode.Value;
        if (f.PlanIds != null) p.PlanIds = f.PlanIds.Count == 0 ? null : JsonSerializer.Serialize(f.PlanIds);
        if (f.Audiences != null) p.Audiences = string.IsNullOrWhiteSpace(f.Audiences) ? null : f.Audiences.Trim().ToLowerInvariant();
        if (f.BillingPeriods != null) p.BillingPeriods = string.IsNullOrWhiteSpace(f.BillingPeriods) ? null : f.BillingPeriods.Trim().ToLowerInvariant();
        if (f.SubscriptionDuration != null)
        {
            if (!PromotionDurations.IsValid(f.SubscriptionDuration)) throw new InvalidOperationException("Durée de remise invalide (first_payment, all_periods, n_periods).");
            p.SubscriptionDuration = f.SubscriptionDuration;
        }
        if (f.DurationPeriods.HasValue) p.DurationPeriods = Math.Max(1, f.DurationPeriods.Value);
        if (p.SubscriptionDuration == PromotionDurations.NPeriods && (p.DurationPeriods ?? 0) < 1)
            throw new InvalidOperationException("Indiquez le nombre de périodes remisées.");
        if (f.TrialDays.HasValue) p.TrialDays = Math.Clamp(f.TrialDays.Value, 1, 365);
        if (p.Kind == PromotionKinds.TrialDays && (p.TrialDays ?? 0) < 1) throw new InvalidOperationException("Indiquez la durée de l'essai en jours.");
        if (f.BonusTokens.HasValue) p.BonusTokens = Math.Max(0, f.BonusTokens.Value);
        if (f.TargetRoles != null) p.TargetRoles = string.IsNullOrWhiteSpace(f.TargetRoles) ? null : f.TargetRoles.Trim().ToLowerInvariant();
        if (f.Eligibility != null)
        {
            if (!PromotionEligibility.IsValid(f.Eligibility)) throw new InvalidOperationException("Éligibilité invalide.");
            p.Eligibility = f.Eligibility;
        }
        if (f.NewAccountDays.HasValue) p.NewAccountDays = Math.Max(1, f.NewAccountDays.Value);
        if (f.EligibleUserIds != null) p.EligibleUserIds = f.EligibleUserIds.Count == 0 ? null : JsonSerializer.Serialize(f.EligibleUserIds);
        if (f.EligibleInstitutionIds != null) p.EligibleInstitutionIds = f.EligibleInstitutionIds.Count == 0 ? null : JsonSerializer.Serialize(f.EligibleInstitutionIds);
        if (f.Stackable.HasValue) p.Stackable = f.Stackable.Value;
    }

    public async Task<PromoCodeDto> CreatePromoCodeAsync(int adminUserId, CreatePromoCodeRequest request)
    {
        try
        {
            // Check if code already exists
            var existing = await _context.PromoCodes
                .FirstOrDefaultAsync(p => p.Code == request.Code.ToUpper());
            
            if (existing != null)
            {
                throw new InvalidOperationException($"Promo code '{request.Code}' already exists");
            }

            var promoCode = new PromoCode
            {
                Code = request.Code.ToUpper(),
                Description = request.Description,
                DiscountType = request.DiscountType ?? "Percentage",
                DiscountValue = request.DiscountValue,
                MinimumPurchase = request.MinimumPurchase,
                MaximumDiscount = request.MaximumDiscount,
                UsageLimit = request.UsageLimit,
                PerUserLimit = request.PerUserLimit ?? 1,
                ValidFrom = Utc(request.ValidFrom),
                ValidUntil = Utc(request.ValidUntil),
                ApplicableSubjectIds = request.ApplicableSubjectIds != null 
                    ? JsonSerializer.Serialize(request.ApplicableSubjectIds) 
                    : null,
                AppliesTo = NormalizeScope(request.AppliesTo),
                CreatedBy = adminUserId,
                CreatedAt = DateTime.UtcNow
            };
            ApplyPromotionFields(promoCode, request.Promotion, request.DiscountType);

            _context.PromoCodes.Add(promoCode);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Promo code {Code} created by user {UserId}", promoCode.Code, adminUserId);

            return MapToDto(promoCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating promo code");
            throw;
        }
    }

    /// <summary>
    /// Lot 9.7b : validation au panier par le moteur unifié (PromotionService).
    /// La remise porte sur les lignes catalogue (cible « catalog ») et sur les
    /// lignes formation (cible « courses », F9) que le code vise ; elle est
    /// supportée par la plateforme seule (18.21) : seul le total payé baisse.
    /// </summary>
    public async Task<PromoCodeValidationResult> ValidatePromoCodeAsync(int userId, ValidatePromoCodeRequest request)
    {
        var engine = new Backend.Services.Entitlements.PromotionService(_context, _platformSettings);
        var coursesBase = Math.Max(0, request.CoursesTotal ?? 0);
        var catalogBase = Math.Max(0, request.CatalogTotal ?? (request.CartTotal - coursesBase));
        var result = new PromoCodeValidationResult { IsValid = false, DiscountAmount = 0, FinalAmount = request.CartTotal };

        Backend.Services.Entitlements.OfferQuote? catalog = null, courses = null;
        if (catalogBase > 0 || coursesBase <= 0)
            catalog = await engine.QuoteAsync(userId, new Backend.Services.Entitlements.PromotionContext(
                PromoCodeScopes.Catalog, catalogBase > 0 ? catalogBase : request.CartTotal, SubjectIds: request.SubjectIds), request.Code);
        if (coursesBase > 0)
            courses = await engine.QuoteAsync(userId, new Backend.Services.Entitlements.PromotionContext(
                PromoCodeScopes.Courses, coursesBase, CourseIds: request.CourseIds), request.Code);

        var valid = (catalog?.Valid ?? false) || (courses?.Valid ?? false);
        if (!valid)
        {
            result.ErrorMessage = catalog?.Error ?? courses?.Error ?? "Code promo invalide.";
            return result;
        }
        var promo = (catalog?.Valid == true ? catalog.Promo : courses!.Promo)!;
        var discount = (catalog?.Valid == true ? catalog.Discount : 0) + (courses?.Valid == true ? courses.Discount : 0);
        if (promo.MaximumDiscount is decimal max && discount > max) discount = max;
        discount = Math.Min(discount, request.CartTotal);
        result.IsValid = true;
        result.DiscountAmount = Math.Round(discount, 0, MidpointRounding.AwayFromZero);
        result.FinalAmount = Math.Max(0, request.CartTotal - result.DiscountAmount);
        result.PromoCode = MapToDto(promo);
        return result;
    }

    /// <summary>
    /// Rattache définitivement un code promo à une commande dont le paiement
    /// vient d'être confirmé : enregistre l'usage et décompte le quota
    /// (Module 34, décision §5.5.O — jamais à la création de la commande).
    ///
    /// Ne touche plus <c>Order.TotalAmount</c>/<c>DiscountAmount</c> : la
    /// remise a déjà été calculée et appliquée côté serveur à la création de
    /// la commande (<see cref="OrderService.CreateOrderAsync"/>), sur le même
    /// prix que celui réellement encaissé. La recalculer ici referait le même
    /// calcul sur un total déjà remisé, ce qui appliquerait la remise deux
    /// fois. Idempotent : un paiement confirmé deux fois (rejeu de webhook) ne
    /// décompte le quota qu'une seule fois.
    /// </summary>
    public async Task<bool> ApplyPromoCodeAsync(int userId, int orderId, string code)
    {
        try
        {
            // Idempotence : si l'usage existe déjà pour cette commande, le
            // code a déjà été appliqué (rejeu de confirmation de paiement).
            var alreadyApplied = await _context.PromoCodeUsages.AnyAsync(u => u.OrderId == orderId);
            if (alreadyApplied) return true;

            var promoCode = await _context.PromoCodes
                .FirstOrDefaultAsync(p => p.Code == code.ToUpper());

            if (promoCode == null)
                return false;

            var order = await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

            if (order == null)
                return false;

            // Le quota et les conditions temporelles sont revérifiés ici (un
            // code a pu expirer ou atteindre son quota entre la création de la
            // commande et la confirmation du paiement). Le montant, lui, n'est
            // plus recalculé : le client a déjà payé le total remisé de la
            // commande, revenir dessus romprait l'égalité avec ce qui a été
            // réellement encaissé chez le prestataire de paiement. Un code
            // épuisé entre-temps n'est donc plus décompté une seconde fois (le
            // quota resterait négatif de sens), mais la remise déjà payée par
            // le client reste acquise — c'est un point ouvert documenté dans le
            // rapport du lot, aucune décision produit ne tranchant ce cas.
            var now = DateTime.UtcNow;
            var quotaOk = promoCode.IsActive
                && promoCode.ValidFrom <= now
                && (!promoCode.ValidUntil.HasValue || promoCode.ValidUntil >= now)
                && (!promoCode.UsageLimit.HasValue || promoCode.UsageCount < promoCode.UsageLimit.Value);

            if (!quotaOk)
            {
                _logger.LogWarning(
                    "Code promo {Code} devenu invalide (quota ou période) entre la commande {OrderId} et la " +
                    "confirmation du paiement : usage non décompté, remise déjà payée conservée.",
                    code, orderId);
            }

            var usage = new PromoCodeUsage
            {
                PromoCodeId = promoCode.Id,
                UserId = userId,
                OrderId = orderId,
                DiscountAmount = order.DiscountAmount,
                UsedAt = now,
            };
            _context.PromoCodeUsages.Add(usage);

            if (quotaOk)
                promoCode.UsageCount++;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Promo code {Code} applied to order {OrderId} by user {UserId}",
                code, orderId, userId);

            return true;
        }
        catch (DbUpdateException dbEx)
        {
            // Rejeu concurrent (webhook + synchronisation de statut presque
            // simultanés) : l'autre appel a déjà inséré l'usage.
            if (await _context.PromoCodeUsages.AnyAsync(u => u.OrderId == orderId))
                return true;
            _logger.LogError(dbEx, "Error applying promo code");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying promo code");
            return false;
        }
    }

    public async Task<List<PromoCodeDto>> GetAllPromoCodesAsync(bool includeInactive = false)
    {
        try
        {
            var query = _context.PromoCodes.AsQueryable();
            if (!includeInactive) query = query.Where(p => p.IsActive);

            var promoCodes = await query
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            return promoCodes.Select(MapToDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all promo codes");
            return new List<PromoCodeDto>();
        }
    }

    public async Task<PromoCodeDto?> GetPromoCodeByCodeAsync(string code)
    {
        try
        {
            var promoCode = await _context.PromoCodes
                .FirstOrDefaultAsync(p => p.Code == code.ToUpper());

            return promoCode != null ? MapToDto(promoCode) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting promo code by code");
            return null;
        }
    }

    public async Task<bool> DeactivatePromoCodeAsync(int id)
    {
        try
        {
            var promoCode = await _context.PromoCodes.FindAsync(id);
            if (promoCode == null) return false;

            promoCode.IsActive = false;
            promoCode.UpdatedAt = DateTime.UtcNow;

            _context.PromoCodes.Update(promoCode);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Promo code {PromoCodeId} deactivated", id);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating promo code");
            return false;
        }
    }

    public async Task<bool> ActivatePromoCodeAsync(int id)
    {
        try
        {
            var promoCode = await _context.PromoCodes.FindAsync(id);
            if (promoCode == null) return false;

            promoCode.IsActive = true;
            promoCode.UpdatedAt = DateTime.UtcNow;

            _context.PromoCodes.Update(promoCode);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Promo code {PromoCodeId} activated", id);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating promo code");
            return false;
        }
    }

    public async Task<PromoCodeDto?> UpdatePromoCodeAsync(int id, UpdatePromoCodeRequest request)
    {
        try
        {
            var promoCode = await _context.PromoCodes.FindAsync(id);
            if (promoCode == null) return null;

            promoCode.Description = request.Description;
            if (request.DiscountType != null) promoCode.DiscountType = request.DiscountType;
            promoCode.DiscountValue = request.DiscountValue;
            if (request.PerUserLimit.HasValue) promoCode.PerUserLimit = request.PerUserLimit;
            promoCode.MinimumPurchase = request.MinimumPurchase;
            promoCode.MaximumDiscount = request.MaximumDiscount;
            promoCode.UsageLimit = request.UsageLimit;
            promoCode.ValidUntil = Utc(request.ValidUntil);
            promoCode.ApplicableSubjectIds = request.ApplicableSubjectIds != null
                ? JsonSerializer.Serialize(request.ApplicableSubjectIds)
                : null;
            if (request.AppliesTo != null) promoCode.AppliesTo = NormalizeScope(request.AppliesTo);
            ApplyPromotionFields(promoCode, request.Promotion, request.DiscountType);
            promoCode.UpdatedAt = DateTime.UtcNow;

            _context.PromoCodes.Update(promoCode);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Promo code {PromoCodeId} updated", id);

            return MapToDto(promoCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating promo code {PromoCodeId}", id);
            throw;
        }
    }

    private static List<int>? ParseIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<List<int>>(json); } catch { return null; }
    }

    private static DateTime Utc(DateTime dt) =>
        dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime();

    private static DateTime? Utc(DateTime? dt) => dt.HasValue ? Utc(dt.Value) : null;

    private PromoCodeDto MapToDto(PromoCode promoCode)
    {
        List<int>? applicableIds = null;
        if (!string.IsNullOrEmpty(promoCode.ApplicableSubjectIds))
        {
            try
            {
                applicableIds = JsonSerializer.Deserialize<List<int>>(promoCode.ApplicableSubjectIds);
            }
            catch { }
        }

        return new PromoCodeDto
        {
            Id = promoCode.Id,
            Code = promoCode.Code,
            Description = promoCode.Description,
            DiscountType = promoCode.DiscountType,
            DiscountValue = promoCode.DiscountValue,
            MinimumPurchase = promoCode.MinimumPurchase,
            MaximumDiscount = promoCode.MaximumDiscount,
            UsageLimit = promoCode.UsageLimit,
            UsageCount = promoCode.UsageCount,
            PerUserLimit = promoCode.PerUserLimit,
            ValidFrom = promoCode.ValidFrom,
            ValidUntil = promoCode.ValidUntil,
            IsActive = promoCode.IsActive,
            ApplicableSubjectIds = applicableIds,
            AppliesTo = PromoCodeScopes.Normalize(promoCode.AppliesTo) ?? PromoCodeScopes.Catalog,
            Promotion = new PromotionFields
            {
                Kind = promoCode.Kind,
                RequiresCode = promoCode.RequiresCode,
                PlanIds = ParseIds(promoCode.PlanIds),
                Audiences = promoCode.Audiences,
                BillingPeriods = promoCode.BillingPeriods,
                SubscriptionDuration = promoCode.SubscriptionDuration,
                DurationPeriods = promoCode.DurationPeriods,
                TrialDays = promoCode.TrialDays,
                BonusTokens = promoCode.BonusTokens,
                TargetRoles = promoCode.TargetRoles,
                Eligibility = promoCode.Eligibility,
                NewAccountDays = promoCode.NewAccountDays,
                EligibleUserIds = ParseIds(promoCode.EligibleUserIds),
                EligibleInstitutionIds = ParseIds(promoCode.EligibleInstitutionIds),
                Stackable = promoCode.Stackable,
            },
            CreatedAt = promoCode.CreatedAt
        };
    }
}
