using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;

namespace Backend.Controllers;

/// <summary>
/// Tri et suivi des discussions du forum.
///
/// Contrôleur autonome : il n'touche pas à ForumsController.
/// Routes ajoutées :
///   GET    /api/forums/threads/feed?category=&amp;q=&amp;page=&amp;pageSize=&amp;sort=&amp;followed=
///   GET    /api/forums/follows
///   POST   /api/forums/threads/{id}/follow
///   DELETE /api/forums/threads/{id}/follow
///
/// ⚠️ Si tes entités forum portent d'autres noms que ForumThreads /
/// ForumPosts, seules les requêtes de ce fichier sont à renommer.
/// </summary>
[ApiController]
[Route("api/forums")]
public class ForumFeedController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ForumFeedController> _logger;

    public ForumFeedController(ApplicationDbContext db, ILogger<ForumFeedController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private int? CurrentUserIdOrNull()
    {
        try { return User?.Identity?.IsAuthenticated == true ? User.GetUserId() : null; }
        catch { return null; }
    }

    // ══ GET /api/forums/threads/feed ═════════════════════════════════
    [HttpGet("threads/feed")]
    [AllowAnonymous]
    public async Task<IActionResult> Feed(
        [FromQuery] string? category,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sort = "recent",
        [FromQuery] bool followed = false)
    {
        try
        {
            if (page < 1) page = 1;
            pageSize = Math.Clamp(pageSize, 1, 50);

            var userId = CurrentUserIdOrNull();

            if (followed && userId == null)
                return Ok(new { threads = Array.Empty<object>(), total = 0, page, pageSize, totalPages = 0 });

            var query = _db.ForumThreads.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(category) && category != "all")
                query = query.Where(t => t.Category == category);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = $"%{q.Trim()}%";
                query = query.Where(t =>
                    EF.Functions.ILike(t.Title, term) ||
                    EF.Functions.ILike(t.Content, term) ||
                    (t.Tag != null && EF.Functions.ILike(t.Tag, term)));
            }

            if (followed && userId != null)
            {
                var followedIds = _db.ForumThreadFollows
                    .Where(f => f.UserId == userId)
                    .Select(f => f.ThreadId);
                query = query.Where(t => followedIds.Contains(t.Id));
            }

            // Tri : les fils épinglés restent en tête dans tous les cas.
            query = sort switch
            {
                "active" => query
                    .OrderByDescending(t => t.IsPinned)
                    .ThenByDescending(t => t.RepliesCount)
                    .ThenByDescending(t => t.UpdatedAt),

                "unanswered" => query
                    .Where(t => t.RepliesCount == 0)
                    .OrderByDescending(t => t.IsPinned)
                    .ThenByDescending(t => t.CreatedAt),

                "popular" => query
                    .OrderByDescending(t => t.IsPinned)
                    .ThenByDescending(t => t.Upvotes)
                    .ThenByDescending(t => t.ViewsCount),

                "oldest" => query
                    .OrderByDescending(t => t.IsPinned)
                    .ThenBy(t => t.CreatedAt),

                _ => query
                    .OrderByDescending(t => t.IsPinned)
                    .ThenByDescending(t => t.CreatedAt),
            };

            var total = await query.CountAsync();

            var rows = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new
                {
                    t.Id,
                    t.UserId,
                    authorName = t.User != null
                        ? (t.User.FirstName + " " + t.User.LastName).Trim()
                        : null,
                    authorRole = t.User != null ? t.User.Role : null,
                    t.Title,
                    t.Content,
                    t.Category,
                    t.Tag,
                    t.IsPinned,
                    t.IsSolved,
                    t.ViewsCount,
                    t.RepliesCount,
                    t.Upvotes,
                    t.CreatedAt,
                    t.UpdatedAt,
                })
                .ToListAsync();

            // Marquage « suivi » pour l'utilisateur courant
            var followSet = new HashSet<int>();
            if (userId != null && rows.Count > 0)
            {
                var ids = rows.Select(r => r.Id).ToList();
                followSet = (await _db.ForumThreadFollows
                        .Where(f => f.UserId == userId && ids.Contains(f.ThreadId))
                        .Select(f => f.ThreadId)
                        .ToListAsync())
                    .ToHashSet();
            }

            var threads = rows.Select(r => new
            {
                r.Id, r.UserId, r.authorName, r.authorRole,
                r.Title, r.Content, r.Category, r.Tag,
                r.IsPinned, r.IsSolved, r.ViewsCount, r.RepliesCount, r.Upvotes,
                r.CreatedAt, r.UpdatedAt,
                isFollowed = followSet.Contains(r.Id),
            });

            return Ok(new
            {
                threads,
                total,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(total / (double)pageSize),
                sort,
                followed,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading forum feed");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ══ GET /api/forums/follows ══════════════════════════════════════
    [HttpGet("follows")]
    [Authorize]
    public async Task<IActionResult> MyFollows()
    {
        try
        {
            var userId = User.GetUserId();
            var ids = await _db.ForumThreadFollows
                .Where(f => f.UserId == userId)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => f.ThreadId)
                .ToListAsync();

            return Ok(new { threadIds = ids });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading follows");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ══ POST /api/forums/threads/{id}/follow ═════════════════════════
    [HttpPost("threads/{id:int}/follow")]
    [Authorize]
    public async Task<IActionResult> Follow(int id)
    {
        try
        {
            var userId = User.GetUserId();

            var exists = await _db.ForumThreads.AnyAsync(t => t.Id == id);
            if (!exists) return NotFound(new { error = "Thread not found" });

            var already = await _db.ForumThreadFollows
                .AnyAsync(f => f.UserId == userId && f.ThreadId == id);

            if (!already)
            {
                _db.ForumThreadFollows.Add(new ForumThreadFollow
                {
                    UserId = userId,
                    ThreadId = id,
                    CreatedAt = DateTime.UtcNow,
                });
                await _db.SaveChangesAsync();
            }

            return Ok(new { threadId = id, isFollowed = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error following thread {ThreadId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ══ DELETE /api/forums/threads/{id}/follow ═══════════════════════
    [HttpDelete("threads/{id:int}/follow")]
    [Authorize]
    public async Task<IActionResult> Unfollow(int id)
    {
        try
        {
            var userId = User.GetUserId();

            var row = await _db.ForumThreadFollows
                .FirstOrDefaultAsync(f => f.UserId == userId && f.ThreadId == id);

            if (row != null)
            {
                _db.ForumThreadFollows.Remove(row);
                await _db.SaveChangesAsync();
            }

            return Ok(new { threadId = id, isFollowed = false });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unfollowing thread {ThreadId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
