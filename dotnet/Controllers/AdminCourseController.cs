using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;

namespace Backend.Controllers;

/// <summary>
/// Administration des formations (modération, stats).
/// GET    /api/admin/courses           → toutes les formations (tous statuts)
/// GET    /api/admin/courses/pending   → en attente de validation
/// PUT    /api/admin/courses/{id}/approve
/// PUT    /api/admin/courses/{id}/reject
/// DELETE /api/admin/courses/{id}
/// GET    /api/admin/courses/stats
/// </summary>
[ApiController]
[Authorize]
[Route("api/admin/courses")]
public class AdminCourseController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AdminCourseController> _logger;

    public AdminCourseController(ApplicationDbContext db, ILogger<AdminCourseController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private IActionResult ForbidIfNotAdmin()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
                ?? User.FindFirst("role")?.Value;
        return role == "admin" ? null! : Forbid();
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? category,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var guard = ForbidIfNotAdmin();
        if (guard != null) return guard;

        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var query = _db.Courses.AsNoTracking().Include(c => c.Instructor).AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(c => c.Status == status);
            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(c => c.Category == category);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(c => c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new
                {
                    c.Id, c.Title, c.Slug, c.Status, c.Category, c.Level,
                    c.Price, c.IsFree, c.IsIncludedInSub,
                    c.EnrolledCount, c.AvgRating, c.ReviewsCount, c.LessonsCount,
                    c.CreatedAt, c.PublishedAt,
                    instructor = new
                    {
                        c.Instructor.Id,
                        name = c.Instructor.FirstName + " " + c.Instructor.LastName,
                        c.Instructor.Email,
                    },
                })
                .ToListAsync();

            return Ok(new
            {
                data = items, total, page, pageSize,
                totalPages = (int)Math.Ceiling(total / (double)pageSize),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing courses (admin)");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("pending")]
    public async Task<IActionResult> Pending([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var guard = ForbidIfNotAdmin();
        if (guard != null) return guard;

        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var query = _db.Courses.AsNoTracking()
                .Where(c => c.Status == "pending_review")
                .Include(c => c.Instructor);

            var total = await query.CountAsync();
            var items = await query
                .OrderBy(c => c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new
                {
                    c.Id, c.Title, c.Slug, c.Category, c.Level,
                    c.Price, c.IsFree, c.LessonsCount, c.CreatedAt,
                    instructor = new
                    {
                        c.Instructor.Id,
                        name = c.Instructor.FirstName + " " + c.Instructor.LastName,
                        c.Instructor.Email,
                    },
                })
                .ToListAsync();

            return Ok(new
            {
                data = items, total, page, pageSize,
                totalPages = (int)Math.Ceiling(total / (double)pageSize),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing pending courses");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPut("{id}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        var guard = ForbidIfNotAdmin();
        if (guard != null) return guard;

        try
        {
            var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == id);
            if (course == null) return NotFound(new { error = "Formation introuvable" });

            course.Status      = "published";
            course.PublishedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Admin {AdminId} approved course {CourseId}", User.GetUserId(), id);
            return Ok(new { message = "Formation publiée", courseId = id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving course {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPut("{id}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectCourseRequest req)
    {
        var guard = ForbidIfNotAdmin();
        if (guard != null) return guard;

        try
        {
            var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == id);
            if (course == null) return NotFound(new { error = "Formation introuvable" });

            course.Status = "draft";
            await _db.SaveChangesAsync();

            _logger.LogInformation("Admin {AdminId} rejected course {CourseId}: {Reason}",
                User.GetUserId(), id, req.Reason);

            return Ok(new { message = "Formation rejetée", courseId = id, reason = req.Reason });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting course {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var guard = ForbidIfNotAdmin();
        if (guard != null) return guard;

        try
        {
            var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == id);
            if (course == null) return NotFound(new { error = "Formation introuvable" });

            _db.Courses.Remove(course);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Admin {AdminId} deleted course {CourseId}", User.GetUserId(), id);
            return Ok(new { message = "Formation supprimée" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting course {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var guard = ForbidIfNotAdmin();
        if (guard != null) return guard;

        try
        {
            var total         = await _db.Courses.CountAsync();
            var published     = await _db.Courses.CountAsync(c => c.Status == "published");
            var pending       = await _db.Courses.CountAsync(c => c.Status == "pending_review");
            var drafts        = await _db.Courses.CountAsync(c => c.Status == "draft");
            var archived      = await _db.Courses.CountAsync(c => c.Status == "archived");
            var totalEnrolled = await _db.CourseEnrollments.CountAsync(e => e.IsActive);
            var completed     = await _db.CourseEnrollments.CountAsync(e => e.CompletedAt != null);
            var avgRating     = await _db.CourseReviews.AnyAsync()
                ? await _db.CourseReviews.AverageAsync(r => (double)r.Rating)
                : 0.0;

            var topCourses = await _db.Courses.AsNoTracking()
                .Where(c => c.Status == "published")
                .OrderByDescending(c => c.EnrolledCount)
                .Take(5)
                .Select(c => new { c.Id, c.Title, c.EnrolledCount, c.AvgRating })
                .ToListAsync();

            var byCategory = await _db.Courses.AsNoTracking()
                .Where(c => c.Status == "published" && c.Category != null)
                .GroupBy(c => c.Category)
                .Select(g => new { category = g.Key, count = g.Count(), enrolled = g.Sum(c => c.EnrolledCount) })
                .OrderByDescending(g => g.enrolled)
                .ToListAsync();

            return Ok(new
            {
                total, published, pending, drafts, archived,
                enrollments = new { total = totalEnrolled, completed },
                avgRating    = Math.Round(avgRating, 2),
                topCourses,
                byCategory,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting course stats");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}

public record RejectCourseRequest(string? Reason);
