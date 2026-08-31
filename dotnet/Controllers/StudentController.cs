using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Services;
using Backend.Extensions;
using Microsoft.AspNetCore.Http;

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
    private readonly IDailyScoreService _dailyScore;

    public StudentController(ILogger<StudentController> logger, ApplicationDbContext db, IDailyScoreService dailyScore)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _db = db;
        _dailyScore = dailyScore;
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

    /// <summary>
    /// Récupère les statistiques de l'étudiant (alias de /stats sans priorities/goals/events)
    /// </summary>
    [HttpGet("statistics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetStatistics()
    {
        try
        {
            var userId = User.GetUserId();

            var totalCoursesEnrolled = await _db.Enrollments
                .CountAsync(e => e.UserId == userId);

            var completedCourses = await _db.Enrollments
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

            var data = new
            {
                totalCoursesEnrolled,
                completedCourses,
                averageScore = Math.Round(averageScore, 2),
                totalTimeSeconds,
                quizCompleted
            };

            return Ok(new { data, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting student statistics");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Historique de scores journaliers pour le graphique d'analyse
    /// </summary>
    [HttpGet("score-history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScoreHistory([FromQuery] string period = "30d")
    {
        try
        {
            var userId = User.GetUserId();
            var history = await _dailyScore.GetScoreHistoryAsync(userId, period);
            return Ok(new { success = true, history });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting score history");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère la progression des cours de l'étudiant
    /// </summary>
    [HttpGet("progress")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetProgress()
    {
        try
        {
            var userId = User.GetUserId();

            var enrollments = await _db.Enrollments
                .Where(e => e.UserId == userId)
                .Select(e => new
                {
                    e.SubjectId,
                    subjectTitle = e.Subject != null ? e.Subject.Title : null,
                    e.IsCompleted,
                    e.ProgressPercentage,
                    e.EnrolledAt
                })
                .ToListAsync();

            var mastered   = enrollments.Where(e => e.IsCompleted).ToList();
            var inProgress = enrollments.Where(e => !e.IsCompleted && e.ProgressPercentage > 0).ToList();
            var difficult  = enrollments.Where(e => !e.IsCompleted && e.ProgressPercentage == 0).ToList();

            var data = new
            {
                total      = enrollments.Count,
                mastered   = new { count = mastered.Count,   items = mastered },
                inProgress = new { count = inProgress.Count, items = inProgress },
                difficult  = new { count = difficult.Count,  items = difficult }
            };

            return Ok(new { data, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting student progress");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Retourne les liaisons de l'étudiant (parents, profs, institution).</summary>
    [HttpGet("links")]
    [Authorize(Roles = "student")]
    public async Task<IActionResult> GetMyLinks()
    {
        try
        {
            var me = User.GetUserId();

            var parents = await _db.ParentStudentLinks
                .AsNoTracking()
                .Where(l => l.StudentId == me)
                .Include(l => l.Parent)
                .Select(l => new
                {
                    l.Parent!.Id,
                    l.Parent.FirstName,
                    l.Parent.LastName,
                    l.Parent.AvatarUrl,
                    Role = "parent",
                })
                .ToListAsync();

            var teachersViaClass = await _db.TeacherClassStudents
                .AsNoTracking()
                .Where(tcs => tcs.StudentId == me)
                .Include(tcs => tcs.TeacherClass).ThenInclude(tc => tc.Teacher)
                .Select(tcs => new
                {
                    tcs.TeacherClass.Teacher!.Id,
                    tcs.TeacherClass.Teacher.FirstName,
                    tcs.TeacherClass.Teacher.LastName,
                    tcs.TeacherClass.Teacher.AvatarUrl,
                    Role = "teacher",
                    Source = "class",
                    ClassName = tcs.TeacherClass.Name,
                })
                .Distinct()
                .ToListAsync();

            var teachersDirect = await _db.TeacherStudentLinks
                .AsNoTracking()
                .Where(l => l.StudentId == me && l.Status == "accepted")
                .Include(l => l.Teacher)
                .Select(l => new
                {
                    l.Teacher!.Id,
                    l.Teacher.FirstName,
                    l.Teacher.LastName,
                    l.Teacher.AvatarUrl,
                    Role = "teacher",
                    Source = "direct",
                    ClassName = (string?)null,
                })
                .ToListAsync();

            object? institution = null;
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == me);
            if (user?.InstitutionId != null)
            {
                var inst = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == user.InstitutionId);
                if (inst != null)
                    institution = new { inst.Id, inst.FirstName, inst.LastName, inst.AvatarUrl };
            }

            var groups = await _db.StudyGroupMembers
                .AsNoTracking()
                .Where(m => m.UserId == me)
                .Include(m => m.StudyGroup)
                .Select(m => new { m.StudyGroup!.Id, m.StudyGroup.Name })
                .ToListAsync();

            return Ok(new
            {
                parents,
                teachers = teachersViaClass.Cast<object>().Concat(teachersDirect.Cast<object>()),
                institution,
                groups,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting student links");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// GET /api/student/peer-comparison
    /// Compare la moyenne de l'étudiant courant avec ses pairs anonymisés au même niveau.
    /// </summary>
    [HttpGet("peer-comparison")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPeerComparison()
    {
        try
        {
            var userId = User.GetUserId();

            // User's daily score average (last 30 days)
            var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
            var userScores = await _db.DailyScores
                .Where(s => s.UserId == userId && s.Date >= cutoff)
                .Select(s => (double)s.AverageScore)
                .ToListAsync();

            if (!userScores.Any())
                return Ok(new { hasData = false });

            var userAvg = Math.Round(userScores.Average(), 1);

            // Peer average: all students who have scores in same period (anonymised)
            var allScores = await _db.DailyScores
                .Where(s => s.Date >= cutoff && s.UserId != userId)
                .GroupBy(s => s.UserId)
                .Select(g => g.Average(s => (double)s.AverageScore))
                .ToListAsync();

            if (!allScores.Any())
                return Ok(new { hasData = false });

            var peerAvg = Math.Round(allScores.Average(), 1);
            var topPct  = allScores.OrderByDescending(s => s).Take(Math.Max(1, allScores.Count / 5)).Average();
            var topPerformersAvg = Math.Round(topPct, 1);

            // Percentile: how many peers score BELOW this student
            var belowCount = allScores.Count(s => s < userAvg);
            var percentile = (int)Math.Round((double)belowCount / allScores.Count * 100);

            return Ok(new
            {
                hasData = true,
                userAvg,
                peerAvg,
                percentile,
                topPerformersAvg,
                sampleSize = allScores.Count,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting peer comparison");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
