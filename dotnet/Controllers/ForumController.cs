using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Backend.Controllers;

/// <summary>
/// Contrôleur API pour le forum communautaire WinPlus
/// </summary>
[ApiController]
[Route("api/forums")]
[Produces("application/json")]
public class ForumController : ControllerBase
{
    private readonly ILogger<ForumController> _logger;

    public ForumController(ILogger<ForumController> logger)
    {
        _logger = logger;
    }

    private int? GetCurrentUserId()
    {
        var claim = User.FindFirst("userId") ?? User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null && int.TryParse(claim.Value, out var id) ? id : null;
    }

    /// <summary>
    /// GET /api/forums/threads?category=&amp;page=&amp;pageSize=
    /// Liste des fils de discussion, filtrés par catégorie
    /// </summary>
    [HttpGet("threads")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetThreads(
        [FromQuery] string? category,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        _logger.LogInformation("GetThreads category={Category} page={Page}", category, page);

        return Ok(new
        {
            items      = Array.Empty<object>(),
            totalCount = 0,
            page,
            pageSize,
            category
        });
    }

    /// <summary>
    /// POST /api/forums/threads
    /// Crée un nouveau fil de discussion
    /// </summary>
    [HttpPost("threads")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult CreateThread([FromBody] CreateForumThreadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { error = "Titre et contenu requis." });

        var userId = GetCurrentUserId();
        _logger.LogInformation("CreateThread userId={UserId} title={Title}", userId, request.Title);

        var created = new
        {
            id       = 0,
            title    = request.Title,
            content  = request.Content,
            category = request.Category ?? "Général",
            tag      = request.Tag,
            authorId = userId,
            createdAt = DateTime.UtcNow,
        };
        return CreatedAtAction(nameof(GetThreads), created);
    }

    /// <summary>
    /// GET /api/forums/threads/{id}/posts
    /// Messages d'un fil de discussion
    /// </summary>
    [HttpGet("threads/{id:int}/posts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetPosts(int id)
    {
        _logger.LogInformation("GetPosts threadId={ThreadId}", id);

        return Ok(new
        {
            threadId = id,
            items    = Array.Empty<object>(),
            total    = 0,
        });
    }

    /// <summary>
    /// POST /api/forums/threads/{id}/posts
    /// Ajoute un message à un fil de discussion
    /// </summary>
    [HttpPost("threads/{id:int}/posts")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult CreatePost(int id, [FromBody] CreateForumPostRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { error = "Contenu requis." });

        var userId = GetCurrentUserId();
        _logger.LogInformation("CreatePost threadId={ThreadId} userId={UserId}", id, userId);

        var created = new
        {
            id        = 0,
            threadId  = id,
            content   = request.Content,
            authorId  = userId,
            createdAt = DateTime.UtcNow,
            upvotes   = 0,
            isAccepted = false,
        };
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>
    /// POST /api/forums/posts/{id}/vote
    /// Vote pour un message (up / down)
    /// </summary>
    [HttpPost("posts/{id:int}/vote")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult VotePost(int id, [FromBody] ForumVoteRequest request)
    {
        if (request.Type != "up" && request.Type != "down")
            return BadRequest(new { error = "Type doit être 'up' ou 'down'." });

        var userId = GetCurrentUserId();
        _logger.LogInformation("VotePost postId={PostId} type={Type} userId={UserId}", id, request.Type, userId);

        return Ok(new { postId = id, type = request.Type, recorded = true });
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record CreateForumThreadRequest(
    string Title,
    string Content,
    string? Category,
    string? Tag
);

public record CreateForumPostRequest(string Content);

public record ForumVoteRequest(string Type);
