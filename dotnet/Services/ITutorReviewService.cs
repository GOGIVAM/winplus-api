using Backend.Models.DTOs;

namespace Backend.Services;

public interface ITutorReviewService
{
    Task<TutorReviewDto> SubmitAsync(int studentUserId, SubmitTutorReviewRequestDto request);
    Task<TutorReviewDto> ReplyAsync(int tutorUserId, int reviewId, string reply);
    Task<List<TutorReviewDto>> GetForTutorAsync(int tutorUserId, int page, int pageSize);
    Task<(double? AverageRating, int ReviewCount)> GetAggregateAsync(int tutorProfileId);
}
