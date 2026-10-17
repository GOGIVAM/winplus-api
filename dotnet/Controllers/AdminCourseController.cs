using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Administration des formations (modération, stats).
/// GET    /api/admin/courses           → formations filtrées (statut, catégorie, recherche), paginées (≤ 100)
/// GET    /api/admin/courses/pending   → en attente de validation
/// PUT    /api/admin/courses/{id}/approve
/// PUT    /api/admin/courses/{id}/reject   (motif obligatoire, stocké et notifié au formateur)
/// DELETE /api/admin/courses/{id}?reason=  (archivage si la formation a des inscrits ou des achats)
/// GET    /api/admin/courses/stats
///
/// Lot 9.7a (18.1) : la vérification de rôle « faite main » (lecture du claim
/// et Forbid) est remplacée par la politique AdminOnly, comme les autres
/// contrôleurs d'administration ; un pageSize supérieur à 100 est ramené à
/// 100 (il retombait à 20, d'où des listes tronquées sans le dire).
/// </summary>
[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin/courses")]
public class AdminCourseController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AdminCourseController> _logger;
    private readonly INtfyService _ntfy;
    private readonly IAdminAuditService _audit;
    private readonly IFastApiClient _fastApi;
    private readonly ICatalogCommissionService _commission;
    private readonly Backend.Services.Entitlements.IEntitlementService _entitlements;

    public AdminCourseController(ApplicationDbContext db, ILogger<AdminCourseController> logger, INtfyService ntfy, IAdminAuditService audit,
        IFastApiClient fastApi, ICatalogCommissionService commission, Backend.Services.Entitlements.IEntitlementService entitlements)
    {
        _entitlements = entitlements;
        _db = db;
        _logger = logger;
        _ntfy = ntfy;
        _audit = audit;
        _fastApi = fastApi;
        _commission = commission;
    }

    private static (int Page, int PageSize) Paging(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? category,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            (page, pageSize) = Paging(page, pageSize);

            var query = _db.Courses.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && status != "all")
                query = query.Where(c => c.Status == status);
            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(c => c.Category == category);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(c => c.Title.ToLower().Contains(term)
                    || (c.Instructor.FirstName + " " + c.Instructor.LastName).ToLower().Contains(term));
            }

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(c => c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new
                {
                    c.Id, c.Title, c.Slug, c.Status, c.Category, c.Level,
                    // Lot 9.7b : prix TTC (18.21), plus d'abonnement (18.16),
                    // évaluation WinAI et part plateforme (F5).
                    c.Price, c.IsFree,
                    c.WinAiScore, c.WinAiJustification, c.PlatformCommissionRate,
                    c.EnrolledCount, c.AvgRating, c.ReviewsCount, c.LessonsCount,
                    c.CreatedAt, c.PublishedAt, c.RejectionReason, c.ReviewedAt,
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
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    [HttpGet("pending")]
    public Task<IActionResult> Pending([FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        List("pending_review", null, null, page, pageSize);

    [HttpPut("{id}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        try
        {
            var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == id);
            if (course == null) return NotFound(new { error = "Formation introuvable" });

            // Lot 9.7b : limite de formations publiées simultanément du plan
            // du professeur (teacher.max_published_courses). Seules les
            // nouvelles publications sont bloquées ; rien n'est dépublié.
            var limit = await Backend.Services.Entitlements.CoursePublicationLimits.CheckApproveAsync(
                _db, _entitlements, course.InstructorId, course.Id, course.Status);
            if (!limit.Allowed) return Conflict(limit.Refusal);

            // Lot 9.7b (F5) : une formation payante jamais évaluée (soumise
            // avant le 9.7b) l'est à l'approbation ; sinon le taux fixé à la
            // soumission est conservé. F10 : gratuit et prix réconciliés.
            CourseSales.ReconcileFreeAndPrice(course);
            if (!CourseSales.IsFreeCourse(course) && course.PlatformCommissionRate == null)
                await CourseCommissionEvaluation.EvaluateAndStoreAsync(_db, _fastApi, _commission, _logger, course);

            var before = course.Status;
            course.Status      = "published";
            course.PublishedAt = DateTime.UtcNow;
            course.ReviewedAt  = DateTime.UtcNow;
            course.ReviewedBy  = User.GetUserId();
            course.RejectionReason = null;
            this.AuditAdd(_audit, AdminAuditActions.CourseApproved, "Course", id, course.Title,
                new { status = before }, new { status = course.Status });
            await _db.SaveChangesAsync();

            await NotifyInstructorAsync(course, "Formation publiée",
                $"Ta formation « {course.Title} » a été validée et est maintenant publiée.");

            _logger.LogInformation("Admin {AdminId} approved course {CourseId}", User.GetUserId(), id);
            return Ok(new { message = "Formation publiée", courseId = id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving course {Id}", id);
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    [HttpPut("{id}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectCourseRequest req)
    {
        var reason = (req?.Reason ?? string.Empty).Trim();
        if (reason.Length < AdminUsersController.MinReasonLength)
            return BadRequest(new { code = "reason_required", error = $"Le motif du rejet est obligatoire ({AdminUsersController.MinReasonLength} caractères au moins) : il est envoyé au formateur." });
        if (reason.Length > 1000) reason = reason[..1000];

        try
        {
            var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == id);
            if (course == null) return NotFound(new { error = "Formation introuvable" });

            var before = course.Status;
            course.Status = "draft";
            course.RejectionReason = reason;
            course.ReviewedAt = DateTime.UtcNow;
            course.ReviewedBy = User.GetUserId();
            this.AuditAdd(_audit, AdminAuditActions.CourseRejected, "Course", id, course.Title,
                new { status = before }, new { status = course.Status }, reason);
            await _db.SaveChangesAsync();

            await NotifyInstructorAsync(course, "Formation à revoir",
                $"Ta formation « {course.Title} » n'a pas été validée. Motif : {reason}. Corrige-la puis soumets-la à nouveau.");

            _logger.LogInformation("Admin {AdminId} rejected course {CourseId}: {Reason}", User.GetUserId(), id, reason);
            return Ok(new { message = "Formation rejetée", courseId = id, reason });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting course {Id}", id);
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>
    /// Supprime une formation sans inscrit ni achat ; une formation suivie ou
    /// achetée est archivée (retirée du catalogue, accès des inscrits
    /// conservé) plutôt que détruite avec les inscriptions et les commandes.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < AdminUsersController.MinReasonLength)
            return BadRequest(new { code = "reason_required", error = $"Un motif d'au moins {AdminUsersController.MinReasonLength} caractères est obligatoire." });
        try
        {
            var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == id);
            if (course == null) return NotFound(new { error = "Formation introuvable" });

            var used = await _db.CourseEnrollments.AnyAsync(e => e.CourseId == id)
                    || await _db.OrderItems.AnyAsync(oi => oi.CourseId == id);
            var before = course.Status;
            if (used)
            {
                course.Status = "archived";
                course.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _db.Courses.Remove(course);
            }
            this.AuditAdd(_audit, AdminAuditActions.CourseDeleted, "Course", id, course.Title,
                new { status = before }, new { status = used ? "archived" : "deleted" }, reason);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Admin {AdminId} {Action} course {CourseId}", User.GetUserId(), used ? "archived" : "deleted", id);
            return Ok(new
            {
                archived = used,
                message = used
                    ? "Formation archivée : elle a des inscrits ou des achats, leur accès est conservé."
                    : "Formation supprimée",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting course {Id}", id);
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
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
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    private async Task NotifyInstructorAsync(Course course, string title, string message)
    {
        try
        {
            await _ntfy.PublishAsync($"winplus-user-{course.InstructorId}", title, message,
                userId: course.InstructorId, type: "Course", relatedEntityType: "Course", relatedEntityId: course.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification au formateur non envoyée (formation {CourseId})", course.Id);
        }
    }
}

public record RejectCourseRequest(string? Reason);
