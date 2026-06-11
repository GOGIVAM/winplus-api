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
                ValidFrom = request.ValidFrom,
                ValidUntil = request.ValidUntil,
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

    public async Task<bool> ApplyPromoCodeAsync(int userId, int orderId, string code)
    {
        try
        {
            var promoCode = await _context.PromoCodes
                .FirstOrDefaultAsync(p => p.Code == code.ToUpper());

            if (promoCode == null)
                return false;

            var order = await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

            if (order == null)
                return false;

            // Validate before applying
            var validation = await ValidatePromoCodeAsync(userId, new ValidatePromoCodeRequest
            {
                Code = code,
                CartTotal = order.TotalAmount,
                SubjectIds = order.Items?.Select(oi => oi.SubjectId).ToList()
            });

            if (!validation.IsValid)
                return false;

            // Record usage
            var usage = new PromoCodeUsage
            {
                PromoCodeId = promoCode.Id,
                UserId = userId,
                OrderId = orderId,
                DiscountAmount = validation.DiscountAmount,
                UsedAt = DateTime.UtcNow
            };

            // Update order with discount
            order.DiscountAmount = validation.DiscountAmount;
            order.TotalAmount = validation.FinalAmount;

            // Increment usage count
            promoCode.UsageCount++;

            _context.PromoCodeUsages.Add(usage);
            _context.Orders.Update(order);
            _context.PromoCodes.Update(promoCode);

            await _context.SaveChangesAsync();

            _logger.LogInformation("Promo code {Code} applied to order {OrderId} by user {UserId}", 
                code, orderId, userId);

            return true;
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
