using Backend.Models.Entities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Backend.Services;

/// <summary>
/// Service pour gérer les paniers anonymes en mémoire (par deviceId)
/// ✅ Solution temporaire: stocks les paniers anonymes pendant la session
/// La vraie solution serait Redis ou une base session (todo pour production)
/// </summary>
public interface IAnonymousCartService
{
    // Récupérer le panier d'un utilisateur anonyme par deviceId
    List<CartItem> GetAnonymousCart(string deviceId);
    
    // Ajouter un article au panier anonyme
    CartItem AddToAnonymousCart(string deviceId, int subjectId, decimal price, string? title = null, string? description = null, string? thumbnailUrl = null);
    
    // Supprimer un article
    bool RemoveFromAnonymousCart(string deviceId, int subjectId);
    
    // Vider le panier
    void ClearAnonymousCart(string deviceId);
    
    // Calculer le total
    decimal GetAnonymousCartTotal(string deviceId);
    
    // Vérifier si un article existe
    bool IsItemInAnonymousCart(string deviceId, int subjectId);
}

public class AnonymousCartService : IAnonymousCartService
{
    // ✅ Dictionnaire en mémoire : deviceId → liste d'articles du panier
    // Format: "device_xxx" → List<CartItem>
    private static readonly Dictionary<string, List<CartItem>> _anonymousCarts = new();
    private static readonly object _lock = new object(); // Thread-safe
    private readonly ILogger<AnonymousCartService> _logger;

    public AnonymousCartService(ILogger<AnonymousCartService> logger)
    {
        _logger = logger;
    }

    public List<CartItem> GetAnonymousCart(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            _logger.LogWarning("[AnonymousCartService] GetAnonymousCart: deviceId is null or empty");
            return new List<CartItem>();
        }

        lock (_lock)
        {
            if (_anonymousCarts.TryGetValue(deviceId, out var cart))
            {
                _logger.LogInformation("[AnonymousCartService] GetAnonymousCart: Found cart for {DeviceId} with {ItemCount} items",
                    deviceId, cart.Count);
                return new List<CartItem>(cart); // Retourner une copie
            }

            _logger.LogInformation("[AnonymousCartService] GetAnonymousCart: No cart found for {DeviceId}", deviceId);
            return new List<CartItem>();
        }
    }

    public CartItem AddToAnonymousCart(string deviceId, int subjectId, decimal price, string? title = null, string? description = null, string? thumbnailUrl = null)
    {
        if (string.IsNullOrEmpty(deviceId))
            throw new ArgumentNullException(nameof(deviceId));

        lock (_lock)
        {
            // ✅ Créer ou récupérer le panier de l'utilisateur anonyme
            if (!_anonymousCarts.ContainsKey(deviceId))
            {
                _anonymousCarts[deviceId] = new List<CartItem>();
                _logger.LogInformation("[AnonymousCartService] Created new cart for {DeviceId}", deviceId);
            }

            var cart = _anonymousCarts[deviceId];

            // ✅ Chercher si l'article existing
            var existingItem = cart.FirstOrDefault(c => c.SubjectId == subjectId);
            if (existingItem != null)
            {
                _logger.LogInformation("[AnonymousCartService] Item {SubjectId} already in cart for {DeviceId}, not adding duplicate", 
                    subjectId, deviceId);
                return existingItem;
            }

            // ✅ Créer un nouvel article temporaire
            var newItem = new CartItem
            {
                Id = 0, // Aucun ID BD (panier temporaire)
                UserId = 0, // Anonyme
                SubjectId = subjectId,
                Price = price,
                AddedAt = DateTime.UtcNow,
                Subject = new Subject
                {
                    Id = subjectId,
                    Title = title ?? "",
                    Description = description,
                    ThumbnailUrl = thumbnailUrl
                },
                User = new User 
                { 
                    Email = "anonymous@local" // ✅ Required field - placeholder for anonymous user
                }
            };

            cart.Add(newItem);

            _logger.LogInformation("[AnonymousCartService] Added item {SubjectId} to cart for {DeviceId}", 
                subjectId, deviceId);

            return newItem;
        }
    }

    public bool RemoveFromAnonymousCart(string deviceId, int subjectId)
    {
        if (string.IsNullOrEmpty(deviceId))
            throw new ArgumentNullException(nameof(deviceId));

        lock (_lock)
        {
            if (!_anonymousCarts.TryGetValue(deviceId, out var cart))
            {
                _logger.LogWarning("[AnonymousCartService] Cart not found for {DeviceId}", deviceId);
                return false;
            }

            var itemToRemove = cart.FirstOrDefault(c => c.SubjectId == subjectId);
            if (itemToRemove == null)
            {
                _logger.LogWarning("[AnonymousCartService] Item {SubjectId} not found in cart for {DeviceId}", 
                    subjectId, deviceId);
                return false;
            }

            cart.Remove(itemToRemove);
            _logger.LogInformation("[AnonymousCartService] Removed item {SubjectId} from cart for {DeviceId}", 
                subjectId, deviceId);

            // ✅ Optionnel : nettoyer les paniers vides
            if (cart.Count == 0)
            {
                _anonymousCarts.Remove(deviceId);
                _logger.LogInformation("[AnonymousCartService] Cleaned up empty cart for {DeviceId}", deviceId);
            }

            return true;
        }
    }

    public void ClearAnonymousCart(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return;

        lock (_lock)
        {
            if (_anonymousCarts.Remove(deviceId))
            {
                _logger.LogInformation("[AnonymousCartService] Cleared cart for {DeviceId}", deviceId);
            }
        }
    }

    public decimal GetAnonymousCartTotal(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return 0;

        lock (_lock)
        {
            if (!_anonymousCarts.TryGetValue(deviceId, out var cart))
                return 0;

            return cart.Sum(c => c.Price);
        }
    }

    public bool IsItemInAnonymousCart(string deviceId, int subjectId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return false;

        lock (_lock)
        {
            if (!_anonymousCarts.TryGetValue(deviceId, out var cart))
                return false;

            return cart.Any(c => c.SubjectId == subjectId);
        }
    }
}
