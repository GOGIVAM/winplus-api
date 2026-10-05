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

    public PromoCodeService(ApplicationDbContext context, ILogger<PromoCodeService> logger)
    {
        _context = context;
        _logger = logger;
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
                DiscountType = request.DiscountType,
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
                CreatedBy = adminUserId,
                CreatedAt = DateTime.UtcNow
            };

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

    public async Task<PromoCodeValidationResult> ValidatePromoCodeAsync(int userId, ValidatePromoCodeRequest request)
    {
        try
        {
            var promoCode = await _context.PromoCodes
                .Include(p => p.Usages)
                .FirstOrDefaultAsync(p => p.Code == request.Code.ToUpper());

            var result = new PromoCodeValidationResult
            {
                IsValid = false,
                DiscountAmount = 0,
                FinalAmount = request.CartTotal
            };

            // Validations
            if (promoCode == null)
            {
                result.ErrorMessage = "Invalid promo code";
                return result;
            }

            if (!promoCode.IsActive)
            {
                result.ErrorMessage = "This promo code is no longer active";
                return result;
            }

            var now = DateTime.UtcNow;
            if (promoCode.ValidFrom > now)
            {
                result.ErrorMessage = $"This promo code is not valid yet (valid from {promoCode.ValidFrom:yyyy-MM-dd})";
                return result;
            }

            if (promoCode.ValidUntil.HasValue && promoCode.ValidUntil < now)
            {
                result.ErrorMessage = "This promo code has expired";
                return result;
            }

            // Check global usage limit
            if (promoCode.UsageLimit.HasValue && promoCode.UsageCount >= promoCode.UsageLimit.Value)
            {
                result.ErrorMessage = "This promo code has reached its usage limit";
                return result;
            }

            // Check per-user usage limit
            var userUsageCount = await _context.PromoCodeUsages
                .Where(u => u.PromoCodeId == promoCode.Id && u.UserId == userId)
                .CountAsync();

            if (promoCode.PerUserLimit.HasValue && userUsageCount >= promoCode.PerUserLimit.Value)
            {
                result.ErrorMessage = "You have already used this promo code the maximum number of times";
                return result;
            }

            // Check minimum purchase amount
            if (promoCode.MinimumPurchase.HasValue && request.CartTotal < promoCode.MinimumPurchase.Value)
            {
                result.ErrorMessage = $"Minimum purchase of {promoCode.MinimumPurchase:C} required";
                return result;
            }

            // Check applicable subjects
            if (!string.IsNullOrEmpty(promoCode.ApplicableSubjectIds))
            {
                var applicableIds = JsonSerializer.Deserialize<List<int>>(promoCode.ApplicableSubjectIds);
                var hasApplicableSubject = request.SubjectIds?.Any(id => applicableIds?.Contains(id) ?? false) ?? false;
                
                if (!hasApplicableSubject)
                {
                    result.ErrorMessage = "This promo code is not applicable to items in your cart";
                    return result;
                }
            }

            // Calculate discount
            decimal discount = 0;
            if (promoCode.DiscountType == "Percentage")
            {
                discount = request.CartTotal * (promoCode.DiscountValue / 100);
                
                // Apply maximum discount cap if defined
                if (promoCode.MaximumDiscount.HasValue && discount > promoCode.MaximumDiscount.Value)
                {
                    discount = promoCode.MaximumDiscount.Value;
                }
            }
            else if (promoCode.DiscountType == "FixedAmount")
            {
                discount = Math.Min(promoCode.DiscountValue, request.CartTotal);
            }

            result.IsValid = true;
            result.DiscountAmount = Math.Round(discount, 2);
            result.FinalAmount = Math.Max(0, request.CartTotal - result.DiscountAmount);
            result.PromoCode = MapToDto(promoCode);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating promo code");
            throw;
        }
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
            promoCode.DiscountType = request.DiscountType;
            promoCode.DiscountValue = request.DiscountValue;
            promoCode.MinimumPurchase = request.MinimumPurchase;
            promoCode.MaximumDiscount = request.MaximumDiscount;
            promoCode.UsageLimit = request.UsageLimit;
            promoCode.ValidUntil = Utc(request.ValidUntil);
            promoCode.ApplicableSubjectIds = request.ApplicableSubjectIds != null
                ? JsonSerializer.Serialize(request.ApplicableSubjectIds)
                : null;
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
            CreatedAt = promoCode.CreatedAt
        };
    }
}
