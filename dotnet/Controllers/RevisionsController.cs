using Amazon.S3;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;
using Backend.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// API pour gérer les Révisions/Guides d'Étude
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RevisionsController : ControllerBase
{
    private readonly IRevisionService _revisionService;
    private readonly ApplicationDbContext _context;
    private readonly IStorageService _storage;
    private readonly ILogger<RevisionsController> _logger;

    public RevisionsController(IRevisionService revisionService, ApplicationDbContext context, IStorageService storage, ILogger<RevisionsController> logger)
    {
        _revisionService = revisionService;
        _context = context;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Récupère toutes les révisions avec pagination
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<RevisionDto>), 200)]
    public async Task<ActionResult<IEnumerable<RevisionDto>>> GetAllRevisions([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var revisions = await _revisionService.GetAllRevisionsAsync(page, pageSize);
        return Ok(revisions);
    }

    /// <summary>
    /// Récupère une révision par son ID
    /// </summary>
    [HttpGet("{id}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RevisionDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<RevisionDto>> GetRevisionById(int id)
    {
        var revision = await _revisionService.GetRevisionByIdAsync(id);
        if (revision == null)
            return NotFound(new { message = "Revision not found" });

        return Ok(revision);
    }

    /// <summary>
    /// Récupère les révisions filtrées
    /// </summary>
    [HttpPost("filter")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<RevisionDto>), 200)]
    public async Task<ActionResult<IEnumerable<RevisionDto>>> SearchRevisions([FromBody] RevisionSearchFilterDto filter)
    {
        var revisions = await _revisionService.GetRevisionsAsync(filter);
        return Ok(revisions);
    }

    /// <summary>
    /// Récupère les révisions par sujet
    /// </summary>
    [HttpGet("by-subject/{subject}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<RevisionDto>), 200)]
    public async Task<ActionResult<IEnumerable<RevisionDto>>> GetRevisionsBySubject(string subject, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var revisions = await _revisionService.GetRevisionsBySubjectAsync(subject, page, pageSize);
        return Ok(revisions);
    }

    /// <summary>
    /// Récupère les révisions assignées à l'utilisateur courant
    /// </summary>
    [HttpGet("me/assigned")]
    [ProducesResponseType(typeof(IEnumerable<RevisionDto>), 200)]
    public async Task<ActionResult<IEnumerable<RevisionDto>>> GetMyAssignedRevisions([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = GetUserId();
        if (userId == 0)
            return Unauthorized(new { message = "User not authenticated" });

        var revisions = await _revisionService.GetAssignedRevisionsAsync(userId, page, pageSize);
        return Ok(revisions);
    }

    /// <summary>
    /// Recherche des révisions
    /// </summary>
    [HttpGet("search")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<RevisionDto>), 200)]
    public async Task<ActionResult<IEnumerable<RevisionDto>>> SearchRevisions([FromQuery] string q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var revisions = await _revisionService.SearchRevisionsAsync(q, page, pageSize);
        return Ok(revisions);
    }

    /// <summary>
    /// Récupère les révisions publiées
    /// </summary>
    [HttpGet("published")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<RevisionDto>), 200)]
    public async Task<ActionResult<IEnumerable<RevisionDto>>> GetPublishedRevisions([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var revisions = await _revisionService.GetPublishedRevisionsAsync(page, pageSize);
        return Ok(revisions);
    }

    /// <summary>
    /// Démarre une révision pour l'utilisateur courant
    /// </summary>
    [HttpPost("{revisionId}/start")]
    [ProducesResponseType(typeof(RevisionEnrollmentDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<RevisionEnrollmentDto>> StartRevision(int revisionId, [FromBody] StartRevisionRequestDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var userId = GetUserId();
            if (userId == 0)
                return Unauthorized(new { message = "User not authenticated" });

            var enrollment = await _revisionService.StartRevisionAsync(revisionId, userId, request);
            return Ok(enrollment);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Revision not found" });
        }
    }

    /// <summary>
    /// Complète une révision pour l'utilisateur courant
    /// </summary>
    [HttpPost("{revisionId}/complete")]
    [ProducesResponseType(typeof(RevisionEnrollmentDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<RevisionEnrollmentDto>> CompleteRevision(int revisionId, [FromBody] CompleteRevisionRequestDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var userId = GetUserId();
            if (userId == 0)
                return Unauthorized(new { message = "User not authenticated" });

            var enrollment = await _revisionService.CompleteRevisionAsync(revisionId, userId, request);
            return Ok(enrollment);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Revision not found" });
        }
    }

    /// <summary>
    /// Récupère la progression de l'utilisateur courant dans une révision
    /// </summary>
    [HttpGet("{revisionId}/progress")]
    [ProducesResponseType(typeof(RevisionProgressResponseDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<RevisionProgressResponseDto>> GetRevisionProgress(int revisionId)
    {
        var userId = GetUserId();
        if (userId == 0)
            return Unauthorized(new { message = "User not authenticated" });

        var progress = await _revisionService.GetRevisionProgressAsync(revisionId, userId);
        if (progress == null)
            return NotFound(new { message = "Revision or enrollment not found" });

        return Ok(progress);
    }

    /// <summary>
    /// Crée une nouvelle révision (Admin only)
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ProducesResponseType(typeof(RevisionDto), 201)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<RevisionDto>> CreateRevision([FromBody] CreateRevisionRequestDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var revision = await _revisionService.CreateRevisionAsync(request);
        return CreatedAtAction(nameof(GetRevisionById), new { id = revision.Id }, revision);
    }

    /// <summary>
    /// Met à jour une révision (Admin only)
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(RevisionDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<RevisionDto>> UpdateRevision(int id, [FromBody] UpdateRevisionRequestDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var revision = await _revisionService.UpdateRevisionAsync(id, request);
            return Ok(revision);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Revision not found" });
        }
    }

    /// <summary>
    /// Publie une révision (Admin only)
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/publish")]
    [ProducesResponseType(typeof(RevisionDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<RevisionDto>> PublishRevision(int id)
    {
        try
        {
            var revision = await _revisionService.PublishRevisionAsync(id);
            return Ok(revision);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Revision not found" });
        }
    }

    /// <summary>
    /// Dépublie une révision (Admin only)
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/unpublish")]
    [ProducesResponseType(typeof(RevisionDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<RevisionDto>> UnpublishRevision(int id)
    {
        try
        {
            var revision = await _revisionService.UnpublishRevisionAsync(id);
            return Ok(revision);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Revision not found" });
        }
    }

    /// <summary>
    /// Supprime une révision (Admin only)
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteRevision(int id)
    {
        var result = await _revisionService.DeleteRevisionAsync(id);
        if (!result)
            return NotFound(new { message = "Revision not found" });

        return NoContent();
    }

    /// <summary>
    /// Récupère les statistiques d'une révision
    /// </summary>
    [HttpGet("{id}/stats")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<object>> GetRevisionStats(int id)
    {
        try
        {
            var stats = await _revisionService.GetRevisionStatsAsync(id);
            return Ok(stats);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Revision not found" });
        }
    }

    /// <summary>
    /// Assigne automatiquement des révisions basées sur les scores de l'utilisateur courant
    /// </summary>
    [HttpPost("me/auto-assign")]
    [ProducesResponseType(typeof(IEnumerable<RevisionEnrollmentDto>), 200)]
    public async Task<ActionResult<IEnumerable<RevisionEnrollmentDto>>> AutoAssignRevisions()
    {
        var userId = GetUserId();
        if (userId == 0)
            return Unauthorized(new { message = "User not authenticated" });

        var enrollments = await _revisionService.AssignRevisionsBasedOnScoresAsync(userId);
        return Ok(enrollments);
    }

    /// <summary>
    /// Génère une fiche de révision personnalisée par IA pour l'utilisateur
    /// courant (erreurs de quiz récentes, épreuves téléchargées, objectifs
    /// actifs) et l'y inscrit aussitôt.
    /// </summary>
    [HttpPost("me/generate")]
    [ProducesResponseType(typeof(RevisionDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(503)]
    public async Task<ActionResult<RevisionDto>> GenerateRevision([FromBody] GenerateRevisionRequestDto? request)
    {
        var userId = GetUserId();
        if (userId == 0)
            return Unauthorized(new { message = "User not authenticated" });

        try
        {
            var revision = await _revisionService.GenerateAIRevisionAsync(userId, request?.Subject, request?.Topic);
            return Ok(revision);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Diffuse le document (PDF) d'une fiche de révision en ligne, comme
    /// SubjectsController.ViewStream pour une épreuve  jamais de lien S3 direct
    /// (privé depuis le retrait des ACL publiques), toujours via ce proxy
    /// authentifié.
    /// </summary>
    [HttpGet("{id}/document")]
    public async Task<IActionResult> ViewDocument(int id)
    {
        var revision = await _context.Revisions.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);

        if (revision == null || string.IsNullOrWhiteSpace(revision.DocumentUrl))
            return NotFound(new { error = "Aucun document pour cette fiche." });

        if (!revision.IsPublished)
            return Forbid();

        try
        {
            var bucket = _storage.Bucket;
            var s3Key = ExtractS3Key(revision.DocumentUrl, bucket);

            using var s3 = _storage.CreateS3Client();
            using var obj = await s3.GetObjectAsync(bucket, s3Key);

            var buffer = new MemoryStream();
            await obj.ResponseStream.CopyToAsync(buffer);
            buffer.Position = 0;

            return File(buffer, "application/pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du streaming du document de la fiche {RevisionId}", id);
            return StatusCode(500, new { error = "Impossible de charger le document pour le moment." });
        }
    }

    private static string ExtractS3Key(string documentUrl, string bucket)
    {
        if (documentUrl.StartsWith("s3://") || documentUrl.StartsWith("http"))
            return new Uri(documentUrl).AbsolutePath.TrimStart('/');
        return documentUrl;
    }

    private int GetUserId()
    {
        // ⚠ Correctif : l'ancienne version lisait FindFirst("sub"), or ASP.NET Core
        // remappe "sub" vers ClaimTypes.NameIdentifier à la validation du JWT.
        // Le claim était donc toujours introuvable → 0 → 401, y compris avec un
        // token parfaitement valide (d'où les boucles de refresh inutiles côté
        // frontend). L'extension partagée essaie user_id, NameIdentifier puis sub.
        try
        {
            return User.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
