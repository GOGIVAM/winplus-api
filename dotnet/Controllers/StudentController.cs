using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Services;
using Backend.Extensions;

namespace Backend.Controllers;

/// <summary>
/// Controller pour les données des étudiants (StudentDashboard)
/// </summary>
[ApiController]
[Route("api/student")]
[Produces("application/json")]
[Authorize]
public class StudentController : ControllerBase
{
    private readonly ILogger<StudentController> _logger;
    private readonly ApplicationDbContext _db;

    public StudentController(ILogger<StudentController> logger, ApplicationDbContext db)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _db = db;
    }

    /// <summary>
    /// Récupère les statistiques de l'étudiant avec priorités, objectifs et événements à venir
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetStats()
    {
        try
        {
            var userId = User.GetUserId();

            var totalCoursesEnrolled = await _db.Enrollments
                .CountAsync(e => e.UserId == userId);

            var coursesCompleted = await _db.Enrollments
                .CountAsync(e => e.UserId == userId && e.IsCompleted);

            var histories = await _db.LearningHistories
                .Where(h => h.UserId == userId)
                .Select(h => new { h.QuizScore, h.TimeSpentSeconds, h.ActivityType })
                .ToListAsync();

            var averageScore = histories.Any(h => h.QuizScore.HasValue)
                ? histories.Where(h => h.QuizScore.HasValue).Average(h => (double)h.QuizScore!.Value)
                : 0.0;

            var totalTimeSeconds = histories.Sum(h => h.TimeSpentSeconds ?? 0);

            var quizCompleted = histories.Count(h =>
                h.ActivityType != null && h.ActivityType.ToLower().Contains("quiz"));

            var priorities = await _db.Goals
                .Where(g => g.UserId == userId && g.Status == "in_progress")
                .OrderBy(g => g.TargetDate)
                .Take(5)
                .Select(g => new
                {
                    g.Id,
                    g.Title,
                    g.Description,
                    g.Type,
                    g.Progress,
                    g.Status,
                    g.TargetDate,
                    g.CreatedAt
                })
                .ToListAsync();

            var goals = await _db.Goals
                .Where(g => g.UserId == userId)
                .OrderByDescending(g => g.CreatedAt)
                .Select(g => new
                {
                    g.Id,
                    g.Title,
                    g.Description,
                    g.Type,
                    g.Progress,
                    g.Status,
                    g.TargetDate,
                    g.CreatedAt,
                    g.CompletedAt
                })
                .ToListAsync();

            var upcomingEvents = await _db.Events
                .Where(e => e.StartDate > DateTime.UtcNow && !e.IsDeleted)
                .OrderBy(e => e.StartDate)
                .Take(5)
                .Select(e => new
                {
                    e.Id,
                    e.Title,
                    e.Description,
                    e.StartDate,
                    e.EndDate,
                    e.Location,
                    e.EventType
                })
                .ToListAsync();

            var data = new
            {
                stats = new
                {
                    totalCoursesEnrolled,
                    coursesCompleted,
                    averageScore = Math.Round(averageScore, 2),
                    totalTimeSeconds,
                    quizCompleted
                },
                priorities,
                goals,
                upcomingEvents
            };

            return Ok(new { data, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting student stats");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les cours à reprendre (continue studying)
    /// </summary>
    [HttpGet("learning/continue")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetLearningContinue()
    {
        try
        {
            var studentId = User.GetUserId();
            var courses = new List<dynamic>();
            return Ok(new { data = courses, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting continue studying");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les examens recommandés
    /// </summary>
    [HttpGet("exams/recommended")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetExamsRecommended()
    {
        try
        {
            var studentId = User.GetUserId();
            var exams = new List<dynamic>();
            return Ok(new { data = exams, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recommended exams");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les priorités du jour
    /// </summary>
    [HttpGet("priorities/today")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetTodayPriorities()
    {
        try
        {
            var studentId = User.GetUserId();
            var priorities = new List<dynamic>();
            return Ok(new { data = priorities, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting today priorities");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les événements à venir
    /// </summary>
    [HttpGet("events/upcoming")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetUpcomingEvents()
    {
        try
        {
            var studentId = User.GetUserId();
            var events = new List<dynamic>();
            return Ok(new { data = events, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting upcoming events");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les objectifs de l'étudiant
    /// </summary>
    [HttpGet("goals")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetGoals()
    {
        try
        {
            var studentId = User.GetUserId();
            var goals = new List<dynamic>();
            return Ok(new { data = goals, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting student goals");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
