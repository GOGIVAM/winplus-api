using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;

namespace Backend.Services;

public class ForumConflictException : Exception
{
    public ForumConflictException(string message) : base(message) { }
}

public class ForumForbiddenException : Exception
{
    public ForumForbiddenException(string message) : base(message) { }
}

public interface IForumService
{
    Task<ForumThreadListResponse> GetThreadsAsync(string? category, int page, int pageSize);
    Task<ForumThreadResponse> CreateThreadAsync(int userId, CreateThreadRequest request);
    Task<ForumPostListResponse> GetPostsAsync(int threadId);
    Task<ForumPostResponse> CreatePostAsync(int threadId, int userId, CreatePostRequest request);
    Task VoteOnPostAsync(int postId, int userId, string type);
    Task AcceptPostAsync(int postId, int requestingUserId);
    Task DeleteThreadAsync(int threadId, int requestingUserId, string userRole);
    Task<int?> GetThreadAuthorIdAsync(int threadId);
}

public class ForumService : IForumService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ForumService> _logger;

    public ForumService(ApplicationDbContext db, ILogger<ForumService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ForumThreadListResponse> GetThreadsAsync(string? category, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query = _db.ForumThreads.Where(t => !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(category) && category != "all")
            query = query.Where(t => t.Category == category);

        var total = await query.CountAsync();

        var threads = await query
            .Include(t => t.User)
            .OrderByDescending(t => t.IsPinned)
            .ThenByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new ForumThreadResponse
            {
                Id = t.Id,
                UserId = t.UserId,
                AuthorName = t.User != null
                    ? (t.User.FirstName + " " + t.User.LastName).Trim()
                    : null,
                Title = t.Title,
                Content = t.Content,
                Category = t.Category,
                Tag = t.Tag,
                IsPinned = t.IsPinned,
                IsSolved = t.IsSolved,
                ViewsCount = t.ViewsCount,
                RepliesCount = t.RepliesCount,
                Upvotes = t.Upvotes,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt
            })
            .ToListAsync();

        return new ForumThreadListResponse
        {
            Threads = threads,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<ForumThreadResponse> CreateThreadAsync(int userId, CreateThreadRequest request)
    {
        var thread = new ForumThread
        {
            UserId = userId,
            Title = request.Title,
            Content = request.Content,
            Category = request.Category,
            Tag = request.Tag,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.ForumThreads.Add(thread);
        await _db.SaveChangesAsync();

        await _db.Entry(thread).Reference(t => t.User).LoadAsync();

        return new ForumThreadResponse
        {
            Id = thread.Id,
            UserId = thread.UserId,
            AuthorName = thread.User != null
                ? (thread.User.FirstName + " " + thread.User.LastName).Trim()
                : null,
            Title = thread.Title,
            Content = thread.Content,
            Category = thread.Category,
            Tag = thread.Tag,
            IsPinned = thread.IsPinned,
            IsSolved = thread.IsSolved,
            ViewsCount = thread.ViewsCount,
            RepliesCount = thread.RepliesCount,
            Upvotes = thread.Upvotes,
            CreatedAt = thread.CreatedAt,
            UpdatedAt = thread.UpdatedAt
        };
    }

    public async Task<ForumPostListResponse> GetPostsAsync(int threadId)
    {
        var thread = await _db.ForumThreads.FindAsync(threadId);
        if (thread != null && !thread.IsDeleted)
        {
            thread.ViewsCount += 1;
            await _db.SaveChangesAsync();
        }

        var posts = await _db.ForumPosts
            .Where(p => p.ThreadId == threadId && !p.IsDeleted)
            .Include(p => p.User)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new ForumPostResponse
            {
                Id = p.Id,
                ThreadId = p.ThreadId,
                UserId = p.UserId,
                AuthorName = p.User != null
                    ? (p.User.FirstName + " " + p.User.LastName).Trim()
                    : null,
                Content = p.Content,
                Upvotes = p.Upvotes,
                IsAccepted = p.IsAccepted,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .ToListAsync();

        return new ForumPostListResponse { Posts = posts, Total = posts.Count };
    }

    public async Task<ForumPostResponse> CreatePostAsync(int threadId, int userId, CreatePostRequest request)
    {
        var thread = await _db.ForumThreads.FindAsync(threadId);
        if (thread == null || thread.IsDeleted)
            throw new KeyNotFoundException($"Thread {threadId} not found");

        var post = new ForumPost
        {
            ThreadId = threadId,
            UserId = userId,
            Content = request.Content,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.ForumPosts.Add(post);
        thread.RepliesCount += 1;
        thread.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _db.Entry(post).Reference(p => p.User).LoadAsync();

        return new ForumPostResponse
        {
            Id = post.Id,
            ThreadId = post.ThreadId,
            UserId = post.UserId,
            AuthorName = post.User != null
                ? (post.User.FirstName + " " + post.User.LastName).Trim()
                : null,
            Content = post.Content,
            Upvotes = post.Upvotes,
            IsAccepted = post.IsAccepted,
            CreatedAt = post.CreatedAt,
            UpdatedAt = post.UpdatedAt
        };
    }

    public async Task VoteOnPostAsync(int postId, int userId, string type)
    {
        var alreadyVoted = await _db.ForumVotes
            .AnyAsync(v => v.PostId == postId && v.UserId == userId);

        if (alreadyVoted)
            throw new ForumConflictException("Vous avez déjà voté sur ce post");

        var post = await _db.ForumPosts.FindAsync(postId);
        if (post == null || post.IsDeleted)
            throw new KeyNotFoundException($"Post {postId} not found");

        _db.ForumVotes.Add(new ForumVote
        {
            PostId = postId,
            UserId = userId,
            Type = type,
            CreatedAt = DateTime.UtcNow
        });

        if (type == "up")
            post.Upvotes += 1;
        else if (type == "down" && post.Upvotes > 0)
            post.Upvotes -= 1;

        await _db.SaveChangesAsync();
    }

    public async Task AcceptPostAsync(int postId, int requestingUserId)
    {
        var post = await _db.ForumPosts
            .Include(p => p.Thread)
            .FirstOrDefaultAsync(p => p.Id == postId && !p.IsDeleted);

        if (post == null)
            throw new KeyNotFoundException($"Post {postId} not found");

        if (post.Thread == null || post.Thread.UserId != requestingUserId)
            throw new ForumForbiddenException("Seul l'auteur du thread peut accepter une réponse");

        post.IsAccepted = true;
        post.UpdatedAt = DateTime.UtcNow;

        if (post.Thread != null)
        {
            post.Thread.IsSolved = true;
            post.Thread.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    public async Task DeleteThreadAsync(int threadId, int requestingUserId, string userRole)
    {
        var thread = await _db.ForumThreads.FindAsync(threadId);
        if (thread == null || thread.IsDeleted)
            throw new KeyNotFoundException($"Thread {threadId} not found");

        if (thread.UserId != requestingUserId && !userRole.Equals("admin", StringComparison.OrdinalIgnoreCase))
            throw new ForumForbiddenException("Vous n'êtes pas autorisé à supprimer ce thread");

        thread.IsDeleted = true;
        thread.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<int?> GetThreadAuthorIdAsync(int threadId)
    {
        var thread = await _db.ForumThreads
            .Where(t => t.Id == threadId && !t.IsDeleted)
            .Select(t => (int?)t.UserId)
            .FirstOrDefaultAsync();
        return thread;
    }
}
