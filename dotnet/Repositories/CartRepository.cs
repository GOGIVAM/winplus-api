using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class CartRepository : ICartRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CartRepository> _logger;

    public CartRepository(ApplicationDbContext context, ILogger<CartRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<CartItem?> GetByIdAsync(int id)
    {
        try
        {
            return await _context.CartItems
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cart item by id {CartItemId}", id);
            return null;
        }
    }

    public async Task<IEnumerable<CartItem>> GetByUserIdAsync(int userId)
    {
        try
        {
            return await _context.CartItems
                .Where(c => c.UserId == userId)
                .Include(c => c.Subject)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cart items for user {UserId}", userId);
            return Enumerable.Empty<CartItem>();
        }
    }

    public async Task<CartItem?> GetByUserAndSubjectAsync(int userId, int subjectId)
    {
        try
        {
            return await _context.CartItems
                .Include(c => c.Subject)
                .FirstOrDefaultAsync(c => c.UserId == userId && c.SubjectId == subjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cart item for user {UserId} and subject {SubjectId}", userId, subjectId);
            return null;
        }
    }

    public async Task<CartItem> AddAsync(CartItem cartItem)
    {
        try
        {
            cartItem.AddedAt = DateTime.UtcNow;
            
            _context.CartItems.Add(cartItem);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Cart item added for user {UserId}", cartItem.UserId);
            return cartItem;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding cart item");
            throw;
        }
    }

    public async Task<CartItem> UpdateAsync(CartItem cartItem)
    {
        try
        {
            _context.CartItems.Update(cartItem);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Cart item {CartItemId} updated", cartItem.Id);
            return cartItem;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating cart item {CartItemId}", cartItem.Id);
            throw;
        }
    }

    public async Task<bool> RemoveAsync(int id)
    {
        try
        {
            var item = await _context.CartItems.FindAsync(id);
            if (item == null)
                return false;

            _context.CartItems.Remove(item);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Cart item {CartItemId} removed", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cart item {CartItemId}", id);
            throw;
        }
    }

    public async Task<bool> RemoveByUserAndSubjectAsync(int userId, int subjectId)
    {
        try
        {
            var item = await _context.CartItems
                .FirstOrDefaultAsync(c => c.UserId == userId && c.SubjectId == subjectId);
            
            if (item == null)
                return false;

            _context.CartItems.Remove(item);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Cart item removed for user {UserId} and subject {SubjectId}", userId, subjectId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cart item for user {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> ClearUserCartAsync(int userId)
    {
        try
        {
            var items = await _context.CartItems
                .Where(c => c.UserId == userId)
                .ToListAsync();
            
            if (items.Count == 0)
                return true;

            _context.CartItems.RemoveRange(items);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Cart cleared for user {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cart for user {UserId}", userId);
            throw;
        }
    }

    public async Task<decimal> GetTotalAsync(int userId)
    {
        try
        {
            return await _context.CartItems
                .Where(c => c.UserId == userId)
                .SumAsync(c => c.Price);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating cart total for user {UserId}", userId);
            return 0;
        }
    }

    public async Task<int> GetCountAsync(int userId)
    {
        try
        {
            return await _context.CartItems
                .Where(c => c.UserId == userId)
                .CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting cart items for user {UserId}", userId);
            return 0;
        }
    }

    // ── Panier anonyme ──────────────────────────────────────────────────────

    public async Task<IEnumerable<CartItem>> GetByDeviceIdAsync(string deviceId)
    {
        try
        {
            return await _context.CartItems
                .Where(c => c.DeviceId == deviceId)
                .Include(c => c.Subject)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cart items for device {DeviceId}", deviceId);
            return Enumerable.Empty<CartItem>();
        }
    }

    public async Task<CartItem?> GetByDeviceAndSubjectAsync(string deviceId, int subjectId)
    {
        try
        {
            return await _context.CartItems
                .Include(c => c.Subject)
                .FirstOrDefaultAsync(c => c.DeviceId == deviceId && c.SubjectId == subjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cart item for device {DeviceId} and subject {SubjectId}", deviceId, subjectId);
            return null;
        }
    }

    public async Task<bool> RemoveByDeviceAndSubjectAsync(string deviceId, int subjectId)
    {
        try
        {
            var item = await _context.CartItems
                .FirstOrDefaultAsync(c => c.DeviceId == deviceId && c.SubjectId == subjectId);

            if (item == null)
                return false;

            _context.CartItems.Remove(item);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Cart item removed for device {DeviceId} and subject {SubjectId}", deviceId, subjectId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cart item for device {DeviceId}", deviceId);
            throw;
        }
    }

    public async Task<bool> ClearDeviceCartAsync(string deviceId)
    {
        try
        {
            var items = await _context.CartItems
                .Where(c => c.DeviceId == deviceId)
                .ToListAsync();

            if (items.Count == 0)
                return true;

            _context.CartItems.RemoveRange(items);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Cart cleared for device {DeviceId}", deviceId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cart for device {DeviceId}", deviceId);
            throw;
        }
    }

    public async Task<int> ReassignDeviceCartToUserAsync(string deviceId, int userId)
    {
        try
        {
            var deviceItems = await _context.CartItems
                .Where(c => c.DeviceId == deviceId)
                .ToListAsync();

            if (deviceItems.Count == 0)
                return 0;

            var existingSubjectIds = await _context.CartItems
                .Where(c => c.UserId == userId)
                .Select(c => c.SubjectId)
                .ToListAsync();

            var reassigned = 0;
            foreach (var item in deviceItems)
            {
                if (existingSubjectIds.Contains(item.SubjectId))
                {
                    // Déjà dans le panier réel de l'utilisateur : le doublon
                    // anonyme est superflu, pas une erreur à propager.
                    _context.CartItems.Remove(item);
                    continue;
                }

                item.UserId = userId;
                item.DeviceId = null;
                reassigned++;
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation(
                "Reassigned {Count} anonymous cart item(s) from device {DeviceId} to user {UserId}",
                reassigned, deviceId, userId);
            return reassigned;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reassigning cart from device {DeviceId} to user {UserId}", deviceId, userId);
            throw;
        }
    }
}
