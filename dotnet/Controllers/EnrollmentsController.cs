using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Services;
using Backend.Extensions;
using Backend.Models.Entities;

namespace Backend.Controllers;

[ApiController]
[Route("api/enrollments")]
public class EnrollmentsController : ControllerBase
{
    private readonly IEnrollmentService _enrollmentService;
    private readonly ILogger<EnrollmentsController> _logger;

    public EnrollmentsController(IEnrollmentService enrollmentService, ILogger<EnrollmentsController> logger)
    {
        _enrollmentService = enrollmentService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Enroll([FromBody] Enrollment enrollment)
    {
        try
        {
            var result = await _enrollmentService.EnrollUserAsync(enrollment.UserId, enrollment.SubjectId);
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
            
            var enrollment = await _enrollmentService.GetEnrollmentAsync(userId, enrollmentId);
            
            if (enrollment == null)
            {
                return NotFound(new { error = "Enrollment not found" });
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
