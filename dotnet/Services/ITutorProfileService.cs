using Backend.Models.DTOs;

namespace Backend.Services;

public interface ITutorProfileService
{
    Task<TutorProfileDto> GetOrCreateAsync(int userId);
    Task<TutorProfileDto> UpdateAsync(int userId, UpdateTutorProfileRequestDto request);
    Task<TutorProfileDto> ActivateAsync(int userId);
    Task<TutorProfileDto> SetVacationAsync(int userId, bool isOnVacation);
    Task<TutorVerificationDocumentDto> SubmitVerificationDocumentAsync(int userId, string documentUrl);
    Task<TutorProfileCompletionDto> GetCompletionAsync(int userId);
    Task<TutorProfileDto?> GetPublicProfileAsync(int userId);
    Task<List<TutorSearchResultDto>> SearchAsync(string? subject, string? level, decimal? maxHourlyRateXaf, bool verifiedOnly, int page, int pageSize);
}
