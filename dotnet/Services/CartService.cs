using Backend.Models.Entities;
using Backend.Repositories;

namespace Backend.Services;

public interface ICartService
{
    Task<IEnumerable<CartItem>> GetUserCartAsync(int userId);
    Task<CartItem> AddToCartAsync(int userId, int subjectId, decimal price);
    Task<bool> RemoveFromCartAsync(int userId, int subjectId);
    Task<bool> RemoveCartItemAsync(int cartItemId);
    Task<bool> ClearCartAsync(int userId);
    Task<decimal> GetCartTotalAsync(int userId);
    Task<int> GetCartCountAsync(int userId);
    Task<bool> IsItemInCartAsync(int userId, int subjectId);

    // ── Panier anonyme (avant connexion), persisté en base par DeviceId ──────
    Task<IEnumerable<CartItem>> GetAnonymousCartAsync(string deviceId);
    Task<CartItem> AddToAnonymousCartAsync(string deviceId, int subjectId, decimal price);
    Task<bool> RemoveFromAnonymousCartAsync(string deviceId, int subjectId);
    Task<bool> ClearAnonymousCartAsync(string deviceId);
    Task<decimal> GetAnonymousCartTotalAsync(string deviceId);

    /// <summary>Réassigne en base le panier anonyme d'un DeviceId à un UserId (connexion).</summary>
    Task<IEnumerable<CartItem>> MergeAnonymousCartAsync(int userId, string deviceId);
}

public class CartService : ICartService
{
    private readonly ICartRepository _cartRepository;
    private readonly ISubjectRepository _subjectRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<CartService> _logger;

    public CartService(
        ICartRepository cartRepository,
        ISubjectRepository subjectRepository,
        IUserRepository userRepository,
        ILogger<CartService> logger)
    {
        _cartRepository = cartRepository;
        _subjectRepository = subjectRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<IEnumerable<CartItem>> GetUserCartAsync(int userId)
    {
        try
        {
            // ✅ Récupérer les items avec les Subjects chargés
            var items = await _cartRepository.GetByUserIdAsync(userId);

            // ✅ Vérifier si les Subjects manquent et les charger au besoin
            var itemsList = items.ToList();
            for (int i = 0; i < itemsList.Count; i++)
            {
                if (itemsList[i].Subject == null && itemsList[i].SubjectId > 0)
                {
                    _logger.LogWarning(
                        "[GetUserCartAsync] ⚠️ Subject not loaded for CartItem {CartItemId}, loading manually",
                        itemsList[i].Id
                    );

                    // Charger le Subject directement
                    var subject = await _subjectRepository.GetByIdAsync(itemsList[i].SubjectId);
                    if (subject != null)
                    {
                        itemsList[i].Subject = subject;
                        _logger.LogInformation(
                            "[GetUserCartAsync] ✅ Subject loaded manually for CartItem {CartItemId}: {Title}",
                            itemsList[i].Id,
                            subject.Title
                        );
                    }
                }
            }

            return itemsList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cart for user {UserId}", userId);
            return Enumerable.Empty<CartItem>();
        }
    }

    public async Task<CartItem> AddToCartAsync(int userId, int subjectId, decimal price)
    {
        try
        {
            // Check if subject exists
            var subject = await _subjectRepository.GetByIdAsync(subjectId);
            if (subject == null)
                throw new InvalidOperationException($"Subject {subjectId} not found");

            // Check if already in cart
            var existing = await _cartRepository.GetByUserAndSubjectAsync(userId, subjectId);
            if (existing != null)
            {
                _logger.LogInformation("Item already in cart for user {UserId}", userId);
                return existing;
            }

            // Get user (for navigation property)
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                throw new InvalidOperationException($"User {userId} not found");

            var cartItem = new CartItem
            {
                UserId = userId,
                SubjectId = subjectId,
                Price = price > 0 ? price : subject.Price,
                User = user,
                Subject = subject
            };

            return await _cartRepository.AddAsync(cartItem);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding item to cart");
            throw;
        }
    }

    public async Task<bool> RemoveFromCartAsync(int userId, int subjectId)
    {
        try
        {
            return await _cartRepository.RemoveByUserAndSubjectAsync(userId, subjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing item from cart");
            throw;
        }
    }

    public async Task<bool> RemoveCartItemAsync(int cartItemId)
    {
        try
        {
            return await _cartRepository.RemoveAsync(cartItemId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cart item {CartItemId}", cartItemId);
            throw;
        }
    }

    public async Task<bool> ClearCartAsync(int userId)
    {
        try
        {
            return await _cartRepository.ClearUserCartAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cart for user {UserId}", userId);
            throw;
        }
    }

    public async Task<decimal> GetCartTotalAsync(int userId)
    {
        try
        {
            return await _cartRepository.GetTotalAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating cart total");
            return 0;
        }
    }

    public async Task<int> GetCartCountAsync(int userId)
    {
        try
        {
            return await _cartRepository.GetCountAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cart count");
            return 0;
        }
    }

    public async Task<bool> IsItemInCartAsync(int userId, int subjectId)
    {
        try
        {
            var item = await _cartRepository.GetByUserAndSubjectAsync(userId, subjectId);
            return item != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if item in cart");
            return false;
        }
    }

    // ── Panier anonyme ──────────────────────────────────────────────────────

    public async Task<IEnumerable<CartItem>> GetAnonymousCartAsync(string deviceId)
    {
        try
        {
            return await _cartRepository.GetByDeviceIdAsync(deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting anonymous cart for device {DeviceId}", deviceId);
            return Enumerable.Empty<CartItem>();
        }
    }

    public async Task<CartItem> AddToAnonymousCartAsync(string deviceId, int subjectId, decimal price)
    {
        try
        {
            var subject = await _subjectRepository.GetByIdAsync(subjectId);
            if (subject == null)
                throw new InvalidOperationException($"Subject {subjectId} not found");

            var existing = await _cartRepository.GetByDeviceAndSubjectAsync(deviceId, subjectId);
            if (existing != null)
            {
                _logger.LogInformation("Item already in anonymous cart for device {DeviceId}", deviceId);
                return existing;
            }

            var cartItem = new CartItem
            {
                DeviceId = deviceId,
                SubjectId = subjectId,
                Price = price > 0 ? price : subject.Price,
                Subject = subject
            };

            return await _cartRepository.AddAsync(cartItem);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding item to anonymous cart");
            throw;
        }
    }

    public async Task<bool> RemoveFromAnonymousCartAsync(string deviceId, int subjectId)
    {
        try
        {
            return await _cartRepository.RemoveByDeviceAndSubjectAsync(deviceId, subjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing item from anonymous cart");
            throw;
        }
    }

    public async Task<bool> ClearAnonymousCartAsync(string deviceId)
    {
        try
        {
            return await _cartRepository.ClearDeviceCartAsync(deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing anonymous cart for device {DeviceId}", deviceId);
            throw;
        }
    }

    public async Task<decimal> GetAnonymousCartTotalAsync(string deviceId)
    {
        try
        {
            var items = await _cartRepository.GetByDeviceIdAsync(deviceId);
            return items.Sum(c => c.Price);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating anonymous cart total for device {DeviceId}", deviceId);
            return 0;
        }
    }

    /// <summary>
    /// Fusion du panier anonyme (persisté en base sous DeviceId) avec le compte
    /// de l'utilisateur qui vient de se connecter — réassignation en base
    /// (ReassignDeviceCartToUserAsync), plus de liste en mémoire à transporter.
    /// </summary>
    public async Task<IEnumerable<CartItem>> MergeAnonymousCartAsync(int userId, string deviceId)
    {
        try
        {
            var reassigned = await _cartRepository.ReassignDeviceCartToUserAsync(deviceId, userId);
            _logger.LogInformation(
                "[MergeAnonymousCart] ✅ {ReassignedCount} article(s) réassigné(s) du device {DeviceId} vers l'utilisateur {UserId}",
                reassigned, deviceId, userId);

            return await GetUserCartAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MergeAnonymousCart] Error merging anonymous cart for user {UserId}", userId);
            throw;
        }
    }
}
