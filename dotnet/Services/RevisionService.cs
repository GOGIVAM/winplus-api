using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Service pour gérer les Révisions/Guides d'Étude
/// </summary>
public class RevisionService : IRevisionService
{
    private readonly ApplicationDbContext _context;
    private const double WEAK_PERFORMANCE_THRESHOLD = 50.0; // Automatiquement assigner si score < 50%
    private const double IMPROVEMENT_TARGET_PENALTY = -10.0; // Réduire la cible si mauvaise performance

    public RevisionService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RevisionDto?> GetRevisionByIdAsync(int id)
    {
        var revision = await _context.Revisions
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);

        return revision != null ? MapToDto(revision) : null;
    }

    public async Task<IEnumerable<RevisionDto>> GetAllRevisionsAsync(int page = 1, int pageSize = 20)
    {
        var revisions = await _context.Revisions
            .AsNoTracking()
            .Where(r => !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return revisions.Select(MapToDto);
    }

    public async Task<IEnumerable<RevisionDto>> GetRevisionsAsync(RevisionSearchFilterDto filter)
    {
        var query = _context.Revisions
            .AsNoTracking()
            .Where(r => !r.IsDeleted);

        // Filtres
        if (!string.IsNullOrEmpty(filter.Subject))
            query = query.Where(r => r.Subject == filter.Subject);

        if (!string.IsNullOrEmpty(filter.Topic))
            query = query.Where(r => r.Topic.Contains(filter.Topic));

        if (!string.IsNullOrEmpty(filter.RevisionType))
            query = query.Where(r => r.Type == filter.RevisionType);

        if (filter.OnlyPublished.HasValue && filter.OnlyPublished.Value)
            query = query.Where(r => r.IsPublished);

        if (!string.IsNullOrEmpty(filter.SearchTerm))
            query = query.Where(r => r.Title.Contains(filter.SearchTerm) ||
                                     r.Description != null && r.Description.Contains(filter.SearchTerm) ||
                                     r.Topic.Contains(filter.SearchTerm));

        // Tri
        query = (filter.SortBy?.ToLower()) switch
        {
            "title" => filter.SortOrder == "asc" ? query.OrderBy(r => r.Title) : query.OrderByDescending(r => r.Title),
            "topic" => filter.SortOrder == "asc" ? query.OrderBy(r => r.Topic) : query.OrderByDescending(r => r.Topic),
            "popular" => query.OrderByDescending(r => r.UserEnrollments.Count),
            _ => query.OrderByDescending(r => r.CreatedAt),
        };

        var revisions = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return revisions.Select(MapToDto);
    }

    public async Task<IEnumerable<RevisionDto>> GetRevisionsBySubjectAsync(string subject, int page = 1, int pageSize = 20)
    {
        var revisions = await _context.Revisions
            .AsNoTracking()
            .Where(r => r.Subject == subject && !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return revisions.Select(MapToDto);
    }

    public async Task<IEnumerable<RevisionDto>> GetAssignedRevisionsAsync(int userId, int page = 1, int pageSize = 20)
    {
        var revisions = await _context.Revisions
            .AsNoTracking()
            .Where(r => r.UserEnrollments.Any(e => e.UserId == userId && e.Status != "Completed") && !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return revisions.Select(MapToDto);
    }

    public async Task<IEnumerable<RevisionDto>> SearchRevisionsAsync(string searchTerm, int page = 1, int pageSize = 20)
    {
        var revisions = await _context.Revisions
            .AsNoTracking()
            .Where(r => !r.IsDeleted &&
                       (r.Title.Contains(searchTerm) ||
                        r.Description != null && r.Description.Contains(searchTerm) ||
                        r.Topic.Contains(searchTerm) ||
                        r.Subject.Contains(searchTerm)))
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return revisions.Select(MapToDto);
    }

    public async Task<IEnumerable<RevisionDto>> GetPublishedRevisionsAsync(int page = 1, int pageSize = 20)
    {
        var revisions = await _context.Revisions
            .AsNoTracking()
            .Where(r => r.IsPublished && !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return revisions.Select(MapToDto);
    }

    public async Task<RevisionEnrollmentDto> StartRevisionAsync(int revisionId, int userId, StartRevisionRequestDto request)
    {
        var revision = await _context.Revisions
            .FirstOrDefaultAsync(r => r.Id == revisionId && !r.IsDeleted);

        if (revision == null)
            throw new KeyNotFoundException($"Revision with id {revisionId} not found");

        // Vérifier s'il y a déjà une inscription
        var existingEnrollment = await _context.RevisionEnrollments
            .FirstOrDefaultAsync(e => e.UserId == userId && e.RevisionId == revisionId);

        if (existingEnrollment != null)
        {
            existingEnrollment.Status = "InProgress";
            existingEnrollment.StartedAt = DateTime.UtcNow;
            _context.RevisionEnrollments.Update(existingEnrollment);
        }
        else
        {
            var enrollment = new RevisionEnrollment
            {
                UserId = userId,
                RevisionId = revisionId,
                Status = "InProgress",
                OriginalScore = request.OriginalScore.HasValue ? (decimal)request.OriginalScore.Value : 0,
                StartedAt = DateTime.UtcNow,
            };

            _context.RevisionEnrollments.Add(enrollment);
            _context.Revisions.Update(revision);
        }

        await _context.SaveChangesAsync();

        return await GetRevisionProgressAsync(revisionId, userId)
            .ContinueWith(t => t.Result != null 
                ? new RevisionEnrollmentDto
                {
                    Id = existingEnrollment?.Id ?? 0,
                    UserId = userId,
                    RevisionId = revisionId,
                    Status = "InProgress",
                    Progress = t.Result.ProgressPercentage,
                    StartedAt = DateTime.UtcNow,
                }
                : null);
    }

    public async Task<RevisionEnrollmentDto> CompleteRevisionAsync(int revisionId, int userId, CompleteRevisionRequestDto request)
    {
        var revision = await _context.Revisions
            .FirstOrDefaultAsync(r => r.Id == revisionId && !r.IsDeleted);

        if (revision == null)
            throw new KeyNotFoundException($"Revision with id {revisionId} not found");

        var enrollment = await _context.RevisionEnrollments
            .FirstOrDefaultAsync(e => e.UserId == userId && e.RevisionId == revisionId);

        if (enrollment == null)
            throw new KeyNotFoundException("User is not enrolled in this revision");

        enrollment.Status = "Completed";
        enrollment.FinalScore = (decimal)request.FinalScore;
        enrollment.CompletedAt = DateTime.UtcNow;
        enrollment.ScoreImprovement = (decimal)request.FinalScore - (enrollment.OriginalScore ?? 0);

        _context.RevisionEnrollments.Update(enrollment);

        await _context.SaveChangesAsync();

        return new RevisionEnrollmentDto
        {
            Id = enrollment.Id,
            UserId = enrollment.UserId,
            RevisionId = enrollment.RevisionId,
            Status = enrollment.Status,
            OriginalScore = enrollment.OriginalScore,
            FinalScore = enrollment.FinalScore,
            ScoreImprovement = enrollment.ScoreImprovement,
            Progress = 100,
            CompletedAt = enrollment.CompletedAt,
            StartedAt = enrollment.StartedAt,
        };
    }

    public async Task<RevisionProgressResponseDto?> GetRevisionProgressAsync(int revisionId, int userId)
    {
        var enrollment = await _context.RevisionEnrollments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == userId && e.RevisionId == revisionId);

        if (enrollment == null)
            return null;

        // Calculer la progression basée sur le temps écoulé si elle est en cours
        double progress = 0;
        if (enrollment.Status == "InProgress" && enrollment.StartedAt.HasValue)
        {
            // Estimer selon le temps écoulé
            var elapsedSeconds = (DateTime.UtcNow - enrollment.StartedAt.Value).TotalSeconds;
            // Assumer que 50% c'est 1 heure, 100% c'est 3 heures
            progress = Math.Min(100, (elapsedSeconds / 10800) * 100); // 10800 seconds = 3 hours
        }
        else if (enrollment.Status == "Completed")
        {
            progress = 100;
        }

        return new RevisionProgressResponseDto
        {
            RevisionId = revisionId,
            UserId = userId,
            Status = enrollment.Status,
            ProgressPercentage = progress,
            OriginalScore = enrollment.OriginalScore.HasValue ? (double?)enrollment.OriginalScore.Value : null,
            FinalScore = enrollment.FinalScore.HasValue ? (double?)enrollment.FinalScore.Value : null,
            ScoreImprovement = enrollment.ScoreImprovement.HasValue ? (double?)enrollment.ScoreImprovement.Value : null,
            EstimatedCompletionTime = enrollment.CompletedAt,
            StartedAt = enrollment.StartedAt,
        };
    }

    public async Task<RevisionDto> CreateRevisionAsync(CreateRevisionRequestDto request)
    {
        var revision = new Revision
        {
            Title = request.Title,
            Description = request.Description,
            Subject = request.Subject,
            Topic = request.Topic,
            Type = request.RevisionType,
            Content = request.Content,
            VideoUrl = request.VideoUrl,
            DocumentUrl = request.DocumentUrl,
            DurationMinutes = request.DurationMinutes,
            IsPublished = false,
            CreatedAt = DateTime.UtcNow,
        };

        _context.Revisions.Add(revision);
        await _context.SaveChangesAsync();

        return MapToDto(revision);
    }

    public async Task<RevisionDto> UpdateRevisionAsync(int id, UpdateRevisionRequestDto request)
    {
        var revision = await _context.Revisions.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (revision == null)
            throw new KeyNotFoundException($"Revision with id {id} not found");

        if (!string.IsNullOrEmpty(request.Title))
            revision.Title = request.Title;
        if (request.Description != null)
            revision.Description = request.Description;
        if (!string.IsNullOrEmpty(request.Subject))
            revision.Subject = request.Subject;
        if (!string.IsNullOrEmpty(request.Topic))
            revision.Topic = request.Topic;
        if (!string.IsNullOrEmpty(request.RevisionType))
            revision.Type = request.RevisionType;
        if (request.Content != null)
            revision.Content = request.Content;
        if (request.VideoUrl != null)
            revision.VideoUrl = request.VideoUrl;
        if (request.DocumentUrl != null)
            revision.DocumentUrl = request.DocumentUrl;
        if (request.DurationMinutes.HasValue)
            revision.DurationMinutes = request.DurationMinutes;

        revision.UpdatedAt = DateTime.UtcNow;

        _context.Revisions.Update(revision);
        await _context.SaveChangesAsync();

        return MapToDto(revision);
    }

    public async Task<RevisionDto> PublishRevisionAsync(int id)
    {
        var revision = await _context.Revisions.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (revision == null)
            throw new KeyNotFoundException($"Revision with id {id} not found");

        revision.IsPublished = true;
        revision.PublishedAt = DateTime.UtcNow;
        revision.UpdatedAt = DateTime.UtcNow;

        _context.Revisions.Update(revision);
        await _context.SaveChangesAsync();

        return MapToDto(revision);
    }

    public async Task<RevisionDto> UnpublishRevisionAsync(int id)
    {
        var revision = await _context.Revisions.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (revision == null)
            throw new KeyNotFoundException($"Revision with id {id} not found");

        revision.IsPublished = false;
        revision.UpdatedAt = DateTime.UtcNow;

        _context.Revisions.Update(revision);
        await _context.SaveChangesAsync();

        return MapToDto(revision);
    }

    public async Task<bool> DeleteRevisionAsync(int id)
    {
        var revision = await _context.Revisions.FirstOrDefaultAsync(r => r.Id == id);
        if (revision == null)
            return false;

        revision.IsDeleted = true;
        revision.UpdatedAt = DateTime.UtcNow;

        _context.Revisions.Update(revision);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<RevisionEnrollmentDto> AutoAssignRevisionAsync(int userId, int revisionId)
    {
        // Vérifier si l'utilisateur est déjà inscrit
        var existingEnrollment = await _context.RevisionEnrollments
            .FirstOrDefaultAsync(e => e.UserId == userId && e.RevisionId == revisionId);

        if (existingEnrollment != null)
        {
            existingEnrollment.Status = "InProgress";
            if (!existingEnrollment.StartedAt.HasValue)
                existingEnrollment.StartedAt = DateTime.UtcNow;

            _context.RevisionEnrollments.Update(existingEnrollment);
        }
        else
        {
            var revision = await _context.Revisions.FirstOrDefaultAsync(r => r.Id == revisionId && !r.IsDeleted);
            if (revision == null)
                throw new KeyNotFoundException($"Revision with id {revisionId} not found");

            var enrollment = new RevisionEnrollment
            {
                UserId = userId,
                RevisionId = revisionId,
                Status = "InProgress",
                StartedAt = DateTime.UtcNow,
            };

            _context.RevisionEnrollments.Add(enrollment);
            _context.Revisions.Update(revision);
        }

        await _context.SaveChangesAsync();

        var progress = await GetRevisionProgressAsync(revisionId, userId);

        return new RevisionEnrollmentDto
        {
            UserId = userId,
            RevisionId = revisionId,
            Status = "InProgress",
            Progress = progress?.ProgressPercentage ?? 0,
            StartedAt = DateTime.UtcNow,
        };
    }

    public async Task<object> GetRevisionStatsAsync(int id)
    {
        var revision = await _context.Revisions
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);

        if (revision == null)
            throw new KeyNotFoundException($"Revision with id {id} not found");

        var enrolledCount = await _context.RevisionEnrollments
            .Where(e => e.RevisionId == id)
            .CountAsync();

        var completionCount = await _context.RevisionEnrollments
            .Where(e => e.RevisionId == id && e.Status == "Completed")
            .CountAsync();

        var avgImprovement = await _context.RevisionEnrollments
            .Where(e => e.RevisionId == id && e.Status == "Completed" && e.ScoreImprovement.HasValue)
            .AverageAsync(e => (double?)e.ScoreImprovement) ?? 0;

        return new
        {
            EnrolledCount = enrolledCount,
            CompletionCount = completionCount,
            CompletionRate = enrolledCount > 0 ? (completionCount / (double)enrolledCount) * 100 : 0,
            AverageScoreImprovement = avgImprovement,
        };
    }

    public async Task<IEnumerable<RevisionEnrollmentDto>> AssignRevisionsBasedOnScoresAsync(int userId)
    {
        // Récupérer les scores les plus récents de l'utilisateur par sujet
        var recentScores = await _context.QuizAttempts
            .Where(a => a.UserId == userId)
            .GroupBy(a => a.Quiz.Subject)
            .Select(g => new
            {
                Subject = g.Key,
                LatestScore = g.OrderByDescending(a => a.CompletedAt).First().Score,
            })
            .ToListAsync();

        var assignedEnrollments = new List<RevisionEnrollmentDto>();

        foreach (var score in recentScores)
        {
            if (score.LatestScore < (decimal)WEAK_PERFORMANCE_THRESHOLD)
            {
                // Trouver les révisions appropriées pour ce sujet
                var appropriateRevisions = await _context.Revisions
                    .Where(r => r.Subject == score.Subject && r.IsPublished && !r.IsDeleted)
                    .ToListAsync();

                foreach (var revision in appropriateRevisions)
                {
                    var enrollment = await AutoAssignRevisionAsync(userId, revision.Id);
                    assignedEnrollments.Add(enrollment);
                }
            }
        }

        return assignedEnrollments;
    }

    private RevisionDto MapToDto(Revision revision)
    {
        return new RevisionDto
        {
            Id = revision.Id,
            Title = revision.Title,
            Description = revision.Description,
            Subject = revision.Subject,
            Topic = revision.Topic,
            RevisionType = revision.Type,
            Content = revision.Content,
            VideoUrl = revision.VideoUrl,
            DocumentUrl = revision.DocumentUrl,
            DurationMinutes = revision.DurationMinutes ?? 0,
            IsPublished = revision.IsPublished,
            EnrolledCount = revision.UserEnrollments?.Count ?? 0,
            CreatedAt = revision.CreatedAt,
            UpdatedAt = revision.UpdatedAt,
            PublishedAt = revision.PublishedAt,
        };
    }
}
