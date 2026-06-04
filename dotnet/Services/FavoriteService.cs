using Backend.Models.Entities;
using Backend.Models.DTOs;
using Backend.Repositories;
using System.Text.Json;

namespace Backend.Services;

public class FavoriteService : IFavoriteService
{
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly ISubjectRepository _subjectRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<FavoriteService> _logger;

    public FavoriteService(IFavoriteRepository favoriteRepository, ISubjectRepository subjectRepository, IUserRepository userRepository, ILogger<FavoriteService> logger)
    {
        _favoriteRepository = favoriteRepository;
        _subjectRepository = subjectRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<IEnumerable<Favorite>> GetFavoritesAsync(int userId)
    {
        try
        {
            return await _favoriteRepository.GetByUserIdAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des favoris utilisateur");
            return Enumerable.Empty<Favorite>();
        }
    }

    public async Task<IEnumerable<Favorite>> GetFavoritesAsync(int userId, int page, int limit)
    {
        try
        {
            if (page < 1) page = 1;
            if (limit < 1 || limit > 100) limit = 20;

            var skip = (page - 1) * limit;
            var favorites = await _favoriteRepository.GetByUserIdAsync(userId);
            return favorites.Skip(skip).Take(limit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération paginée des favoris (page {Page}, limit {Limit})", page, limit);
            return Enumerable.Empty<Favorite>();
        }
    }

    public async Task<Favorite> AddFavoriteAsync(int userId, int subjectId)
    {
        try
        {
            var subject = await _subjectRepository.GetByIdAsync(subjectId);
            if (subject == null)
                throw new InvalidOperationException($"Cours {subjectId} introuvable");

            // Get user (for navigation property)
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                throw new InvalidOperationException($"User {userId} not found");

            var favorite = new Favorite
            {
                UserId = userId,
                SubjectId = subjectId,
                User = user,
                Subject = subject
            };
            return await _favoriteRepository.AddAsync(favorite);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'ajout du favori");
            throw;
        }
    }

    public async Task<bool> RemoveFavoriteAsync(int userId, int subjectId)
    {
        try
        {
            return await _favoriteRepository.RemoveByUserAndSubjectAsync(userId, subjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la suppression du favori");
            return false;
        }
    }

    public async Task<bool> IsFavoriteAsync(int userId, int subjectId)
    {
        try
        {
            var fav = await _favoriteRepository.GetByUserAndSubjectAsync(userId, subjectId);
            return fav != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la vérification du favori");
            return false;
        }
    }

    public async Task<Favorite> UpdateFavoriteAsync(int userId, int favoriteId, UpdateFavoriteRequest request)
    {
        try
        {
            var favorite = await _favoriteRepository.GetByIdAsync(favoriteId);
            
            if (favorite == null || favorite.UserId != userId)
            {
                throw new KeyNotFoundException("Favorite not found");
            }

            // Update tags
            if (request.Tags != null)
            {
                var cleanTags = request.Tags
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim().ToLower())
                    .Distinct()
                    .Take(10) // Max 10 tags
                    .ToList();

                favorite.Tags = cleanTags.Any() ? JsonSerializer.Serialize(cleanTags) : null;
            }

            // Update notes
            if (request.Notes != null)
            {
                favorite.Notes = request.Notes.Length > 5000 
                    ? request.Notes.Substring(0, 5000) 
                    : request.Notes;
            }

            await _favoriteRepository.UpdateAsync(favorite);

            _logger.LogInformation("Favorite {FavoriteId} updated for user {UserId}", favoriteId, userId);

            return favorite;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating favorite {FavoriteId}", favoriteId);
            throw;
        }
    }

    public async Task<List<Favorite>> SearchFavoritesByTagAsync(int userId, string tag)
    {
        try
        {
            var favorites = await _favoriteRepository.GetByUserIdAsync(userId);

            // Filter by tag (JSON search)
            return favorites
                .Where(f =>
                {
                    if (string.IsNullOrEmpty(f.Tags))
                        return false;
                    
                    var tags = JsonSerializer.Deserialize<List<string>>(f.Tags);
                    return tags != null && tags.Contains(tag.ToLower());
                })
                .OrderByDescending(f => f.AddedAt)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching favorites by tag {Tag}", tag);
            return new List<Favorite>();
        }
    }

    public async Task<List<dynamic>> GetAllTagsAsync(int userId)
    {
        try
        {
            var favorites = await _favoriteRepository.GetByUserIdAsync(userId);

            // Extract all unique tags
            var allTags = favorites
                .Where(f => !string.IsNullOrEmpty(f.Tags))
                .SelectMany(f => JsonSerializer.Deserialize<List<string>>(f.Tags) ?? new List<string>())
                .GroupBy(t => t)
                .Select(g => new { tag = g.Key, count = g.Count() })
                .OrderByDescending(t => t.count)
                .Cast<dynamic>()
                .ToList();

            return allTags;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all tags for user {UserId}", userId);
            return new List<dynamic>();
        }
    }
}
