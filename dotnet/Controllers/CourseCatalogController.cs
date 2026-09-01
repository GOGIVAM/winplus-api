using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;

namespace Backend.Controllers;

/// <summary>
/// Catalogue public des formations.
/// GET  /api/courses               → liste paginée (published uniquement)
/// GET  /api/courses/search        → recherche full-text
/// GET  /api/courses/{id}          → détail + sections + leçons preview
/// GET  /api/courses/{id}/reviews  → avis paginés
/// POST /api/courses/{id}/reviews  → soumettre un avis (inscrit uniquement)
/// </summary>
[ApiController]
[Route("api/courses")]
public class CourseCatalogController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<CourseCatalogController> _logger;

    public CourseCatalogController(ApplicationDbContext db, ILogger<CourseCatalogController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? category,
        [FromQuery] string? level,
        [FromQuery] bool? free,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var query = _db.Courses.AsNoTracking()
                .Where(c => c.Status == "published")
                .Include(c => c.Instructor);

            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(c => c.Category == category);
            if (!string.IsNullOrWhiteSpace(level))
                query = query.Where(c => c.Level == level);
            if (free.HasValue)
                query = query.Where(c => c.IsFree == free.Value);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(c => c.EnrolledCount)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new
                {
                    c.Id, c.Title, c.Slug, c.ShortDescription, c.ThumbnailUrl,
                    c.Level, c.Category, c.Price, c.IsFree, c.IsIncludedInSub,
                    c.TotalDurationMin, c.LessonsCount, c.EnrolledCount,
                    c.AvgRating, c.ReviewsCount, c.Tags,
                    instructorName = c.Instructor.FirstName + " " + c.Instructor.LastName,
                    c.CreatedAt,
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
            _logger.LogError(ex, "Error listing courses");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(new { error = "q is required" });

            var lower = q.ToLower();
            var items = await _db.Courses.AsNoTracking()
                .Where(c => c.Status == "published" &&
                    (c.Title.ToLower().Contains(lower) ||
                     (c.ShortDescription != null && c.ShortDescription.ToLower().Contains(lower)) ||
                     (c.Category != null && c.Category.ToLower().Contains(lower))))
                .Include(c => c.Instructor)
                .OrderByDescending(c => c.EnrolledCount)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(c => new
                {
                    c.Id, c.Title, c.Slug, c.ShortDescription, c.ThumbnailUrl,
                    c.Level, c.Category, c.Price, c.IsFree, c.TotalDurationMin,
                    c.LessonsCount, c.EnrolledCount, c.AvgRating,
                    instructorName = c.Instructor.FirstName + " " + c.Instructor.LastName,
                })
                .ToListAsync();

            return Ok(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching courses");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        try
        {
            var course = await _db.Courses.AsNoTracking()
                .Where(c => c.Id == id && c.Status == "published")
                .Include(c => c.Instructor)
                .Include(c => c.Sections.OrderBy(s => s.Position))
                    .ThenInclude(s => s.Lessons.Where(l => l.IsPublished).OrderBy(l => l.Position))
                .FirstOrDefaultAsync();

            if (course == null) return NotFound(new { error = "Formation introuvable" });

            // Déterminer si l'utilisateur est inscrit (pour afficher le contenu complet ou non)
            int? userId = null;
            try { userId = User.GetUserId(); } catch { }

            bool enrolled = userId.HasValue && await _db.CourseEnrollments.AnyAsync(
                e => e.UserId == userId && e.CourseId == id && e.IsActive);

            return Ok(new
            {
                course.Id, course.Title, course.Slug, course.Description, course.ShortDescription,
                course.ThumbnailUrl, course.PreviewVideoUrl, course.Language, course.Level,
                course.Category, course.Tags, course.Price, course.IsFree, course.IsIncludedInSub,
                course.TotalDurationMin, course.LessonsCount, course.EnrolledCount,
                course.AvgRating, course.ReviewsCount, course.Requirements, course.Objectives,
                course.CertificateEnabled, course.CreatedAt,
                instructor = new
                {
                    id = course.Instructor.Id,
                    name = course.Instructor.FirstName + " " + course.Instructor.LastName,
                    avatarUrl = course.Instructor.AvatarUrl,
                },
                isEnrolled = enrolled,
                sections = course.Sections.OrderBy(s => s.Position).Select(s => new
                {
                    s.Id, s.Title, s.Description, s.Position,
                    lessons = s.Lessons.OrderBy(l => l.Position).Select(l => new
                    {
                        l.Id, l.Title, l.LessonType, l.VideoDurationSec,
                        l.IsPreview, l.Position,
                        // Contenu uniquement si preview ou inscrit
                        videoUrl    = (l.IsPreview || enrolled) ? l.VideoUrl : null,
                        fileUrl     = (l.IsPreview || enrolled) ? l.FileUrl : null,
                        articleContent = (l.IsPreview || enrolled) ? l.ArticleContent : null,
                    }),
                }),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting course {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("{id}/reviews")]
    public async Task<IActionResult> GetReviews(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var total = await _db.CourseReviews.CountAsync(r => r.CourseId == id);
            var items = await _db.CourseReviews.AsNoTracking()
                .Where(r => r.CourseId == id)
                .Include(r => r.User)
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(r => new
                {
                    r.Id, r.Rating, r.Comment, r.IsVerified, r.CreatedAt,
                    author = new { r.User.Id, name = r.User.FirstName + " " + r.User.LastName, r.User.AvatarUrl },
                })
                .ToListAsync();

            return Ok(new { data = items, total, page, pageSize });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting reviews for course {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("{id}/reviews")]
    [Authorize]
    public async Task<IActionResult> SubmitReview(int id, [FromBody] SubmitReviewRequest req)
    {
        try
        {
            var userId = User.GetUserId();
            if (req.Rating < 1 || req.Rating > 5)
                return BadRequest(new { error = "La note doit être entre 1 et 5" });

            var enrolled = await _db.CourseEnrollments.AnyAsync(
                e => e.UserId == userId && e.CourseId == id && e.IsActive);
            if (!enrolled)
                return Forbid();

            var existing = await _db.CourseReviews.FirstOrDefaultAsync(
                r => r.UserId == userId && r.CourseId == id);

            if (existing != null)
            {
                existing.Rating  = (short)req.Rating;
                existing.Comment = req.Comment;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                var completed = await _db.CourseEnrollments.AnyAsync(
                    e => e.UserId == userId && e.CourseId == id && e.CompletedAt != null);
                _db.CourseReviews.Add(new Backend.Models.Entities.CourseReview
                {
                    CourseId   = id,
                    UserId     = userId,
                    Rating     = (short)req.Rating,
                    Comment    = req.Comment,
                    IsVerified = completed,
                });
            }

            await _db.SaveChangesAsync();
            return Ok(new { message = "Avis enregistré" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting review for course {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}

public record SubmitReviewRequest(int Rating, string? Comment);
