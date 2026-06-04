using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Backend.Models.DTOs;
using Backend.Services;
using Backend.Extensions;

namespace Backend.Controllers;

[ApiController]
[Route("api/forums")]
[Produces("application/json")]
[Authorize]
public class ForumController : ControllerBase
{
    private readonly IForumService _forumService;
    private readonly INtfyService _ntfyService;
    private readonly ILogger<ForumController> _logger;

    public ForumController(IForumService forumService, INtfyService ntfyService, ILogger<ForumController> logger)
    {
        _forumService = forumService;
        _ntfyService = ntfyService;
        _logger = logger;
    }

    /// <summary>
    /// Liste les threads du forum avec filtrage optionnel par catégorie
    /// </summary>
    [HttpGet("threads")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ForumThreadListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetThreads(
        [FromQuery] string? category,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var result = await _forumService.GetThreadsAsync(category, page, pageSize);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting forum threads");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Crée un nouveau thread
    /// </summary>
    [HttpPost("threads")]
    [ProducesResponseType(typeof(ForumThreadResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateThread([FromBody] CreateThreadRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var userId = User.GetUserId();
            var thread = await _forumService.CreateThreadAsync(userId, request);
            return StatusCode(201, thread);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating forum thread");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les posts d'un thread et incrémente le compteur de vues
    /// </summary>
    [HttpGet("threads/{id}/posts")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ForumPostListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPosts(int id)
    {
        try
        {
            var result = await _forumService.GetPostsAsync(id);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Thread not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting posts for thread {ThreadId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Crée un post dans un thread et notifie l'auteur du thread via Ntfy
    /// </summary>
    [HttpPost("threads/{id}/posts")]
    [ProducesResponseType(typeof(ForumPostResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreatePost(int id, [FromBody] CreatePostRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var userId = User.GetUserId();
            var post = await _forumService.CreatePostAsync(id, userId, request);

            var threadAuthorId = await _forumService.GetThreadAuthorIdAsync(id);
            if (threadAuthorId.HasValue && threadAuthorId.Value != userId)
            {
                _ = _ntfyService.PublishAsync(
                    topic: $"winplus-user-{threadAuthorId.Value}",
                    title: "Nouvelle réponse sur votre thread",
                    message: "Une nouvelle réponse a été ajoutée à votre thread.",
                    priority: "default",
                    tags: new[] { "speech_balloon" },
                    userId: threadAuthorId.Value,
                    type: "forum");
            }

            return StatusCode(201, post);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Thread not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating post for thread {ThreadId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Vote sur un post (up/down) — HTTP 409 si déjà voté
    /// </summary>
    [HttpPost("posts/{id}/vote")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> VoteOnPost(int id, [FromBody] VoteRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var userId = User.GetUserId();
            await _forumService.VoteOnPostAsync(id, userId, request.Type);
            return Ok(new { success = true });
        }
        catch (ForumConflictException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Post not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error voting on post {PostId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Marque un post comme réponse acceptée — réservé à l'auteur du thread
    /// </summary>
    [HttpPost("posts/{id}/accept")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcceptPost(int id)
    {
        try
        {
            var userId = User.GetUserId();
            await _forumService.AcceptPostAsync(id, userId);
            return Ok(new { success = true });
        }
        catch (ForumForbiddenException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Post not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting post {PostId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Soft-delete un thread — réservé à l'auteur ou aux admins
    /// </summary>
    [HttpDelete("threads/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteThread(int id)
    {
        try
        {
            var userId = User.GetUserId();
            var userRole = User.GetUserRole();
            await _forumService.DeleteThreadAsync(id, userId, userRole);
            return Ok(new { success = true });
        }
        catch (ForumForbiddenException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Thread not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting forum thread {ThreadId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
