using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Services;
using Backend.Models.DTOs;
using Backend.Extensions;

namespace Backend.Controllers;

[ApiController]
[Route("api/favorite-collections")]
[Authorize]
public class FavoriteCollectionsController : ControllerBase
{
    private readonly IFavoriteCollectionService _collectionService;
    private readonly ILogger<FavoriteCollectionsController> _logger;

    public FavoriteCollectionsController(IFavoriteCollectionService collectionService, ILogger<FavoriteCollectionsController> logger)
    {
        _collectionService = collectionService;
        _logger = logger;
    }

    /// <summary>
    /// Create a new favorite collection
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateCollection([FromBody] CreateFavoriteCollectionRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            var collection = await _collectionService.CreateCollectionAsync(userId, request);

            return CreatedAtAction(
                nameof(GetCollection),
                new { collectionId = collection.Id },
                new
                {
                    success = true,
                    data = collection,
                    message = "Collection created successfully",
                    timestamp = DateTime.UtcNow
                });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating collection");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get all user's collections
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetCollections()
    {
        try
        {
            var userId = User.GetUserId();
            var collections = await _collectionService.GetUserCollectionsAsync(userId);

            return Ok(new
            {
                success = true,
                data = collections,
                count = collections.Count,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting collections");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get collection with all favorites
    /// </summary>
    [HttpGet("{collectionId}")]
    public async Task<IActionResult> GetCollection(int collectionId)
    {
        try
        {
            var userId = User.GetUserId();
            var collection = await _collectionService.GetCollectionWithFavoritesAsync(userId, collectionId);

            return Ok(new
            {
                success = true,
                data = collection,
                timestamp = DateTime.UtcNow
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting collection {CollectionId}", collectionId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Update a collection
    /// </summary>
    [HttpPut("{collectionId}")]
    public async Task<IActionResult> UpdateCollection(int collectionId, [FromBody] UpdateFavoriteCollectionRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            var collection = await _collectionService.UpdateCollectionAsync(userId, collectionId, request);

            return Ok(new
            {
                success = true,
                data = collection,
                message = "Collection updated successfully",
                timestamp = DateTime.UtcNow
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating collection {CollectionId}", collectionId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Delete a collection
    /// </summary>
    [HttpDelete("{collectionId}")]
    public async Task<IActionResult> DeleteCollection(int collectionId)
    {
        try
        {
            var userId = User.GetUserId();
            var result = await _collectionService.DeleteCollectionAsync(userId, collectionId);

            if (!result)
                return NotFound(new { error = "Collection not found" });

            return Ok(new
            {
                success = true,
                message = "Collection deleted successfully",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting collection {CollectionId}", collectionId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Add favorite to collection
    /// </summary>
    [HttpPost("{collectionId}/favorites/{favoriteId}")]
    public async Task<IActionResult> AddFavoriteToCollection(int collectionId, int favoriteId)
    {
        try
        {
            var userId = User.GetUserId();
            var result = await _collectionService.AddFavoriteToCollectionAsync(userId, favoriteId, collectionId);

            if (!result)
                return BadRequest(new { error = "Failed to add favorite to collection" });

            return Ok(new
            {
                success = true,
                message = "Favorite added to collection successfully",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding favorite to collection");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Remove favorite from collection
    /// </summary>
    [HttpDelete("favorites/{favoriteId}")]
    public async Task<IActionResult> RemoveFavoriteFromCollection(int favoriteId)
    {
        try
        {
            var userId = User.GetUserId();
            var result = await _collectionService.RemoveFavoriteFromCollectionAsync(userId, favoriteId);

            if (!result)
                return BadRequest(new { error = "Failed to remove favorite from collection" });

            return Ok(new
            {
                success = true,
                message = "Favorite removed from collection successfully",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing favorite from collection");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Reorder collections
    /// </summary>
    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderCollections([FromBody] List<int> collectionIds)
    {
        try
        {
            var userId = User.GetUserId();
            var result = await _collectionService.ReorderCollectionsAsync(userId, collectionIds);

            if (!result)
                return BadRequest(new { error = "Failed to reorder collections" });

            return Ok(new
            {
                success = true,
                message = "Collections reordered successfully",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering collections");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
