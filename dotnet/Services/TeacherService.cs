using Backend.Data;
using Backend.Models.Entities;
using Backend.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public interface ITeacherService
{
    Task<IEnumerable<CourseContent>> GetTeacherContentsAsync(int teacherId, int limit = 50);
    Task<IEnumerable<User>> GetTeacherStudentsAsync(int teacherId, int limit = 10);
    Task<IEnumerable<dynamic>> GetPendingCorrectionsAsync(int teacherId);
    Task<IEnumerable<Session>> GetUpcomingSessionsAsync(int teacherId, int limit = 10);
    Task<IEnumerable<dynamic>> GetTeacherQuizzesAsync(int teacherId, int limit = 10);
    Task<IEnumerable<dynamic>> GetTeacherRevisionsAsync(int teacherId, int limit = 10);
    Task<dynamic> GetTeacherStatsAsync(int teacherId);
    Task<dynamic> GetTeacherProfileAsync(int teacherId);
    Task<dynamic> GetTeacherRevenuesAsync(int teacherId);
}

public class TeacherService : ITeacherService
{
    private readonly ApplicationDbContext _context;
    private readonly ISessionRepository _sessionRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<TeacherService> _logger;

    public TeacherService(
        ApplicationDbContext context,
        ISessionRepository sessionRepository,
        IUserRepository userRepository,
        ILogger<TeacherService> logger)
    {
        _context = context;
        _sessionRepository = sessionRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<IEnumerable<CourseContent>> GetTeacherContentsAsync(int teacherId, int limit = 50)
    {
        try
        {
            return await _context.CourseContents
                .AsNoTracking()
                .OrderByDescending(c => c.CreatedAt)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher contents for teacher {TeacherId}", teacherId);
            return Enumerable.Empty<CourseContent>();
        }
    }

    public async Task<IEnumerable<User>> GetTeacherStudentsAsync(int teacherId, int limit = 10)
    {
        try
        {
            var enrollments = await _context.Enrollments
                .AsNoTracking()
                .Where(e => e.EnrolledAt > DateTime.UtcNow.AddMonths(-1))
                .OrderByDescending(e => e.EnrolledAt)
                .Take(limit)
                .Select(e => e.UserId)
                .ToListAsync();

            var students = await _context.Users
                .AsNoTracking()
                .Where(u => enrollments.Contains(u.Id))
                .ToListAsync();

            return students;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher students for teacher {TeacherId}", teacherId);
            return Enumerable.Empty<User>();
        }
    }

    public async Task<IEnumerable<dynamic>> GetPendingCorrectionsAsync(int teacherId)
    {
        try
        {
            // Placeholder: needs a Submissions/Corrections table
            return Enumerable.Empty<dynamic>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending corrections for teacher {TeacherId}", teacherId);
            return Enumerable.Empty<dynamic>();
        }
    }

    public async Task<IEnumerable<Session>> GetUpcomingSessionsAsync(int teacherId, int limit = 10)
    {
        try
        {
            var sessions = await _sessionRepository.GetByTeacherAsync(teacherId);
            var now = DateTime.UtcNow;
            
            return sessions
                .Where(s => s.StartDate > now)
                .OrderBy(s => s.StartDate)
                .Take(limit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting upcoming sessions for teacher {TeacherId}", teacherId);
            return Enumerable.Empty<Session>();
        }
    }

    public async Task<IEnumerable<dynamic>> GetTeacherQuizzesAsync(int teacherId, int limit = 10)
    {
        try
        {
            // Placeholder: needs a Quizzes table
            return Enumerable.Empty<dynamic>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher quizzes for teacher {TeacherId}", teacherId);
            return Enumerable.Empty<dynamic>();
        }
    }

    public async Task<IEnumerable<dynamic>> GetTeacherRevisionsAsync(int teacherId, int limit = 10)
    {
        try
        {
            // Placeholder: needs a Revisions table
            return Enumerable.Empty<dynamic>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher revisions for teacher {TeacherId}", teacherId);
            return Enumerable.Empty<dynamic>();
        }
    }

    public async Task<dynamic> GetTeacherStatsAsync(int teacherId)
    {
        try
        {
            var enrollments = await _context.Enrollments
                .AsNoTracking()
                .Where(e => e.EnrolledAt > DateTime.UtcNow.AddMonths(-3))
                .CountAsync();

            var reviews = await _context.Reviews
                .AsNoTracking()
                .Where(r => !r.IsDeleted)
                .AverageAsync(r => (double)r.Rating);

            var contents = await _context.CourseContents
                .AsNoTracking()
                .CountAsync();

            var sessions = await _sessionRepository.GetByTeacherAsync(teacherId);
            
            return new
            {
                totalStudents = enrollments,
                averageRating = Math.Round(reviews, 2),
                contentCount = contents,
                sessionCount = sessions.Count()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher stats for teacher {TeacherId}", teacherId);
            return new { totalStudents = 0, averageRating = 0, contentCount = 0, sessionCount = 0 };
        }
    }

    public async Task<dynamic> GetTeacherProfileAsync(int teacherId)
    {
        try
        {
            var teacher = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == teacherId && u.Role == "Teacher");

            if (teacher == null)
                return null;

            return new
            {
                teacherId = teacher.Id,
                name = $"{teacher.FirstName} {teacher.LastName}",
                email = teacher.Email,
                phone = teacher.Phone,
                profileImageUrl = teacher.ProfileImageUrl,
                bio = teacher.Bio,
                createdAt = teacher.CreatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher profile");
            return null;
        }
    }

    public async Task<dynamic> GetTeacherRevenuesAsync(int teacherId)
    {
        try
        {
            // Récupérer les commandes où le créateur/professeur a des revenus
            var totalRevenue = await _context.Orders
                .AsNoTracking()
                .Where(o => o.Status == "completed")
                .SumAsync(o => o.TotalAmount);

            var monthlyRevenue = await _context.Orders
                .AsNoTracking()
                .Where(o => o.Status == "completed" && o.CreatedAt.Month == DateTime.Now.Month)
                .SumAsync(o => o.TotalAmount);

            var transactionCount = await _context.Orders
                .AsNoTracking()
                .Where(o => o.Status == "completed")
                .CountAsync();

            return new
            {
                totalRevenue = totalRevenue,
                monthlyRevenue = monthlyRevenue,
                transactionCount = transactionCount,
                averagePerTransaction = transactionCount > 0 ? totalRevenue / transactionCount : 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher revenues");
            return new { totalRevenue = 0, monthlyRevenue = 0, transactionCount = 0, averagePerTransaction = 0 };
        }
    }
}
