using Backend.Data;
using Backend.Models.Entities;
using Backend.Models.DTOs;
using Backend.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public interface IReviewService
{
    Task<ReviewDto> CreateReviewAsync(int userId, int subjectId, CreateReviewRequest request);
    Task<ReviewDto> UpdateReviewAsync(int reviewId, int userId, UpdateReviewRequest request);
    Task<bool> DeleteReviewAsync(int reviewId, int userId);
    Task<List<ReviewDto>> GetSubjectReviewsAsync(int subjectId, int page, int pageSize);
    Task<SubjectRatingSummary> GetSubjectRatingSummaryAsync(int subjectId);
    Task<ReviewDto?> GetUserReviewForSubjectAsync(int userId, int subjectId);
    Task<bool> MarkAsHelpfulAsync(int reviewId);
    Task<List<ReviewDto>> GetUserReviewsAsync(int userId);
    Task<ReviewDto?> GetReviewAsync(int id);
    Task<List<ReviewDto>> GetTopReviewsAsync(int limit = 6);
}

public class ReviewService : IReviewService
{
    private readonly IReviewRepository _reviewRepository;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ReviewService> _logger;

    public ReviewService(
        IReviewRepository reviewRepository,
        ApplicationDbContext context,
        ILogger<ReviewService> logger)
    {
        _reviewRepository = reviewRepository;
        _context = context;
        _logger = logger;
    }

    public async Task<ReviewDto> CreateReviewAsync(int userId, int subjectId, CreateReviewRequest request)
    {
        try
        {
            // Check if user hasn't already reviewed
            var existing = await _reviewRepository.GetByUserAndSubjectAsync(userId, subjectId);
            if (existing != null)
            {
                throw new InvalidOperationException("You have already reviewed this course");
            }

            // Check if verified purchase
            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId && e.SubjectId == subjectId);
            
            var isVerifiedPurchase = enrollment != null;

            var review = new Review
            {
                UserId = userId,
                SubjectId = subjectId,
                Rating = request.Rating,
                Title = request.Title,
                Comment = request.Comment,
                IsVerifiedPurchase = isVerifiedPurchase
            };

            await _reviewRepository.CreateAsync(review);

            return MapToDto(review);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating review");
            throw;
        }
    }

    public async Task<ReviewDto> UpdateReviewAsync(int reviewId, int userId, UpdateReviewRequest request)
    {
        try
        {
            var review = await _reviewRepository.GetByIdAsync(reviewId);
            
            if (review == null)
                throw new KeyNotFoundException("Review not found");
            
            if (review.UserId != userId)
                throw new UnauthorizedAccessException("You can only edit your own reviews");

            if (request.Rating.HasValue)
                review.Rating = request.Rating.Value;
            
            if (request.Title != null)
                review.Title = request.Title;
            
            if (request.Comment != null)
                review.Comment = request.Comment;

            await _reviewRepository.UpdateAsync(review);

            return MapToDto(review);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating review");
            throw;
        }
    }

    public async Task<bool> DeleteReviewAsync(int reviewId, int userId)
    {
        try
        {
            var review = await _reviewRepository.GetByIdAsync(reviewId);
            
            if (review == null)
                return false;
            
            if (review.UserId != userId)
                throw new UnauthorizedAccessException("You can only delete your own reviews");

            return await _reviewRepository.DeleteAsync(reviewId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting review");
            throw;
        }
    }

    public async Task<List<ReviewDto>> GetSubjectReviewsAsync(int subjectId, int page, int pageSize)
    {
        try
        {
            var reviews = await _reviewRepository.GetBySubjectAsync(subjectId, page, pageSize);
            return reviews.Select(MapToDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subject reviews");
            return new List<ReviewDto>();
        }
    }

    public async Task<SubjectRatingSummary> GetSubjectRatingSummaryAsync(int subjectId)
    {
        try
        {
            var totalReviews = await _reviewRepository.GetTotalReviewsAsync(subjectId);
            var averageRating = await _reviewRepository.GetAverageRatingAsync(subjectId);
            var distribution = await _reviewRepository.GetRatingDistributionAsync(subjectId);

            return new SubjectRatingSummary
            {
                SubjectId = subjectId,
                AverageRating = averageRating,
                TotalReviews = totalReviews,
                RatingDistribution = distribution
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting rating summary");
            throw;
        }
    }

    public async Task<ReviewDto?> GetUserReviewForSubjectAsync(int userId, int subjectId)
    {
        try
        {
            var review = await _reviewRepository.GetByUserAndSubjectAsync(userId, subjectId);
            return review != null ? MapToDto(review) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user review");
            return null;
        }
    }

    public async Task<bool> MarkAsHelpfulAsync(int reviewId)
    {
        try
        {
            var review = await _reviewRepository.GetByIdAsync(reviewId);
            if (review == null) return false;

            review.HelpfulCount++;
            await _reviewRepository.UpdateAsync(review);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking review as helpful");
            return false;
        }
    }

    public async Task<List<ReviewDto>> GetUserReviewsAsync(int userId)
    {
        try
        {
            var reviews = await _reviewRepository.GetByUserAsync(userId);
            return reviews.Select(MapToDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user reviews");
            return new List<ReviewDto>();
        }
    }

    public async Task<ReviewDto?> GetReviewAsync(int id)
    {
        try
        {
            var review = await _reviewRepository.GetByIdAsync(id);
            return review != null ? MapToDto(review) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting review");
            return null;
        }
    }

    /// <summary>
    /// Récupère les meilleurs avis (top-rated) pour les témoignages
    /// </summary>
    public async Task<List<ReviewDto>> GetTopReviewsAsync(int limit = 6)
    {
        try
        {
            var topReviews = await _context.Reviews
                .Where(r => r.Rating >= 4) // Au minimum 4 étoiles
                .OrderByDescending(r => r.Rating)
                .ThenByDescending(r => r.HelpfulCount)
                .ThenByDescending(r => r.CreatedAt)
                .Take(limit)
                .Include(r => r.User)
                .Include(r => r.Subject)
                .ToListAsync();

            return topReviews.Select(MapToDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting top reviews");
            return new List<ReviewDto>();
        }
    }

    private ReviewDto MapToDto(Review review)
    {
        return new ReviewDto
        {
            Id = review.Id,
            UserId = review.UserId,
            UserName = review.User != null ? $"{review.User.FirstName} {review.User.LastName}" : "Unknown",
            UserAvatar = review.User?.AvatarUrl,
            SubjectId = review.SubjectId,
            Rating = review.Rating,
            Title = review.Title,
            Comment = review.Comment,
            IsVerifiedPurchase = review.IsVerifiedPurchase,
            HelpfulCount = review.HelpfulCount,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt
        };
    }
}
