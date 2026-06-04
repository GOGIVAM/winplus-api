using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Interface pour les services des étudiants
/// </summary>
public interface IStudentService
{
    Task<dynamic> GetStudentStatsAsync(int studentId);
    Task<IEnumerable<dynamic>> GetLearningContinueAsync(int studentId);
    Task<IEnumerable<dynamic>> GetExamsRecommendedAsync(int studentId);
    Task<IEnumerable<dynamic>> GetTodayPrioritiesAsync(int studentId);
    Task<IEnumerable<dynamic>> GetStudentGoalsAsync(int studentId);
}

/// <summary>
/// Service pour les données des étudiants
/// </summary>
public class StudentService : IStudentService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<StudentService> _logger;

    public StudentService(ApplicationDbContext context, ILogger<StudentService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<dynamic> GetStudentStatsAsync(int studentId)
    {
        try
        {
            var enrollments = await _context.Enrollments
                .Where(e => e.UserId == studentId)
                .CountAsync();

            var completedEnrollments = await _context.Enrollments
                .Where(e => e.UserId == studentId && e.IsCompleted)
                .CountAsync();

            var histories = await _context.LearningHistories
                .Where(h => h.UserId == studentId)
                .ToListAsync();

            return new
            {
                totalCoursesEnrolled = enrollments,
                coursesCompleted = completedEnrollments,
                hoursSpent = histories.Count() * 2, // Approximation
                averageScore = 0,
                streak = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting student stats");
            return new { totalCoursesEnrolled = 0, coursesCompleted = 0, hoursSpent = 0, averageScore = 0, streak = 0 };
        }
    }

    public async Task<IEnumerable<dynamic>> GetLearningContinueAsync(int studentId)
    {
        try
        {
            var courses = await _context.Enrollments
                .AsNoTracking()
                .Where(e => e.UserId == studentId && !e.IsCompleted)
                .Select(e => new
                {
                    enrollmentId = e.Id,
                    subjectId = e.SubjectId,
                    progress = e.ProgressPercentage
                })
                .Take(5)
                .ToListAsync();

            return courses;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting learning continue");
            return Enumerable.Empty<dynamic>();
        }
    }

    public async Task<IEnumerable<dynamic>> GetExamsRecommendedAsync(int studentId)
    {
        try
        {
            var exams = await _context.Exams
                .AsNoTracking()
                .Where(e => !e.IsDeleted && e.IsPublished)
                .Take(5)
                .ToListAsync();

            return exams.Select(e => new
            {
                examId = e.Id,
                title = e.Title,
                year = e.Year,
                difficulty = e.Difficulty
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recommended exams");
            return Enumerable.Empty<dynamic>();
        }
    }

    public async Task<IEnumerable<dynamic>> GetTodayPrioritiesAsync(int studentId)
    {
        try
        {
            var priorities = new List<dynamic>();

            // 1. Objectifs avec deadline proche
            var urgentGoals = await _context.Goals
                .AsNoTracking()
                .Where(g => g.UserId == studentId && g.Status == "active" && g.TargetDate <= DateTime.UtcNow.AddDays(7))
                .OrderBy(g => g.TargetDate)
                .Select(g => new
                {
                    type = "goal",
                    title = g.Title,
                    description = g.Description ?? "Objectif à atteindre",
                    daysLeft = (int)Math.Ceiling((g.TargetDate - DateTime.UtcNow).TotalDays),
                    priority = "urgent",
                    progress = g.Progress,
                    icon = "bullseye"
                })
                .Take(3)
                .ToListAsync();

            priorities.AddRange(urgentGoals);

            // 2. Cours incomplets avec faible progression
            var incompleteCourses = await _context.Enrollments
                .AsNoTracking()
                .Where(e => e.UserId == studentId && !e.IsCompleted && e.ProgressPercentage < 50)
                .Join(_context.Subjects,
                    e => e.SubjectId,
                    s => s.Id,
                    (e, s) => new
                    {
                        type = "course",
                        title = s.Title,
                        description = $"Progression: {e.ProgressPercentage}%",
                        progress = e.ProgressPercentage,
                        priority = e.ProgressPercentage < 20 ? "urgent" : "high",
                        icon = "book"
                    })
                .OrderBy(x => x.progress)
                .Take(2)
                .ToListAsync();

            priorities.AddRange(incompleteCourses);

            // 3. Quiz à faire
            var quizzesTodo = await _context.Quizzes
                .AsNoTracking()
                .Where(q => q.IsPublished && !q.IsDeleted)
                .Select(q => new
                {
                    type = "quiz",
                    title = q.Title,
                    description = "Quiz à compléter",
                    priority = "medium",
                    icon = "brain"
                })
                .Take(2)
                .ToListAsync();

            priorities.AddRange(quizzesTodo);

            return priorities.OrderBy(p => p.priority == "urgent" ? 0 : (p.priority == "high" ? 1 : 2)).Take(6);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting today priorities");
            return Enumerable.Empty<dynamic>();
        }
    }

    public async Task<IEnumerable<dynamic>> GetStudentGoalsAsync(int studentId)
    {
        try
        {
            var goals = await _context.Goals
                .AsNoTracking()
                .Where(g => g.UserId == studentId && g.Status == "active")
                .Select(g => new
                {
                    goalId = g.Id,
                    title = g.Title,
                    progress = g.Progress,
                    targetDate = g.TargetDate
                })
                .ToListAsync();

            return goals;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting student goals");
            return Enumerable.Empty<dynamic>();
        }
    }
}
