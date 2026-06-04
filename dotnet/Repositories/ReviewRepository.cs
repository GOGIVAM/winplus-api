using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.Entities;

namespace Backend.Repositories;

public interface IReviewRepository
{
    Task<Review?> GetByIdAsync(int id);
    Task<Review?> GetByUserAndSubjectAsync(int userId, int subjectId);
    Task<List<Review>> GetBySubjectAsync(int subjectId, int page = 1, int pageSize = 20);
    Task<List<Review>> GetByUserAsync(int userId);
    Task<Review> CreateAsync(Review review);
    Task<Review> UpdateAsync(Review review);
    Task<bool> DeleteAsync(int id);
    Task<int> GetTotalReviewsAsync(int subjectId);
    Task<double> GetAverageRatingAsync(int subjectId);
    Task<Dictionary<int, int>> GetRatingDistributionAsync(int subjectId);
}

public class ReviewRepository : IReviewRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ReviewRepository> _logger;

    public ReviewRepository(ApplicationDbContext context, ILogger<ReviewRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Review?> GetByIdAsync(int id)
    {
        try
        {
            return await _context.Reviews
                .Include(r => r.User)
                .Include(r => r.Subject)
                .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting review by id {ReviewId}", id);
            return null;
        }
    }

    public async Task<Review?> GetByUserAndSubjectAsync(int userId, int subjectId)
    {
        try
        {
            return await _context.Reviews
                .FirstOrDefaultAsync(r => 
                    r.UserId == userId && 
                    r.SubjectId == subjectId && 
                    !r.IsDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting review for user {UserId} and subject {SubjectId}", userId, subjectId);
            return null;
        }
    }

    public async Task<List<Review>> GetBySubjectAsync(int subjectId, int page = 1, int pageSize = 20)
    {
        try
        {
            return await _context.Reviews
                .Include(r => r.User)
                .Where(r => r.SubjectId == subjectId && !r.IsDeleted)
                .OrderByDescending(r => r.HelpfulCount)
                .ThenByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting reviews for subject {SubjectId}", subjectId);
            return new List<Review>();
        }
    }

    public async Task<List<Review>> GetByUserAsync(int userId)
    {
        try
        {
            return await _context.Reviews
                .Include(r => r.Subject)
                .Where(r => r.UserId == userId && !r.IsDeleted)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting reviews for user {UserId}", userId);
            return new List<Review>();
        }
    }

    public async Task<Review> CreateAsync(Review review)
    {
        try
        {
            review.CreatedAt = DateTime.UtcNow;
            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Review {ReviewId} created by user {UserId} for subject {SubjectId}", 
                review.Id, review.UserId, review.SubjectId);
            
            return review;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating review");
            throw;
        }
    }

    public async Task<Review> UpdateAsync(Review review)
    {
        try
        {
            review.UpdatedAt = DateTime.UtcNow;
            _context.Reviews.Update(review);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Review {ReviewId} updated", review.Id);
            
            return review;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating review {ReviewId}", review.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            var review = await GetByIdAsync(id);
            if (review == null) return false;

            review.IsDeleted = true;
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Review {ReviewId} deleted", id);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting review {ReviewId}", id);
            throw;
        }
    }

    public async Task<int> GetTotalReviewsAsync(int subjectId)
    {
        try
        {
            return await _context.Reviews
                .Where(r => r.SubjectId == subjectId && !r.IsDeleted)
                .CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting reviews for subject {SubjectId}", subjectId);
            return 0;
        }
    }

    public async Task<double> GetAverageRatingAsync(int subjectId)
    {
        try
        {
            var reviews = await _context.Reviews
                .Where(r => r.SubjectId == subjectId && !r.IsDeleted)
                .ToListAsync();
            
            if (!reviews.Any()) return 0;
            
            return Math.Round(reviews.Average(r => r.Rating), 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating average rating for subject {SubjectId}", subjectId);
            return 0;
        }
    }

    public async Task<Dictionary<int, int>> GetRatingDistributionAsync(int subjectId)
    {
        try
        {
            var distribution = await _context.Reviews
                .Where(r => r.SubjectId == subjectId && !r.IsDeleted)
                .GroupBy(r => r.Rating)
                .Select(g => new { Rating = g.Key, Count = g.Count() })
                .ToListAsync();
            
            return Enumerable.Range(1, 5)
                .ToDictionary(
                    rating => rating,
                    rating => distribution.FirstOrDefault(d => d.Rating == rating)?.Count ?? 0
                );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting rating distribution for subject {SubjectId}", subjectId);
            return Enumerable.Range(1, 5).ToDictionary(r => r, r => 0);
        }
    }
}
