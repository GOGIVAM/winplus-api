using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>Certificats de complétion de formation (Module 5, 5C).</summary>
[ApiController]
public class CourseCertificateController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICourseCertificateService _certificates;
    private readonly ILogger<CourseCertificateController> _logger;

    public CourseCertificateController(ApplicationDbContext db, ICourseCertificateService certificates, ILogger<CourseCertificateController> logger)
    {
        _db = db;
        _certificates = certificates;
        _logger = logger;
    }

    /// <summary>Vérification publique d'un certificat par son code (US-3C) — aucune authentification requise.</summary>
    [HttpGet("api/certificats/{code}")]
    [AllowAnonymous]
    public async Task<IActionResult> Verify(string code)
    {
        var result = await _certificates.VerifyAsync(code);
        if (!result.IsValid) return NotFound(new { isValid = false });

        return Ok(new
        {
            isValid = true,
            studentName = result.StudentName,
            courseTitle = result.CourseTitle,
            grade = result.Grade,
            issuedAt = result.IssuedAt,
        });
    }

    /// <summary>Mon certificat pour cette formation (téléchargement PDF).</summary>
    [HttpGet("api/courses/{courseId:int}/certificate/me")]
    [Authorize]
    public async Task<IActionResult> GetMine(int courseId)
    {
        var userId = User.GetUserId();
        var cert = await _certificates.GetMineAsync(userId, courseId);
        if (cert == null) return NotFound(new { error = "Aucun certificat pour l'instant — termine la formation à 100%." });

        return Ok(new
        {
            cert.Id,
            cert.VerificationCode,
            cert.Grade,
            cert.FileUrl,
            cert.IssuedAt,
        });
    }

    /// <summary>
    /// Filet de sécurité : régénère le certificat à la demande si l'élève est
    /// à 100% mais que la génération automatique (déclenchée par la dernière
    /// leçon complétée) a échoué ou n'a pas encore eu lieu.
    /// </summary>
    [HttpPost("api/courses/{courseId:int}/certificate/generate")]
    [Authorize]
    public async Task<IActionResult> Generate(int courseId)
    {
        try
        {
            var userId = User.GetUserId();
            var cert = await _certificates.GenerateForCompletedCourseAsync(userId, courseId);
            return Ok(new { cert.Id, cert.VerificationCode, cert.Grade, cert.FileUrl, cert.IssuedAt });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating certificate for course {CourseId}", courseId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
