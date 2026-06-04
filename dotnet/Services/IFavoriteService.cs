using Backend.Models.Entities;
using Backend.Models.DTOs;

namespace Backend.Services;

public interface IFavoriteService
{
    Task<IEnumerable<Favorite>> GetFavoritesAsync(int userId);
    Task<IEnumerable<Favorite>> GetFavoritesAsync(int userId, int page, int limit);
    Task<Favorite> AddFavoriteAsync(int userId, int subjectId);
    Task<bool> RemoveFavoriteAsync(int userId, int subjectId);
    Task<bool> IsFavoriteAsync(int userId, int subjectId);
    Task<Favorite> UpdateFavoriteAsync(int userId, int favoriteId, UpdateFavoriteRequest request);
    Task<List<Favorite>> SearchFavoritesByTagAsync(int userId, string tag);
    Task<List<dynamic>> GetAllTagsAsync(int userId);
}
