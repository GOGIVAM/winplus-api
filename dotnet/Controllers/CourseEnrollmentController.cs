using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;

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
    private readonly Backend.Services.IContentAccessService _contentAccess;

    public CourseEnrollmentController(
        ApplicationDbContext db,
        ILogger<CourseEnrollmentController> logger,
        Backend.Services.IContentAccessService contentAccess)
    {
        _db = db;
        _logger = logger;
        _contentAccess = contentAccess;
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

            // Déjà inscrit ? Seule une inscription active compte : depuis la
            // décision 10.11, l'approbation d'un remboursement (ou la fin de
            // l'abonnement remboursé) désactive l'inscription au lieu de la
            // supprimer, pour garder la progression. Sans cette distinction, un
            // rachat ou un réabonnement renvoyait « Déjà inscrit » sur une
            // inscription inactive, que le lecteur refuse.
            var existingActive = await _db.CourseEnrollments
                .Where(e => e.UserId == userId && e.CourseId == id)
                .Select(e => (bool?)e.IsActive)
                .FirstOrDefaultAsync();
            if (existingActive == true)
                return Ok(new { message = "Déjà inscrit", alreadyEnrolled = true });

            // Vérifier l'accès
            string accessType;
            if (course.IsFree)
            {
                accessType = "free";
            }
            else if (course.IsIncludedInSub)
            {
                // Règle unique (ContentAccessService) : abonnement non
                // supprimé, actif, de statut "active", et surtout porté par un
                // plan payant. Le test local précédent ne regardait que
                // IsActive : un abonnement sur un plan gratuit créable sans
                // paiement ouvrait donc les formations réservées aux abonnés.
                var hasSub = await _contentAccess.HasActiveSubscriptionAsync(userId);
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
                              PaidOrderStatus.All.Contains(oi.Order.Status.ToLower()));
                if (!paid)
                    return BadRequest(new { error = "Veuillez acheter cette formation avant de vous inscrire" });
                accessType = "purchase";
            }

            if (existingActive == false)
            {
                // Index unique (UserId, CourseId) : on réactive la ligne
                // existante, progression conservée, avec le nouveau motif
                // d'accès. Mise à jour ciblée, sans matérialiser l'entité.
                await _db.CourseEnrollments
                    .Where(e => e.UserId == userId && e.CourseId == id && !e.IsActive)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.IsActive, true)
                        .SetProperty(e => e.AccessType, accessType));

                _logger.LogInformation("User {UserId} re-enrolled in course {CourseId} ({AccessType})",
                    userId, id, accessType);

                return Ok(new { message = "Inscription réactivée", accessType });
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
