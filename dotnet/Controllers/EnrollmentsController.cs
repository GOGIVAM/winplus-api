using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Services;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Models.Entities;

namespace Backend.Controllers;

[ApiController]
[Route("api/enrollments")]
// Module 17 : ce contrôleur était entièrement ouvert. POST /api/enrollments
// acceptait un UserId et un SubjectId arbitraires sans vérifier ni
// l'authentification ni le paiement, et GET /user/{userId} exposait
// anonymement les inscriptions de n'importe qui.
[Authorize]
public class EnrollmentsController : ControllerBase
{
    private readonly IEnrollmentService _enrollmentService;
    private readonly ISubjectService _subjectService;
    private readonly IContentAccessService _contentAccess;
    private readonly ILogger<EnrollmentsController> _logger;

    public EnrollmentsController(
        IEnrollmentService enrollmentService,
        ISubjectService subjectService,
        IContentAccessService contentAccess,
        ILogger<EnrollmentsController> logger)
    {
        _enrollmentService = enrollmentService;
        _subjectService = subjectService;
        _contentAccess = contentAccess;
        _logger = logger;
    }

    /// <summary>
    /// Un utilisateur ne consulte que ses propres inscriptions ;
    /// l'administrateur conserve la vue complète.
    /// </summary>
    private bool CanActOnBehalfOf(int userId) => userId == User.GetUserId() || User.IsAdmin();

    /// <summary>
    /// Inscription à un contenu (Module 17).
    ///
    /// L'identifiant d'utilisateur n'est plus accepté depuis le corps de la
    /// requête : il est déduit du jeton. Une inscription à un contenu payant
    /// doit découler d'un droit d'accès réel (achat confirmé, abonnement en
    /// cours, assignation via une classe) et non d'un simple appel : c'est
    /// exactement la règle appliquée à la consultation et au téléchargement,
    /// portée par le même service.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Enroll([FromBody] EnrollRequest request)
    {
        try
        {
            var userId = User.GetUserId();

            var subject = await _subjectService.GetSubjectByIdAsync(request.SubjectId);
            if (subject == null)
                return NotFound(new { error = "Contenu introuvable." });

            // Un brouillon ne s'inscrit pas : seul son auteur ou un
            // administrateur peut y toucher avant publication.
            if (!subject.IsPublished && !User.IsAdmin() && subject.AuthorUserId != userId)
                return NotFound(new { error = "Contenu introuvable." });

            // Un contenu gratuit reste librement accessible : le nouveau test
            // ne doit pas le bloquer par excès de zèle.
            if (subject.Price > 0 &&
                !await _contentAccess.HasPaidContentAccessAsync(userId, subject, User.IsAdmin()))
            {
                return StatusCode(403, new
                {
                    error = "Veuillez acheter ce contenu pour pouvoir vous y inscrire."
                });
            }

            var result = await _enrollmentService.EnrollUserAsync(userId, request.SubjectId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'inscription");
            return StatusCode(500, "Erreur serveur");
        }
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserEnrollments(int userId)
    {
        try
        {
            if (!CanActOnBehalfOf(userId))
                return StatusCode(403, new { error = "Accès refusé." });

            var enrollments = await _enrollmentService.GetUserEnrollmentsAsync(userId);
            return Ok(enrollments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des inscriptions utilisateur");
            return StatusCode(500, "Erreur serveur");
        }
    }

    [HttpGet("{userId}/{subjectId}")]
    public async Task<IActionResult> GetEnrollment(int userId, int subjectId)
    {
        try
        {
            if (!CanActOnBehalfOf(userId))
                return StatusCode(403, new { error = "Accès refusé." });

            var enrollment = await _enrollmentService.GetEnrollmentAsync(userId, subjectId);
            if (enrollment == null)
                return NotFound();
            return Ok(enrollment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération de l'inscription");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Get progress for an enrollment
    /// </summary>
    [HttpGet("{enrollmentId}/progress")]
    [Authorize]
    public async Task<IActionResult> GetProgress(int enrollmentId)
    {
        try
        {
            var userId = User.GetUserId();
            var progress = await _enrollmentService.GetProgressAsync(enrollmentId, userId);

            return Ok(new
            {
                success = true,
                data = progress,
                timestamp = DateTime.UtcNow
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting progress for enrollment {EnrollmentId}", enrollmentId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Unenroll user from a course
    /// </summary>
    [HttpDelete("{enrollmentId}")]
    [Authorize]
    public async Task<IActionResult> Unenroll(int enrollmentId)
    {
        try
        {
            var userId = User.GetUserId();
            
            // Module 20 : l'appel passait `enrollmentId` là où la signature
            // attend un `subjectId`, si bien que le contrôle de propriété
            // portait sur la mauvaise ligne (ou aucune). On lit désormais
            // l'inscription par son identifiant propre, et on vérifie
            // explicitement qu'elle appartient bien à l'appelant.
            var enrollment = await _enrollmentService.GetEnrollmentByIdAsync(enrollmentId);

            if (enrollment == null)
            {
                return NotFound(new { error = "Enrollment not found" });
            }

            if (enrollment.UserId != userId && !User.IsAdmin())
            {
                return StatusCode(403, new { error = "Accès refusé." });
            }

            var result = await _enrollmentService.UnenrollAsync(enrollmentId);
            
            if (!result)
            {
                return BadRequest(new 
                { 
                    error = "Cannot unenroll: the 7-day unenroll window has passed",
                    enrolledAt = enrollment.EnrolledAt,
                    currentTime = DateTime.UtcNow
                });
            }

            _logger.LogInformation("User {UserId} unenrolled from enrollment {EnrollmentId}", userId, enrollmentId);

            return Ok(new
            {
                success = true,
                message = "Successfully unenrolled from course",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unenrolling from enrollment {EnrollmentId}", enrollmentId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
