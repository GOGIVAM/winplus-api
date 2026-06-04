using Backend.Models.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

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

    public RevisionsController(IRevisionService revisionService)
    {
        _revisionService = revisionService;
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

    private int GetUserId()
    {
        // Extraire l'ID utilisateur depuis le token JWT ou les claims
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst("userid")?.Value;
        if (int.TryParse(userIdClaim, out int userId))
            return userId;
        return 0;
    }
}
