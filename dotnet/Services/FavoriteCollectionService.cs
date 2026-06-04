using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public interface IFavoriteCollectionService
{
    Task<FavoriteCollectionDto> CreateCollectionAsync(int userId, CreateFavoriteCollectionRequest request);
    Task<FavoriteCollectionDto> UpdateCollectionAsync(int userId, int collectionId, UpdateFavoriteCollectionRequest request);
    Task<bool> DeleteCollectionAsync(int userId, int collectionId);
    Task<List<FavoriteCollectionDto>> GetUserCollectionsAsync(int userId);
    Task<FavoriteCollectionDto> GetCollectionWithFavoritesAsync(int userId, int collectionId);
    Task<bool> AddFavoriteToCollectionAsync(int userId, int favoriteId, int collectionId);
    Task<bool> RemoveFavoriteFromCollectionAsync(int userId, int favoriteId);
    Task<bool> ReorderCollectionsAsync(int userId, List<int> collectionIds);
}

public class FavoriteCollectionService : IFavoriteCollectionService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<FavoriteCollectionService> _logger;

    public FavoriteCollectionService(ApplicationDbContext context, ILogger<FavoriteCollectionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<FavoriteCollectionDto> CreateCollectionAsync(int userId, CreateFavoriteCollectionRequest request)
    {
        try
        {
            // Check if collection with same name exists
            var existing = await _context.FavoriteCollections
                .FirstOrDefaultAsync(c => c.UserId == userId && c.Name == request.Name);
            
            if (existing != null)
            {
                throw new InvalidOperationException($"Collection '{request.Name}' already exists");
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                throw new InvalidOperationException($"User {userId} not found");
            }

            var collection = new FavoriteCollection
            {
                UserId = userId,
                User = user,
                Name = request.Name,
                Description = request.Description,
                Color = request.Color ?? "#3B82F6", // Blue by default
                Icon = request.Icon ?? "folder",
                CreatedAt = DateTime.UtcNow,
                Order = 0
            };

            _context.FavoriteCollections.Add(collection);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Collection {CollectionId} created by user {UserId}", collection.Id, userId);

            return MapToDto(collection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating collection");
            throw;
        }
    }

    public async Task<FavoriteCollectionDto> UpdateCollectionAsync(int userId, int collectionId, UpdateFavoriteCollectionRequest request)
    {
        try
        {
            var collection = await _context.FavoriteCollections
                .FirstOrDefaultAsync(c => c.Id == collectionId && c.UserId == userId);

            if (collection == null)
            {
                throw new KeyNotFoundException("Collection not found");
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                // Check if new name is already used
                var duplicate = await _context.FavoriteCollections
                    .FirstOrDefaultAsync(c => c.UserId == userId && c.Name == request.Name && c.Id != collectionId);
                
                if (duplicate != null)
                {
                    throw new InvalidOperationException($"Collection '{request.Name}' already exists");
                }
                
                collection.Name = request.Name;
            }

            if (request.Description != null)
                collection.Description = request.Description;

            if (request.Color != null)
                collection.Color = request.Color;

            if (request.Icon != null)
                collection.Icon = request.Icon;

            collection.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Collection {CollectionId} updated", collectionId);

            return MapToDto(collection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating collection {CollectionId}", collectionId);
            throw;
        }
    }

    public async Task<bool> DeleteCollectionAsync(int userId, int collectionId)
    {
        try
        {
            var collection = await _context.FavoriteCollections
                .Include(c => c.Favorites)
                .FirstOrDefaultAsync(c => c.Id == collectionId && c.UserId == userId);

            if (collection == null)
                return false;

            // Remove favorites from collection (don't delete the favorites)
            foreach (var favorite in collection.Favorites)
            {
                favorite.CollectionId = null;
            }

            _context.FavoriteCollections.Remove(collection);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Collection {CollectionId} deleted", collectionId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting collection {CollectionId}", collectionId);
            throw;
        }
    }

    public async Task<List<FavoriteCollectionDto>> GetUserCollectionsAsync(int userId)
    {
        try
        {
            var collections = await _context.FavoriteCollections
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Name)
                .ToListAsync();

            return collections.Select(MapToDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting collections for user {UserId}", userId);
            return new List<FavoriteCollectionDto>();
        }
    }

    public async Task<FavoriteCollectionDto> GetCollectionWithFavoritesAsync(int userId, int collectionId)
    {
        try
        {
            var collection = await _context.FavoriteCollections
                .Include(c => c.Favorites)
                    .ThenInclude(f => f.Subject)
                .FirstOrDefaultAsync(c => c.Id == collectionId && c.UserId == userId);

            if (collection == null)
            {
                throw new KeyNotFoundException("Collection not found");
            }

            return MapToDto(collection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting collection {CollectionId}", collectionId);
            throw;
        }
    }

    public async Task<bool> AddFavoriteToCollectionAsync(int userId, int favoriteId, int collectionId)
    {
        try
        {
            var favorite = await _context.Favorites
                .FirstOrDefaultAsync(f => f.Id == favoriteId && f.UserId == userId);

            if (favorite == null)
                return false;

            var collection = await _context.FavoriteCollections
                .FirstOrDefaultAsync(c => c.Id == collectionId && c.UserId == userId);

            if (collection == null)
                return false;

            favorite.CollectionId = collectionId;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Favorite {FavoriteId} added to collection {CollectionId}", favoriteId, collectionId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding favorite to collection");
            return false;
        }
    }

    public async Task<bool> RemoveFavoriteFromCollectionAsync(int userId, int favoriteId)
    {
        try
        {
            var favorite = await _context.Favorites
                .FirstOrDefaultAsync(f => f.Id == favoriteId && f.UserId == userId);

            if (favorite == null)
                return false;

            favorite.CollectionId = null;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Favorite {FavoriteId} removed from collection", favoriteId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing favorite from collection");
            return false;
        }
    }

    public async Task<bool> ReorderCollectionsAsync(int userId, List<int> collectionIds)
    {
        try
        {
            var collections = await _context.FavoriteCollections
                .Where(c => c.UserId == userId && collectionIds.Contains(c.Id))
                .ToListAsync();

            if (collections.Count != collectionIds.Count)
                return false;

            for (int i = 0; i < collectionIds.Count; i++)
            {
                var collection = collections.FirstOrDefault(c => c.Id == collectionIds[i]);
                if (collection != null)
                {
                    collection.Order = i;
                }
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Collections reordered for user {UserId}", userId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering collections");
            return false;
        }
    }

    private FavoriteCollectionDto MapToDto(FavoriteCollection collection)
    {
        return new FavoriteCollectionDto
        {
            Id = collection.Id,
            UserId = collection.UserId,
            Name = collection.Name,
            Description = collection.Description,
            Color = collection.Color,
            Icon = collection.Icon,
            Order = collection.Order,
            CreatedAt = collection.CreatedAt,
            UpdatedAt = collection.UpdatedAt,
            Favorites = collection.Favorites
                ?.Select(f => new FavoriteDto
                {
                    Id = f.Id,
                    UserId = f.UserId,
                    SubjectId = f.SubjectId,
                    SubjectTitle = f.Subject?.Title,
                    Tags = !string.IsNullOrEmpty(f.Tags) ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(f.Tags) : new List<string>(),
                    Notes = f.Notes,
                    AddedAt = f.AddedAt
                })
                .OrderByDescending(f => f.AddedAt)
                .ToList() ?? new List<FavoriteDto>()
        };
    }
}
