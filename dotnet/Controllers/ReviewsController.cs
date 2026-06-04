using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Backend.Services;
using Backend.Models.DTOs;
using Backend.Extensions;

namespace Backend.Controllers;

[ApiController]
[Route("api/reviews")]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviewService;
    private readonly ILogger<ReviewsController> _logger;

    public ReviewsController(IReviewService reviewService, ILogger<ReviewsController> logger)
    {
        _reviewService = reviewService;
        _logger = logger;
    }

    [HttpPost("subjects/{subjectId}")]
    [Authorize]
    public async Task<IActionResult> CreateReview(int subjectId, [FromBody] CreateReviewRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            var review = await _reviewService.CreateReviewAsync(userId, subjectId, request);

            return CreatedAtAction(
                nameof(GetReview),
                new { id = review.Id },
                new
                {
                    success = true,
                    data = review,
                    message = "Review created successfully",
                    timestamp = DateTime.UtcNow
                });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating review for subject {SubjectId}", subjectId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetReview(int id)
    {
        try
        {
            var review = await _reviewService.GetReviewAsync(id);
            
            if (review == null)
                return NotFound(new { error = "Review not found" });

            return Ok(new
            {
                success = true,
                data = review,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting review {ReviewId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("subjects/{subjectId}")]
    public async Task<IActionResult> GetSubjectReviews(
        int subjectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var reviews = await _reviewService.GetSubjectReviewsAsync(subjectId, page, pageSize);
            var summary = await _reviewService.GetSubjectRatingSummaryAsync(subjectId);

            return Ok(new
            {
                success = true,
                data = new
                {
                    reviews,
                    summary
                },
                pagination = new
                {
                    page,
                    pageSize,
                    totalReviews = summary.TotalReviews
                },
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting reviews for subject {SubjectId}", subjectId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPut("{id}")]
    [Authorize]
    public async Task<IActionResult> UpdateReview(int id, [FromBody] UpdateReviewRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            var review = await _reviewService.UpdateReviewAsync(id, userId, request);

            return Ok(new
            {
                success = true,
                data = review,
                message = "Review updated successfully",
                timestamp = DateTime.UtcNow
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating review {ReviewId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteReview(int id)
    {
        try
        {
            var userId = User.GetUserId();
            var result = await _reviewService.DeleteReviewAsync(id, userId);

            if (!result)
                return NotFound(new { error = "Review not found" });

            return Ok(new
            {
                success = true,
                message = "Review deleted successfully",
                timestamp = DateTime.UtcNow
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting review {ReviewId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("{id}/helpful")]
    [Authorize]
    public async Task<IActionResult> MarkAsHelpful(int id)
    {
        try
        {
            var result = await _reviewService.MarkAsHelpfulAsync(id);

            if (!result)
                return NotFound(new { error = "Review not found" });

            return Ok(new
            {
                success = true,
                message = "Review marked as helpful",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking review {ReviewId} as helpful", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("user/my-reviews")]
    [Authorize]
    public async Task<IActionResult> GetMyReviews()
    {
        try
        {
            var userId = User.GetUserId();
            var reviews = await _reviewService.GetUserReviewsAsync(userId);

            return Ok(new
            {
                success = true,
                data = reviews,
                count = reviews.Count,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user reviews");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
