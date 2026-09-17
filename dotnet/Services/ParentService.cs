using Backend.Data;
using Backend.Models.Entities;
using Backend.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public interface IParentService
{
    Task<dynamic> GetChildStatsAsync(int parentId, int childId);
    Task<IEnumerable<dynamic>> GetChildActivitiesAsync(int parentId, int childId, int limit = 10);
    Task<IEnumerable<dynamic>> GetUpcomingPaymentsAsync(int parentId);
    Task<IEnumerable<Event>> GetUpcomingEventsAsync(int parentId, int limit = 10);
    Task<IEnumerable<dynamic>> GetChildQuizzesAsync(int parentId, int childId, int limit = 10);
    Task<IEnumerable<dynamic>> GetChildRevisionsAsync(int parentId, int childId, int limit = 10);
    Task<dynamic> GetParentProfileAsync(int parentId);
    Task<IEnumerable<dynamic>> GetChildGoalsAsync(int parentId, int childId);
}

public class ParentService : IParentService
{
    private readonly ApplicationDbContext _context;
    private readonly IEventRepository _eventRepository;
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly ILogger<ParentService> _logger;

    public ParentService(
        ApplicationDbContext context,
        IEventRepository eventRepository,
        ISubscriptionRepository subscriptionRepository,
        ILogger<ParentService> logger)
    {
        _context = context;
        _eventRepository = eventRepository;
        _subscriptionRepository = subscriptionRepository;
        _logger = logger;
    }

    public async Task<dynamic> GetChildStatsAsync(int parentId, int childId)
    {
        try
        {
            var enrollments = await _context.Enrollments
                .AsNoTracking()
                .Where(e => e.UserId == childId)
                .ToListAsync();

            var histories = await _context.LearningHistories
                .AsNoTracking()
                .Where(h => h.UserId == childId)
                .ToListAsync();
            
            var enrollmentCount = enrollments.Count;
            var completionRate = enrollmentCount > 0 ? 
                (enrollments.Count(e => e.IsCompleted) / (decimal)enrollmentCount * 100) : 0;
            var averageScore = histories.Any() ? 
                histories.Average(h => h.Score ?? 0) : 0;
            
            return new
            {
                enrollmentCount,
                completionRate = Math.Round(completionRate, 2),
                averageScore = Math.Round(averageScore, 2)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting child stats for parent {ParentId} child {ChildId}", parentId, childId);
            return new { enrollmentCount = 0, completionRate = 0, averageScore = 0 };
        }
    }

    public async Task<IEnumerable<dynamic>> GetChildActivitiesAsync(int parentId, int childId, int limit = 10)
    {
        try
        {
            var activities = await _context.LearningHistories
                .AsNoTracking()
                .Where(h => h.UserId == childId)
                .OrderByDescending(h => h.CreatedAt)
                .Take(limit)
                .Select(h => new
                {
                    h.Id,
                    Action = h.ActivityType ?? h.EventType,
                    h.SubjectId,
                    h.CreatedAt,
                    status = h.IsCompleted ? "completed" : "ongoing"
                })
                .ToListAsync();
            
            return activities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting child activities for parent {ParentId} child {ChildId}", parentId, childId);
            return Enumerable.Empty<dynamic>();
        }
    }

    public async Task<IEnumerable<dynamic>> GetUpcomingPaymentsAsync(int parentId)
    {
        try
        {
            var now = DateTime.UtcNow;
            var payments = await _context.Orders
                .AsNoTracking()
                .Where(o => o.UserId == parentId && o.OrderDate > now.AddDays(-30))
                .OrderByDescending(o => o.OrderDate)
                .Select(o => new
                {
                    o.Id,
                    o.TotalAmount,
                    o.OrderDate,
                    o.Status
                })
                .ToListAsync();
            
            return payments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting upcoming payments for parent {ParentId}", parentId);
            return Enumerable.Empty<dynamic>();
        }
    }

    public async Task<IEnumerable<Event>> GetUpcomingEventsAsync(int parentId, int limit = 10)
    {
        try
        {
            var events = await _eventRepository.GetUpcomingAsync(limit);
            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting upcoming events for parent {ParentId}", parentId);
            return Enumerable.Empty<Event>();
        }
    }

    public async Task<IEnumerable<dynamic>> GetChildQuizzesAsync(int parentId, int childId, int limit = 10)
    {
        try
        {
            // Quiz publiés que l'enfant n'a pas encore tentés  les tables
            // Quizzes/QuizAttempts existent bel et bien aujourd'hui (le
            // commentaire "Placeholder: needs Quizzes table" datait d'avant
            // leur création et n'avait jamais été mis à jour).
            var attemptedQuizIds = await _context.QuizAttempts
                .Where(a => a.UserId == childId)
                .Select(a => a.QuizId)
                .Distinct()
                .ToListAsync();

            var quizzes = await _context.Quizzes
                .AsNoTracking()
                .Where(q => q.IsPublished && !q.IsDeleted && !attemptedQuizIds.Contains(q.Id))
                .OrderByDescending(q => q.CreatedAt)
                .Take(limit)
                .Select(q => new
                {
                    q.Id,
                    q.Title,
                    q.Subject,
                    q.Difficulty,
                    q.QuestionCount,
                    q.TimeLimit,
                })
                .ToListAsync();

            return quizzes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting child quizzes for parent {ParentId} child {ChildId}", parentId, childId);
            return Enumerable.Empty<dynamic>();
        }
    }

    public async Task<IEnumerable<dynamic>> GetChildRevisionsAsync(int parentId, int childId, int limit = 10)
    {
        try
        {
            // Fiches de révision (Revision) appartenant à l'enfant  la table
            // existe bel et bien (voir RevisionsController/RevisionService),
            // le commentaire "Placeholder: needs Revisions table" était périmé.
            var revisions = await _context.Revisions
                .AsNoTracking()
                .Where(r => r.CreatedByUserId == childId && r.IsPublished && !r.IsDeleted && !r.HiddenFromList)
                .OrderByDescending(r => r.CreatedAt)
                .Take(limit)
                .Select(r => new
                {
                    r.Id,
                    r.Title,
                    r.Subject,
                    r.Type,
                    r.CreatedAt,
                })
                .ToListAsync();

            return revisions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting child revisions for parent {ParentId} child {ChildId}", parentId, childId);
            return Enumerable.Empty<dynamic>();
        }
    }

    public async Task<dynamic> GetParentProfileAsync(int parentId)
    {
        try
        {
            // Rôle stocké en minuscule partout ailleurs dans le code (JwtService,
            // [Authorize(Roles="parent")], WeeklyParentReportService...) : "Parent"
            // avec majuscule ne correspondait jamais à aucune ligne, rendant cet
            // endpoint systématiquement vide (retour null) pour tous les parents.
            var parent = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == parentId && u.Role == "parent");

            if (parent == null)
                return null;

            return new
            {
                parentId = parent.Id,
                name = $"{parent.FirstName} {parent.LastName}",
                email = parent.Email,
                phone = parent.Phone,
                profileImageUrl = parent.ProfileImageUrl,
                createdAt = parent.CreatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting parent profile");
            return null;
        }
    }

    public async Task<IEnumerable<dynamic>> GetChildGoalsAsync(int parentId, int childId)
    {
        try
        {
            // Récupérer les objectifs de l'enfant depuis la table Goals. Tous les
            // statuts (pas seulement Active) : le frontend parent distingue les
            // propositions en attente (Pending) des objectifs actifs co-construits
            // (Active + proposedByUserId renseigné) à partir de ces deux champs.
            var goals = await _context.Goals
                .AsNoTracking()
                .Where(g => g.UserId == childId)
                .OrderByDescending(g => g.CreatedAt)
                .Select(g => new
                {
                    goalId = g.Id,
                    title = g.Title,
                    description = g.Description,
                    type = g.Type,
                    targetDate = g.TargetDate,
                    progress = g.Progress,
                    status = g.Status,
                    proposedByUserId = g.ProposedByUserId
                })
                .ToListAsync();

            return goals;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting child goals");
            return Enumerable.Empty<dynamic>();
        }
    }
}
