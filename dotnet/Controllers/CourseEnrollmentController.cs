using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;

namespace Backend.Controllers;

/// <summary>
/// Inscription aux formations et liste "Mes formations".
/// POST /api/courses/{id}/enroll  → inscrit l'utilisateur (gratuit / abonnement / achat)
/// GET  /api/my-courses           → formations de l'utilisateur connecté
/// </summary>
[ApiController]
[Authorize]
public class CourseEnrollmentController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<CourseEnrollmentController> _logger;

    public CourseEnrollmentController(ApplicationDbContext db, ILogger<CourseEnrollmentController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpPost("api/courses/{id}/enroll")]
    public async Task<IActionResult> Enroll(int id)
    {
        try
        {
            var userId = User.GetUserId();

            var course = await _db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (course == null || course.Status != "published")
                return NotFound(new { error = "Formation introuvable" });

            // Déjà inscrit ?
            var already = await _db.CourseEnrollments.AnyAsync(
                e => e.UserId == userId && e.CourseId == id);
            if (already)
                return Ok(new { message = "Déjà inscrit", alreadyEnrolled = true });

            // Vérifier l'accès
            string accessType;
            if (course.IsFree)
            {
                accessType = "free";
            }
            else if (course.IsIncludedInSub)
            {
                var hasSub = await _db.Subscriptions.AnyAsync(
                    s => s.UserId == userId && s.IsActive);
                if (!hasSub)
                    return BadRequest(new { error = "Abonnement Premium requis pour accéder à cette formation" });
                accessType = "subscription";
            }
            else
            {
                // Vérifier qu'il y a un order payé pour ce cours
                var paid = await _db.OrderItems
                    .AnyAsync(oi => oi.CourseId == id &&
                              oi.Order!.UserId == userId &&
                              oi.Order.Status == "completed");
                if (!paid)
                    return BadRequest(new { error = "Veuillez acheter cette formation avant de vous inscrire" });
                accessType = "purchase";
            }

            _db.CourseEnrollments.Add(new CourseEnrollment
            {
                UserId     = userId,
                CourseId   = id,
                AccessType = accessType,
                EnrolledAt = DateTime.UtcNow,
                IsActive   = true,
            });
            await _db.SaveChangesAsync();

            _logger.LogInformation("User {UserId} enrolled in course {CourseId} ({AccessType})",
                userId, id, accessType);

            return Ok(new { message = "Inscription réussie", accessType });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enrolling user in course {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("api/my-courses")]
    public async Task<IActionResult> MyCourses()
    {
        try
        {
            var userId = User.GetUserId();

            var enrollments = await _db.CourseEnrollments.AsNoTracking()
                .Where(e => e.UserId == userId && e.IsActive)
                .Include(e => e.Course)
                    .ThenInclude(c => c.Instructor)
                .OrderByDescending(e => e.LastAccessedAt ?? e.EnrolledAt)
                .Select(e => new
                {
                    enrollmentId    = e.Id,
                    accessType      = e.AccessType,
                    enrolledAt      = e.EnrolledAt,
                    lastAccessedAt  = e.LastAccessedAt,
                    progressPercent = e.ProgressPercent,
                    completedAt     = e.CompletedAt,
                    certificateUrl  = e.CertificateUrl,
                    course = new
                    {
                        e.Course.Id, e.Course.Title, e.Course.Slug, e.Course.ThumbnailUrl,
                        e.Course.Level, e.Course.Category, e.Course.TotalDurationMin, e.Course.LessonsCount,
                        instructorName = e.Course.Instructor.FirstName + " " + e.Course.Instructor.LastName,
                    },
                })
                .ToListAsync();

            return Ok(enrollments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting my courses");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
