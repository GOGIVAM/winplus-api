using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Services;

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
    private readonly IStorageService _storage;

    // Module 27 (lot 8) : même TTL que CoursePlayerController pour les leçons
    // en libre aperçu (pas d'inscription requise, mais pas de lien S3 permanent
    // non plus).
    private static readonly TimeSpan LessonMediaTtl = TimeSpan.FromMinutes(60);

    public CourseCatalogController(ApplicationDbContext db, ILogger<CourseCatalogController> logger, IStorageService storage)
    {
        _db = db;
        _logger = logger;
        _storage = storage;
    }

    private string? Presign(string? urlOrKey) =>
        string.IsNullOrWhiteSpace(urlOrKey) ? null : _storage.PresignGetUrl(urlOrKey, LessonMediaTtl);

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

            // Sans .AsQueryable(), `var` type la variable en
            // IIncludableQueryable<Course, User> : les reaffectations
            // `query = query.Where(...)` qui suivent renvoient un IQueryable et
            // ne compilent pas (CS0266). Meme correctif que
            // AdminCourseController ligne 54, qui compile deja.
            var query = _db.Courses.AsNoTracking()
                .Where(c => c.Status == "published")
                .Include(c => c.Instructor)
                .AsQueryable();

            // Catégorie et niveau reçus dans l'une ou l'autre langue.
            var index = HttpContext?.RequestServices.GetService<Backend.Services.Translation.IContentTranslationIndex>();
            var snapshot = index == null ? null : await index.GetAsync();
            if (!string.IsNullOrWhiteSpace(category))
            {
                var categories = snapshot?.FrenchEquivalents(category) ?? new[] { category.Trim().ToLowerInvariant() };
                query = query.Where(c => c.Category != null && categories.Contains(c.Category.ToLower()));
            }
            if (!string.IsNullOrWhiteSpace(level))
            {
                var levels = snapshot?.FrenchEquivalents(level) ?? new[] { level.Trim().ToLowerInvariant() };
                query = query.Where(c => levels.Contains(c.Level.ToLower()));
            }
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
                    // Lot 9.7b (18.16, F6) : plus d'inclusion dans l'abonnement ;
                    // Price est le prix TTC payé (18.21).
                    c.Level, c.Category, c.Price, c.IsFree,
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
                     (c.TitleEn != null && c.TitleEn.ToLower().Contains(lower)) ||
                     (c.ShortDescriptionEn != null && c.ShortDescriptionEn.ToLower().Contains(lower)) ||
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

            // Lot 9.7b (F1, F2) : état d'achat. Une formation payée dont
            // l'inscription manque (achat d'avant le 9.7b) est rattrapée ici,
            // par la même règle que la confirmation du paiement.
            bool purchased = false;
            string? purchaseBlockCode = null;
            if (userId.HasValue)
            {
                purchased = await CourseSales.IsPurchasedAsync(_db, userId.Value, id);
                if (purchased && !enrolled)
                {
                    try
                    {
                        var paidOrderId = await CourseSales.OtherPaidOrderIdAsync(_db, userId.Value, id, excludedOrderId: 0);
                        if (paidOrderId != null)
                            await CourseSales.SyncEnrollmentsForOrderAsync(_db, paidOrderId.Value);
                        enrolled = await _db.CourseEnrollments.AnyAsync(
                            e => e.UserId == userId && e.CourseId == id && e.IsActive);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Rattrapage de l'inscription achetée impossible (formation {CourseId})", id);
                    }
                }
                purchaseBlockCode = (await CourseSales.PurchaseBlockAsync(_db, userId.Value, userId.Value, course))?.Code;
            }

            return Ok(new
            {
                course.Id, course.Title, course.Slug, course.Description, course.ShortDescription,
                course.ThumbnailUrl, course.PreviewVideoUrl, course.Language, course.Level,
                // Price : prix TTC payé (18.21). Plus d'inclusion dans l'abonnement (18.16).
                course.Category, course.Tags, course.Price, course.IsFree,
                priceExclTax = VatPolicy.ExclTaxFromInclTax(course.IsFree ? 0 : course.Price),
                vatIncluded = VatPolicy.TaxIncludedIn(course.IsFree ? 0 : course.Price),
                isPurchased = purchased,
                isOwner = userId.HasValue && course.InstructorId == userId.Value,
                // free_course | own_course | already_enrolled | already_purchased, ou null si achetable.
                purchaseBlockCode,
                course.TotalDurationMin, course.LessonsCount, course.EnrolledCount,
                course.AvgRating, course.ReviewsCount, course.Requirements, course.Objectives,
                course.CertificateEnabled, course.CanalMessagerie, course.CreatedAt,
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
                        // Contenu uniquement si preview ou inscrit ; jamais de lien S3
                        // permanent (Module 27, lot 8) — toujours une URL présignée.
                        videoUrl    = (l.IsPreview || enrolled) ? Presign(l.VideoUrl) : null,
                        fileUrl     = (l.IsPreview || enrolled) ? Presign(l.FileUrl) : null,
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
