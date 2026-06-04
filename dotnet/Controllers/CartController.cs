using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Backend.Services;
using Backend.Models.Entities;
using Backend.Models.DTOs;
using Backend.Extensions;
using System.Text.RegularExpressions;

namespace Backend.Controllers;

/// <summary>
/// Shopping cart management controller
/// GET endpoints are public (anonymous cart), others require authentication
/// </summary>
[ApiController]
[Route("api/cart")]
public class CartController : ControllerBase
{
    private readonly ICartService _cartService;
    private readonly IAnonymousCartService _anonymousCartService; // ✅ Service for anonymous carts
    private readonly ILogger<CartController> _logger;

    public CartController(ICartService cartService, IAnonymousCartService anonymousCartService, ILogger<CartController> logger)
    {
        _cartService = cartService;
        _anonymousCartService = anonymousCartService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(CartResponseDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetCart([FromQuery] string? deviceId = null)
    {
        try
        {
            // 🔍 DEBUG: Afficher le token reçu et tous les claims disponibles
            var authHeader = Request.Headers.Authorization.ToString();
            var allClaims = string.Join(", ", User.Claims.Select(c => $"{c.Type}={c.Value}"));
            var rawToken = authHeader.StartsWith("Bearer ") ? authHeader.Substring(7) : "(NO BEARER PREFIX)";
            
            _logger.LogInformation(
                "[GetCart] 🔍 DEBUG - Requête panier reçue\n" +
                "DeviceId: {DeviceId}\n" +
                "Token Length: {TokenLength}\n" +
                "User.Identity.IsAuthenticated: {IsAuthenticated}\n" +
                "Timestamp: {Timestamp}",
                deviceId ?? "(NO DEVICE ID)",
                rawToken.Length,
                User.Identity?.IsAuthenticated ?? false,
                DateTime.UtcNow
            );
            
            // ✅ Optionnel: vérifier si l'utilisateur est authentifié
            int? userId = null;
            if (User.Identity?.IsAuthenticated ?? false)
            {
                // ✅ Utilisateur authentifié - récupérer son userId
                try
                {
                    userId = User.GetUserId();
                    var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
                    var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                    
                    _logger.LogInformation(
                        "[GetCart] 🛒 Authenticated user cart request\n" +
                        "UserId: {UserId}\n" +
                        "Email: {Email}\n" +
                        "Timestamp: {Timestamp}",
                        userId,
                        userEmail,
                        DateTime.UtcNow
                    );
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning(ex, "[GetCart] ❌ Failed to extract userId from token");
                    return Unauthorized(new 
                    { 
                        error = "Invalid token", 
                        details = ex.Message 
                    });
                }
            }
            else
            {
                // ✅ Utilisateur anonyme - pas authentifié
                _logger.LogInformation(
                    "[GetCart] 🛒 Anonymous cart request with deviceId: {DeviceId}\n" +
                    "Timestamp: {Timestamp}",
                    deviceId ?? "(NO DEVICE ID)",
                    DateTime.UtcNow
                );
            }

            // ✅ Récupérer les items du panier (différent selon auth)
            IEnumerable<CartItem> items;
            
            if (userId.HasValue && userId > 0)
            {
                // Panier authentifié: récupérer de la BD
                items = await _cartService.GetUserCartAsync(userId.Value);
                
                // ✅ DEBUG: Vérifier si les Subjects sont chargés
                var itemsWithMissingSubjects = items.Where(i => i.Subject == null).ToList();
                if (itemsWithMissingSubjects.Any())
                {
                    _logger.LogWarning(
                        "[GetCart] ⚠️ Found {Count} items without Subject data - requires Include in query",
                        itemsWithMissingSubjects.Count
                    );
                }
            }
            else if (!string.IsNullOrEmpty(deviceId))
            {
                // ✅ Panier anonyme: récupérer du service en mémoire par deviceId
                items = _anonymousCartService.GetAnonymousCart(deviceId);
                _logger.LogInformation(
                    "[GetCart] ✅ Panier anonyme récupéré depuis AnonymousCartService\n" +
                    "DeviceId: {DeviceId}\n" +
                    "ItemCount: {ItemCount}\n" +
                    "Timestamp: {Timestamp}",
                    deviceId,
                    items.Count(),
                    DateTime.UtcNow
                );
            }
            else
            {
                // Pas d'utilisateur, pas de deviceId: retourner panier vide
                items = new List<CartItem>();
                _logger.LogWarning(
                    "[GetCart] ⚠️ No userId and no deviceId provided - returning empty cart"
                );
            }

            // ✅ Convertir en DTO avec la structure complète du panier
            // ✅ CORRECTION: Validation STRICTE et enrichissement des données d'items
            var validatedItems = items
                .Where(item => 
                {
                    // Valider que l'item a un SubjectId valide
                    if (item.SubjectId <= 0)
                    {
                        _logger.LogWarning(
                            "[GetCart] ⚠️ CartItem {CartItemId} has invalid SubjectId: {SubjectId}",
                            item.Id, item.SubjectId
                        );
                        return false;
                    }
                    
                    // Valider que le Subject est chargé
                    if (item.Subject == null)
                    {
                        _logger.LogWarning(
                            "[GetCart] ⚠️ CartItem {CartItemId} missing Subject data",
                            item.Id
                        );
                        return false;
                    }
                    
                    return true;
                })
                .Select(item => new CartItemDto
                {
                    Id = item.Id > 0 ? item.Id : 0, // Mapper l'ID ou 0 si invalide
                    SubjectId = item.SubjectId,
                    // ✅ Utiliser le titre du Subject, jamais une chaîne vide
                    Title = !string.IsNullOrWhiteSpace(item.Subject?.Title) 
                        ? item.Subject.Title 
                        : $"Subject #{item.SubjectId}",
                    Description = item.Subject?.Description,
                    // ✅ Utiliser le prix du Subject si celui du CartItem est 0 ou invalide
                    Price = item.Price > 0 ? item.Price : (item.Subject?.Price ?? 0),
                    Image = item.Subject?.ThumbnailUrl,
                    Quantity = 1, // TODO: Gérer les quantités si nécessaire
                    AddedAt = item.AddedAt
                })
                .ToList();
            
            // ✅ Log les items filtrés (invalides)
            var invalidItemsCount = items.Count() - validatedItems.Count;
            if (invalidItemsCount > 0)
            {
                _logger.LogWarning(
                    "[GetCart] ⚠️ Filtered out {InvalidCount} invalid cart items",
                    invalidItemsCount
                );
            }
            
            var cartDto = new CartResponseDto
            {
                Items = validatedItems,
                ItemsCount = validatedItems.Count,
                Subtotal = validatedItems.Sum(i => i.Price),
                Discount = 0,
                Tax = validatedItems.Sum(i => i.Price) * 0.1m, // 10% tax
                Total = validatedItems.Sum(i => i.Price) * 1.1m, // subtotal + tax
                Currency = "XAF",
                UpdatedAt = DateTime.UtcNow
            };
            
            // ✅ Log adapté: distinguer panier anonyme vs panier utilisateur
            if (!userId.HasValue || userId == 0)
            {
                _logger.LogInformation(
                    "[GetCart] ✅ Anonymous cart retrieved (empty - expected)\n" +
                    "UserStatus: {UserStatus}\n" +
                    "ItemCount: {ItemCount}\n" +
                    "Note: {Note}\n" +
                    "IP: {IpAddress}\n" +
                    "Timestamp: {Timestamp}",
                    "Anonymous",
                    cartDto.ItemsCount,
                    "Frontend manages localStorage for anonymous users",
                    HttpContext.Connection.RemoteIpAddress,
                    DateTime.UtcNow
                );
            }
            else
            {
                _logger.LogInformation(
                    "[GetCart] ✅ Authenticated user cart retrieved\n" +
                    "UserId: {UserId}\n" +
                    "ItemCount: {ItemCount}\n" +
                    "Total: {Total} XAF\n" +
                    "Timestamp: {Timestamp}",
                    userId,
                    cartDto.ItemsCount,
                    cartDto.Total,
                    DateTime.UtcNow
                );
            }
            return Ok(cartDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[GetCart] ❌ Erreur lors de la récupération du panier\n" +
                "Exception: {ExceptionMessage}\n" +
                "StackTrace: {StackTrace}\n" +
                "Timestamp: {Timestamp}",
                ex.Message,
                ex.StackTrace,
                DateTime.UtcNow
            );
            return StatusCode(500, new 
            { 
                error = "Server error", 
                details = ex.Message 
            });
        }
    }

    /// <summary>
    /// Add item to shopping cart
    /// </summary>
    [HttpPost("items")]
    // ✅ REMOVED [Authorize] - Allow anonymous carts with deviceId
    [ProducesResponseType(typeof(CartItemDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> AddToCart([FromBody] AddToCartRequestDto request)
    {
        try
        {
            // Validation
            if (request == null)
            {
                return BadRequest(new { error = "Request body is required" });
            }

            if (request.SubjectId <= 0)
            {
                return BadRequest(new { error = "Invalid subject ID" });
            }

            if (request.Price < 0)
            {
                return BadRequest(new { error = "Invalid price" });
            }

            // ✅ Handle both authenticated users and anonymous users
            int userId = 0;
            bool isAuthenticated = false;
            
            // Check if user is authenticated
            if (User.Identity?.IsAuthenticated == true)
            {
                try
                {
                    userId = User.GetUserId();
                    isAuthenticated = true;
                    _logger.LogInformation("[AddToCart] Authenticated user {UserId} adding item: SubjectId={SubjectId}", 
                        userId, request.SubjectId);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning(ex, "[AddToCart] Authenticated user with invalid token");
                }
            }
            else if (!string.IsNullOrEmpty(request.DeviceId))
            {
                // Anonymous user with deviceId
                _logger.LogInformation("[AddToCart] Anonymous user {DeviceId} adding item: SubjectId={SubjectId}", 
                    request.DeviceId, request.SubjectId);
                // For anonymous users, we could use a hash of deviceId as negative userId
                // But for now, we'll return the item directly without storing in DB
                userId = 0; // Indicates anonymous
            }
            else
            {
                return BadRequest(new { error = "Authentication required or DeviceId must be provided" });
            }
            
            // ✅ For authenticated users, save to DB. For anonymous, add to in-memory service
            CartItem? added;
            if (isAuthenticated && userId > 0)
            {
                added = await _cartService.AddToCartAsync(userId, request.SubjectId, request.Price);
                if (added == null)
                {
                    return BadRequest(new { error = "Failed to add item to cart" });
                }
                _logger.LogInformation("[AddToCart] Item persisted for user {UserId}: CartItemId={CartItemId}", 
                    userId, added.Id);
            }
            else if (!string.IsNullOrEmpty(request.DeviceId))
            {
                // ✅ Anonymous user: persist to in-memory cache by deviceId
                added = _anonymousCartService.AddToAnonymousCart(
                    request.DeviceId, 
                    request.SubjectId, 
                    request.Price
                );
                _logger.LogInformation("[AddToCart] Item persisted to anonymous cart {DeviceId}: SubjectId={SubjectId}", 
                    request.DeviceId, request.SubjectId);
            }
            else
            {
                // Should not reach here (already validated above)
                return BadRequest(new { error = "Invalid request: no userId or deviceId" });
            }

            // Retourner le nouvel item
            var cartItemDto = new CartItemDto
            {
                Id = added.Id,
                SubjectId = added.SubjectId,
                Title = added.Subject?.Title ?? "",
                Description = added.Subject?.Description,
                Price = added.Price,
                Image = added.Subject?.ThumbnailUrl,
                Quantity = 1,
                AddedAt = added.AddedAt
            };

            return Ok(cartItemDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding item to cart");
            return StatusCode(500, new 
            { 
                error = "Server error", 
                details = ex.Message 
            });
        }
    }

    /// <summary>
    /// Remove item from shopping cart
    /// Supports both authenticated users and anonymous users with deviceId
    /// </summary>
    [HttpDelete("items/{id}")]
    // ✅ REMOVED [Authorize] - Support anonymous carts with deviceId
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> RemoveFromCart(string id, [FromQuery] string? deviceId = null)
    {
        try
        {
            // ✅ Parse parameter: can be either CartItemId (int) for authenticated or SubjectId for anonymous
            int subjectOrItemId;
            
            if (!int.TryParse(id, out subjectOrItemId))
            {
                // Not a pure integer, try to extract from format "cart_..._subjectId"
                var match = System.Text.RegularExpressions.Regex.Match(id, @"_(\d+)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var extractedId))
                {
                    subjectOrItemId = extractedId;
                }
                else
                {
                    return BadRequest(new { error = "Invalid item ID format" });
                }
            }

            // ✅ Check if user is authenticated
            int userId = 0;
            bool isAuthenticated = false;
            
            if (User.Identity?.IsAuthenticated == true)
            {
                try
                {
                    userId = User.GetUserId();
                    isAuthenticated = true;
                    _logger.LogInformation("[@RemoveFromCart] Removing cart item {CartItemId} for user {UserId}", subjectOrItemId, userId);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning(ex, "[RemoveFromCart] Unauthorized remove from cart attempt");
                    return Unauthorized(new { error = "Invalid token" });
                }
                
                var result = await _cartService.RemoveFromCartAsync(userId, subjectOrItemId);
                
                if (!result)
                {
                    _logger.LogWarning("[RemoveFromCart] Cart item {CartItemId} not found for user {UserId}", subjectOrItemId, userId);
                    return NotFound(new { error = "Cart item not found" });
                }

                _logger.LogInformation("[RemoveFromCart] Cart item {CartItemId} removed for user {UserId}", subjectOrItemId, userId);
                return NoContent();
            }
            else if (!string.IsNullOrEmpty(deviceId))
            {
                // ✅ Anonymous user: remove from anonymous cart service using SubjectId
                var result = _anonymousCartService.RemoveFromAnonymousCart(deviceId, subjectOrItemId);
                
                if (!result)
                {
                    _logger.LogWarning("[RemoveFromCart] Cart item {SubjectId} not found for anonymous {DeviceId}", subjectOrItemId, deviceId);
                    return NotFound(new { error = "Cart item not found" });
                }

                _logger.LogInformation("[RemoveFromCart] Cart item {SubjectId} removed for anonymous {DeviceId}", subjectOrItemId, deviceId);
                return NoContent();
            }
            else
            {
                return BadRequest(new { error = "Authentication required or DeviceId must be provided" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RemoveFromCart] Error removing item from cart: CartItemId={CartItemId}", id);
            return StatusCode(500, new 
            { 
                error = "Server error", 
                details = ex.Message 
            });
        }
    }
    /// <summary>
    /// Clear all items from shopping cart
    /// Supports both authenticated users and anonymous users with deviceId
    /// </summary>
    [HttpDelete]
    // ✅ REMOVED [Authorize] - Support anonymous carts with deviceId
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> ClearCart([FromQuery] string? deviceId = null)
    {
        try
        {
            // ✅ Check if user is authenticated
            int userId = 0;
            bool isAuthenticated = false;
            
            if (User.Identity?.IsAuthenticated == true)
            {
                try
                {
                    userId = User.GetUserId();
                    isAuthenticated = true;
                    _logger.LogInformation("[ClearCart] Clearing cart for user {UserId}", userId);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning(ex, "[ClearCart] Unauthorized clear cart attempt");
                    return Unauthorized(new { error = "Invalid token" });
                }
                
                var result = await _cartService.ClearCartAsync(userId);

                _logger.LogInformation("[ClearCart] Cart cleared for user {UserId}", userId);
                
                return Ok(new 
                { 
                    success = true, 
                    message = "Cart cleared successfully" 
                });
            }
            else if (!string.IsNullOrEmpty(deviceId))
            {
                // ✅ Anonymous user: clear anonymous cart
                _anonymousCartService.ClearAnonymousCart(deviceId);

                _logger.LogInformation("[ClearCart] Cart cleared for anonymous {DeviceId}", deviceId);
                
                return Ok(new 
                { 
                    success = true, 
                    message = "Cart cleared successfully" 
                });
            }
            else
            {
                return BadRequest(new { error = "Authentication required or DeviceId must be provided" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ClearCart] Error clearing cart");
            return StatusCode(500, new 
            { 
                error = "Server error", 
                details = ex.Message 
            });
        }
    }

    /// <summary>
    /// Apply promo code to cart
    /// </summary>
    [HttpPost("promo")]
    // ✅ REMOVED [Authorize] - Allow anonymous users to apply promo codes
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> ApplyPromoCode([FromBody] ApplyPromoCodeDto request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request?.Code))
            {
                return BadRequest(new { error = "Promo code is required" });
            }

            int userId = User.GetUserId();
            _logger.LogInformation("Applying promo code '{Code}' for user {UserId}", request.Code, userId);

            // TODO: Implémenter la logique de validation du code promo
            // Pour l'instant, retourner un message simple
            
            return Ok(new 
            { 
                success = true, 
                message = "Promo code applied successfully",
                discount = 0m // TODO: Calculer le discount réel
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = "Invalid token" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying promo code");
            return StatusCode(500, new { error = "Server error" });
        }
    }

    /// <summary>
    /// Remove promo code from cart
    /// </summary>
    [HttpDelete("promo")]
    // ✅ REMOVED [Authorize] - Allow anonymous users to remove promo codes
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> RemovePromoCode()
    {
        try
        {
            int userId = User.GetUserId();
            _logger.LogInformation("Removing promo code for user {UserId}", userId);

            // TODO: Implémenter la logique de suppression du code promo
            
            return Ok(new 
            { 
                success = true, 
                message = "Promo code removed successfully" 
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = "Invalid token" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing promo code");
            return StatusCode(500, new { error = "Server error" });
        }
    }

    /// <summary>
    /// Sync local cart with server (for offline/online sync)
    /// </summary>
    [HttpPost("sync")]
    // ✅ REMOVED [Authorize] - Allow anonymous users to sync their carts
    [ProducesResponseType(typeof(CartResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> SyncCart([FromBody] CartResponseDto localCart)
    {
        try
        {
            if (localCart == null)
            {
                return BadRequest(new { error = "Cart data is required" });
            }

            int userId = User.GetUserId();
            _logger.LogInformation("Syncing cart for user {UserId}: {ItemCount} items", 
                userId, localCart.Items.Count);

            // TODO: Implémenter la logique de synchronisation
            // Pour l'instant, retourner le panier actuel du serveur
            var serverItems = await _cartService.GetUserCartAsync(userId);
            
            var cartDto = new CartResponseDto
            {
                Items = serverItems.Select(item => new CartItemDto
                {
                    Id = item.Id,
                    SubjectId = item.SubjectId,
                    Title = item.Subject?.Title ?? "",
                    Description = item.Subject?.Description,
                    Price = item.Price,
                    Image = item.Subject?.ThumbnailUrl,
                    Quantity = 1,
                    AddedAt = item.AddedAt
                }).ToList(),
                ItemsCount = serverItems.Count(),
                Subtotal = serverItems.Sum(i => i.Price),
                Discount = 0,
                Tax = serverItems.Sum(i => i.Price) * 0.1m,
                Total = serverItems.Sum(i => i.Price) * 1.1m,
                Currency = "XAF",
                UpdatedAt = DateTime.UtcNow
            };

            return Ok(cartDto);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = "Invalid token" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing cart");
            return StatusCode(500, new { error = "Server error" });
        }
    }
}