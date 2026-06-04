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
    // ✅ Fusion panier anonyme → utilisateur authentifié
    Task<IEnumerable<CartItem>> MergeAnonymousCartAsync(int userId, List<CartItem> anonymousItems);
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

    /// <summary>
    /// ✅ Fusion du panier anonyme avec le panier de l'utilisateur authentifié
    /// Ajoute tous les articles du panier anonyme au panier de l'utilisateur
    /// en évitant les doublons
    /// </summary>
    public async Task<IEnumerable<CartItem>> MergeAnonymousCartAsync(int userId, List<CartItem> anonymousItems)
    {
        try
        {
            if (!anonymousItems.Any())
            {
                _logger.LogInformation("[MergeAnonymousCart] No items to merge for user {UserId}", userId);
                return await GetUserCartAsync(userId);
            }

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                throw new InvalidOperationException($"User {userId} not found");

            int mergedCount = 0;
            foreach (var anonItem in anonymousItems)
            {
                // Vérifier si l'article existe déjà
                var existing = await _cartRepository.GetByUserAndSubjectAsync(userId, anonItem.SubjectId);
                
                if (existing == null)
                {
                    // Article n'existe pas: l'ajouter
                    var newItem = new CartItem
                    {
                        UserId = userId,
                        SubjectId = anonItem.SubjectId,
                        Price = anonItem.Price,
                        User = user,
                        Subject = anonItem.Subject,
                        AddedAt = DateTime.UtcNow
                    };

                    await _cartRepository.AddAsync(newItem);
                    mergedCount++;
                    _logger.LogInformation("[MergeAnonymousCart] Added item {SubjectId} for user {UserId}", 
                        anonItem.SubjectId, userId);
                }
                else
                {
                    _logger.LogInformation("[MergeAnonymousCart] Item {SubjectId} already in cart for user {UserId}, skipping", 
                        anonItem.SubjectId, userId);
                }
            }

            _logger.LogInformation("[MergeAnonymousCart] ✅ Panier anonyme fusionné avec succès\n" +
                "UserId: {UserId}\n" +
                "ItemsMerged: {MergedCount}\n" +
                "TotalAnonymousItems: {TotalItems}\n" +
                "Timestamp: {Timestamp}",
                userId,
                mergedCount,
                anonymousItems.Count,
                DateTime.UtcNow
            );

            return await GetUserCartAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MergeAnonymousCart] Error merging anonymous cart for user {UserId}", userId);
            throw;
        }
    }
}
