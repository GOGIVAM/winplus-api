using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Services;
using Backend.Models.Entities;
using Backend.Extensions;
using Backend.Models.DTOs;

namespace Backend.Controllers;

[ApiController]
[Route("api/favorites")]
public class FavoritesController : ControllerBase
{
    private readonly IFavoriteService _favoriteService;
    private readonly ILogger<FavoritesController> _logger;

    public FavoritesController(IFavoriteService favoriteService, ILogger<FavoritesController> logger)
    {
        _favoriteService = favoriteService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PaginationResponse<Favorite>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFavorites([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var userId = User.GetUserId();
            var favorites = await _favoriteService.GetFavoritesAsync(userId, page, pageSize);
            var allFavorites = await _favoriteService.GetFavoritesAsync(userId);
            var totalCount = allFavorites.Count();
            
            var response = new PaginationResponse<Favorite>(favorites, totalCount, page, pageSize);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des favoris");
            return StatusCode(500, "Erreur serveur");
        }
    }

    [HttpPost("{id}")]
    public async Task<IActionResult> AddFavorite(int id)
    {
        try
        {
            var userId = User.GetUserId();
            var result = await _favoriteService.AddFavoriteAsync(userId, id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'ajout du favori");
            return StatusCode(500, "Erreur serveur");
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> RemoveFavorite(int id)
    {
        try
        {
            var userId = User.GetUserId();
            var result = await _favoriteService.RemoveFavoriteAsync(userId, id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la suppression du favori");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Update a favorite with tags and notes
    /// </summary>
    [HttpPut("{favoriteId}")]
    [Authorize]
    public async Task<IActionResult> UpdateFavorite(int favoriteId, [FromBody] UpdateFavoriteRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            var favorite = await _favoriteService.UpdateFavoriteAsync(userId, favoriteId, request);

            return Ok(new
            {
                success = true,
                data = favorite,
                message = "Favorite updated successfully",
                timestamp = DateTime.UtcNow
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating favorite {FavoriteId}", favoriteId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Search favorites by tag
    /// </summary>
    [HttpGet("tags/{tag}")]
    [Authorize]
    public async Task<IActionResult> GetFavoritesByTag(string tag)
    {
        try
        {
            var userId = User.GetUserId();
            var favorites = await _favoriteService.SearchFavoritesByTagAsync(userId, tag);

            return Ok(new
            {
                success = true,
                data = favorites,
                tag = tag,
                count = favorites.Count,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching favorites by tag {Tag}", tag);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get all tags for user's favorites
    /// </summary>
    [HttpGet("tags")]
    [Authorize]
    public async Task<IActionResult> GetAllTags()
    {
        try
        {
            var userId = User.GetUserId();
            var allTags = await _favoriteService.GetAllTagsAsync(userId);

            return Ok(new
            {
                success = true,
                data = allTags,
                totalTags = allTags.Count,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all tags");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
