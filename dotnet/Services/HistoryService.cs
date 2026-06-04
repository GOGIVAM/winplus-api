using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Repositories;

namespace Backend.Services;

/// <summary>
/// Interface pour le service d'historique d'apprentissage
/// </summary>
public interface IHistoryService
{
    Task<HistoryResponse> AddToHistoryAsync(int userId, AddHistoryRequest request);
    Task<HistoryResponse> GetHistoryByIdAsync(int id);
    Task<HistoryListResponse> GetUserHistoryAsync(int userId, int page = 1, int limit = 50);
    Task<HistoryListResponse> GetUserHistoryByTypeAsync(int userId, string eventType, int page = 1, int limit = 50);
    Task<HistoryListResponse> GetUserHistoryBySubjectAsync(int userId, int subjectId, int page = 1, int limit = 50);
    Task<HistoryListResponse> GetUserHistoryByDateRangeAsync(int userId, DateTime startDate, DateTime endDate, int page = 1, int limit = 50);
    Task<HistoryStatistics> GetUserStatisticsAsync(int userId);
    Task<bool> DeleteHistoryAsync(int id);
    Task<bool> ClearUserHistoryAsync(int userId);
    Task<List<HistoryResponse>> GetRecentActivityAsync(int userId, int count = 10);
}

/// <summary>
/// Service pour l'historique d'apprentissage
/// </summary>
public class HistoryService : IHistoryService
{
    private readonly IHistoryRepository _repository;
    private readonly ILogger<HistoryService> _logger;

    public HistoryService(IHistoryRepository repository, ILogger<HistoryService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<HistoryResponse> AddToHistoryAsync(int userId, AddHistoryRequest request)
    {
        try
        {
            var history = new LearningHistory
            {
                UserId = userId,
                SubjectId = request.SubjectId,
                EventType = request.EventType,
                EventTitle = request.EventTitle,
                EventDescription = request.EventDescription,
                Score = request.Score,
                DurationSeconds = request.DurationSeconds,
                ProgressPercentage = request.ProgressPercentage,
                Notes = request.Notes,
                EventDetails = request.EventDetails,
                IsCompleted = request.IsCompleted,
                CreatedAt = DateTime.UtcNow
            };

            var created = await _repository.CreateAsync(history);
            _logger.LogInformation("Événement d'historique ajouté: {HistoryId} pour l'utilisateur {UserId}", created.Id, userId);

            return MapToResponse(created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'ajout à l'historique");
            throw;
        }
    }

    public async Task<HistoryResponse> GetHistoryByIdAsync(int id)
    {
        var history = await _repository.GetByIdAsync(id);
        if (history == null)
            throw new ArgumentException("Événement d'historique non trouvé");

        return MapToResponse(history);
    }

    public async Task<HistoryListResponse> GetUserHistoryAsync(int userId, int page = 1, int limit = 50)
    {
        try
        {
            var history = await _repository.GetByUserIdAsync(userId, page, limit);
            var total = await _repository.GetTotalCountByUserAsync(userId);

            return new HistoryListResponse
            {
                History = history.Select(MapToResponse).ToList(),
                Total = total,
                Page = page,
                Limit = limit,
                Statistics = await GetUserStatisticsAsync(userId)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération de l'historique de l'utilisateur {UserId}", userId);
            throw;
        }
    }

    public async Task<HistoryListResponse> GetUserHistoryByTypeAsync(int userId, string eventType, int page = 1, int limit = 50)
    {
        try
        {
            var history = await _repository.GetByUserAndTypeAsync(userId, eventType, page, limit);
            var total = await _repository.GetTotalCountByUserAsync(userId);

            return new HistoryListResponse
            {
                History = history.Select(MapToResponse).ToList(),
                Total = total,
                Page = page,
                Limit = limit
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération de l'historique filtré par type");
            throw;
        }
    }

    public async Task<HistoryListResponse> GetUserHistoryBySubjectAsync(int userId, int subjectId, int page = 1, int limit = 50)
    {
        try
        {
            var history = await _repository.GetByUserAndSubjectAsync(userId, subjectId, page, limit);
            var total = await _repository.GetTotalCountByUserAsync(userId);

            return new HistoryListResponse
            {
                History = history.Select(MapToResponse).ToList(),
                Total = total,
                Page = page,
                Limit = limit
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération de l'historique par cours");
            throw;
        }
    }

    public async Task<HistoryListResponse> GetUserHistoryByDateRangeAsync(int userId, DateTime startDate, DateTime endDate, int page = 1, int limit = 50)
    {
        try
        {
            var history = await _repository.GetByUserAndDateRangeAsync(userId, startDate, endDate, page, limit);
            var total = await _repository.GetTotalCountByUserAsync(userId);

            return new HistoryListResponse
            {
                History = history.Select(MapToResponse).ToList(),
                Total = total,
                Page = page,
                Limit = limit
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération de l'historique par plage de dates");
            throw;
        }
    }

    public async Task<HistoryStatistics> GetUserStatisticsAsync(int userId)
    {
        try
        {
            var recentActivity = await _repository.GetRecentActivityAsync(userId, 1);
            var breakdown = await _repository.GetEventTypeBreakdownAsync(userId);

            return new HistoryStatistics
            {
                TotalEvents = await _repository.GetTotalCountByUserAsync(userId),
                CoursesStarted = breakdown.ContainsKey("course_started") ? breakdown["course_started"] : 0,
                CoursesCompleted = breakdown.ContainsKey("course_completed") ? breakdown["course_completed"] : 0,
                TotalTimeSpentMinutes = await _repository.GetTotalTimeSpentAsync(userId) / 60,
                AverageScore = await _repository.GetAverageScoreAsync(userId),
                FirstActivityAt = recentActivity.Any() ? recentActivity.Last().CreatedAt : DateTime.UtcNow,
                LastActivityAt = recentActivity.Any() ? recentActivity.First().CreatedAt : DateTime.UtcNow,
                EventTypeBreakdown = breakdown
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du calcul des statistiques de l'utilisateur {UserId}", userId);
            return new HistoryStatistics(); // Retourner une instance vide plutôt que de lever une exception
        }
    }

    public async Task<bool> DeleteHistoryAsync(int id)
    {
        try
        {
            var result = await _repository.DeleteAsync(id);
            if (result)
                _logger.LogInformation("Événement d'historique supprimé: {HistoryId}", id);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la suppression de l'événement d'historique");
            throw;
        }
    }

    public async Task<bool> ClearUserHistoryAsync(int userId)
    {
        try
        {
            var result = await _repository.DeleteByUserIdAsync(userId);
            if (result)
                _logger.LogInformation("Historique de l'utilisateur {UserId} supprimé", userId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'effacement de l'historique");
            throw;
        }
    }

    public async Task<List<HistoryResponse>> GetRecentActivityAsync(int userId, int count = 10)
    {
        try
        {
            var activity = await _repository.GetRecentActivityAsync(userId, count);
            return activity.Select(MapToResponse).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération de l'activité récente");
            throw;
        }
    }

    private HistoryResponse MapToResponse(LearningHistory history)
    {
        return new HistoryResponse
        {
            Id = history.Id,
            UserId = history.UserId,
            SubjectId = history.SubjectId,
            EventType = history.EventType,
            EventTitle = history.EventTitle,
            EventDescription = history.EventDescription,
            Score = history.Score,
            DurationSeconds = history.DurationSeconds,
            ProgressPercentage = history.ProgressPercentage,
            Notes = history.Notes,
            EventDetails = history.EventDetails,
            IsCompleted = history.IsCompleted,
            CreatedAt = history.CreatedAt,
            UpdatedAt = history.UpdatedAt,
            SubjectTitle = history.Subject?.Title
        };
    }
}
