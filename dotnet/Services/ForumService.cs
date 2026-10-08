using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Utils;

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
    Task<ForumThreadResponse> GetThreadByIdAsync(int threadId);
    Task<ForumThreadResponse> CreateThreadAsync(int userId, CreateThreadRequest request);
    Task<ForumPostListResponse> GetPostsAsync(int threadId);
    Task<ForumPostResponse> CreatePostAsync(int threadId, int userId, CreatePostRequest request);
    Task VoteOnPostAsync(int postId, int userId, string type);
    Task AcceptPostAsync(int postId, int requestingUserId);
    Task DeleteThreadAsync(int threadId, int requestingUserId, string userRole);
    Task<int?> GetThreadAuthorIdAsync(int threadId);
    Task<List<int>> GetThreadFollowerIdsAsync(int threadId);

    // ── Module 42 : fils suivis (le service frontal les attendait déjà) ──
    /// <summary>Fils triés/filtrés pour le fil d'actualité du forum. `userId` null pour un visiteur anonyme (isFollowed non calculé, followedOnly ignoré).</summary>
    Task<ForumThreadListResponse> GetFeedAsync(int? userId, string? category, int page, int pageSize, string sort, bool followedOnly);
    Task<List<int>> GetFollowedThreadIdsAsync(int userId);
    Task FollowThreadAsync(int userId, int threadId);
    Task UnfollowThreadAsync(int userId, int threadId);
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
                AuthorRole = t.User != null ? t.User.Role : null,
                IsVerifiedInstitution = t.User != null && t.User.Role == "institution" && t.User.IsEmailVerified,
                IsVerifiedTeacher = t.User != null && t.User.Role == "teacher"
                    && _db.TutorProfiles.Any(tp => tp.UserId == t.UserId && tp.IsDiplomaVerified),
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

    public async Task<ForumThreadResponse> GetThreadByIdAsync(int threadId)
    {
        var t = await _db.ForumThreads
            .Include(t => t.User)
            .Where(t => t.Id == threadId && !t.IsDeleted)
            .Select(t => new ForumThreadResponse
            {
                Id = t.Id,
                UserId = t.UserId,
                AuthorName = t.User != null ? (t.User.FirstName + " " + t.User.LastName).Trim() : null,
                AuthorRole = t.User != null ? t.User.Role : null,
                IsVerifiedInstitution = t.User != null && t.User.Role == "institution" && t.User.IsEmailVerified,
                IsVerifiedTeacher = t.User != null && t.User.Role == "teacher"
                    && _db.TutorProfiles.Any(tp => tp.UserId == t.UserId && tp.IsDiplomaVerified),
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
            .FirstOrDefaultAsync();

        if (t == null) throw new KeyNotFoundException($"Thread {threadId} not found");
        return t;
    }

    public async Task<ForumThreadResponse> CreateThreadAsync(int userId, CreateThreadRequest request)
    {
        var thread = new ForumThread
        {
            UserId = userId,
            Title = request.Title,
            Content = ContentSanitizer.CensorPhoneNumbers(request.Content),
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
            .Where(p => p.ThreadId == threadId && !p.IsDeleted && !p.IsHidden)
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
                AuthorRole = p.User != null ? p.User.Role : null,
                IsVerifiedInstitution = p.User != null && p.User.Role == "institution" && p.User.IsEmailVerified,
                IsVerifiedTeacher = p.User != null && p.User.Role == "teacher"
                    && _db.TutorProfiles.Any(tp => tp.UserId == p.UserId && tp.IsDiplomaVerified),
                IsHidden = p.IsHidden,
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
            Content = ContentSanitizer.CensorPhoneNumbers(request.Content),
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

    private static int GetVoteWeight(string? role) => role switch
    {
        "teacher"     => 3,
        "institution" => 3,
        _             => 1,
    };

    public async Task VoteOnPostAsync(int postId, int userId, string type)
    {
        var alreadyVoted = await _db.ForumVotes
            .AnyAsync(v => v.PostId == postId && v.UserId == userId);

        if (alreadyVoted)
            throw new ForumConflictException("Vous avez déjà voté sur ce post");

        var post = await _db.ForumPosts.FindAsync(postId);
        if (post == null || post.IsDeleted)
            throw new KeyNotFoundException($"Post {postId} not found");

        // Récupérer le rôle du votant pour calculer le poids
        var voter = await _db.Users.FindAsync(userId);
        var weight = GetVoteWeight(voter?.Role);

        _db.ForumVotes.Add(new ForumVote
        {
            PostId = postId,
            UserId = userId,
            Type = type,
            Weight = weight,
            CreatedAt = DateTime.UtcNow
        });

        if (type == "up")
            post.Upvotes += weight;
        else if (type == "down")
            post.Upvotes = Math.Max(0, post.Upvotes - weight);

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

            // US-FOR-COM-01 : la réponse d'un professeur vérifié (diplôme)
            // marquée "Meilleure réponse" met automatiquement le fil en avant.
            var isVerifiedTeacher = await _db.Users.AsNoTracking()
                .Where(u => u.Id == post.UserId && u.Role == "teacher")
                .Select(u => _db.TutorProfiles.Any(tp => tp.UserId == u.Id && tp.IsDiplomaVerified))
                .FirstOrDefaultAsync();
            if (isVerifiedTeacher)
                post.Thread.IsPinned = true;
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

    /// <summary>Abonnés à un fil (US-FOR-COM-01 : notifiés à chaque nouvelle réponse, pas seulement l'auteur).</summary>
    public async Task<List<int>> GetThreadFollowerIdsAsync(int threadId) =>
        await _db.ForumThreadFollows
            .Where(f => f.ThreadId == threadId)
            .Select(f => f.UserId)
            .ToListAsync();

    // ── Module 42 : routes manquantes du service frontal du forum ──────────
    //
    // L'entité ForumThreadFollow et GetThreadFollowerIdsAsync existaient déjà
    // (consommés par la distribution de notification à chaque réponse), mais
    // aucune route ne permettait à un utilisateur de suivre/ne plus suivre un
    // fil lui-même, ni de lister ses fils suivis, ni de trier/filtrer le fil
    // d'actualité. Le service frontal (forumService.ts) les appelait déjà :
    // c'est du code mort côté client qu'on raccorde, conformément à la
    // décision produit (construire le backend plutôt que retirer le front).

    public async Task<List<int>> GetFollowedThreadIdsAsync(int userId) =>
        await _db.ForumThreadFollows
            .Where(f => f.UserId == userId)
            .Select(f => f.ThreadId)
            .ToListAsync();

    public async Task FollowThreadAsync(int userId, int threadId)
    {
        var thread = await _db.ForumThreads.FindAsync(threadId);
        if (thread == null || thread.IsDeleted)
            throw new KeyNotFoundException($"Thread {threadId} not found");

        var already = await _db.ForumThreadFollows
            .AnyAsync(f => f.UserId == userId && f.ThreadId == threadId);
        if (already) return; // idempotent : suivre deux fois ne crée pas de doublon

        _db.ForumThreadFollows.Add(new ForumThreadFollow
        {
            UserId = userId,
            ThreadId = threadId,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    public async Task UnfollowThreadAsync(int userId, int threadId)
    {
        var follow = await _db.ForumThreadFollows
            .FirstOrDefaultAsync(f => f.UserId == userId && f.ThreadId == threadId);
        if (follow == null) return; // ne suivait déjà pas : pas d'erreur

        _db.ForumThreadFollows.Remove(follow);
        await _db.SaveChangesAsync();
    }

    public async Task<ForumThreadListResponse> GetFeedAsync(
        int? userId, string? category, int page, int pageSize, string sort, bool followedOnly)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query = _db.ForumThreads.Where(t => !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(category) && category != "all")
            query = query.Where(t => t.Category == category);

        // Un visiteur anonyme ne peut rien suivre : le filtre est ignoré pour lui
        // plutôt que de renvoyer une liste vide qui ressemblerait à une erreur.
        if (followedOnly && userId.HasValue)
        {
            var followedIds = _db.ForumThreadFollows.Where(f => f.UserId == userId.Value).Select(f => f.ThreadId);
            query = query.Where(t => followedIds.Contains(t.Id));
        }

        var total = await query.CountAsync();

        // Les fils épinglés restent toujours en tête, quel que soit le tri choisi.
        IQueryable<Backend.Models.Entities.ForumThread> sorted = sort switch
        {
            "active"      => query.OrderByDescending(t => t.IsPinned).ThenByDescending(t => t.UpdatedAt),
            "unanswered"  => query.OrderByDescending(t => t.IsPinned).ThenBy(t => t.RepliesCount).ThenByDescending(t => t.CreatedAt),
            "popular"     => query.OrderByDescending(t => t.IsPinned).ThenByDescending(t => t.Upvotes).ThenByDescending(t => t.CreatedAt),
            "oldest"      => query.OrderByDescending(t => t.IsPinned).ThenBy(t => t.CreatedAt),
            _ /* recent */ => query.OrderByDescending(t => t.IsPinned).ThenByDescending(t => t.CreatedAt),
        };

        var pageThreadIds = await sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => t.Id)
            .ToListAsync();

        var threads = await _db.ForumThreads
            .Where(t => pageThreadIds.Contains(t.Id))
            .Include(t => t.User)
            .Select(t => new ForumThreadResponse
            {
                Id = t.Id,
                UserId = t.UserId,
                AuthorName = t.User != null ? (t.User.FirstName + " " + t.User.LastName).Trim() : null,
                AuthorRole = t.User != null ? t.User.Role : null,
                IsVerifiedInstitution = t.User != null && t.User.Role == "institution" && t.User.IsEmailVerified,
                IsVerifiedTeacher = t.User != null && t.User.Role == "teacher"
                    && _db.TutorProfiles.Any(tp => tp.UserId == t.UserId && tp.IsDiplomaVerified),
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
                UpdatedAt = t.UpdatedAt,
            })
            .ToListAsync();

        // Remet l'ordre décidé par le tri (la requête Where ci-dessus ne le conserve pas).
        var byId = threads.ToDictionary(t => t.Id);
        var ordered = pageThreadIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();

        if (userId.HasValue && ordered.Count > 0)
        {
            var followedSet = await _db.ForumThreadFollows
                .Where(f => f.UserId == userId.Value && pageThreadIds.Contains(f.ThreadId))
                .Select(f => f.ThreadId)
                .ToListAsync();
            var followedHash = followedSet.ToHashSet();
            foreach (var t in ordered)
                t.IsFollowed = followedHash.Contains(t.Id);
        }

        return new ForumThreadListResponse
        {
            Threads = ordered,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }
}
