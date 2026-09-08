using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Models.Entities;

namespace Backend.Controllers;

/// <summary>
/// "Ma bibliothèque" (Module 2, US-CAT-05) : vue unifiée de tout le contenu
/// qu'un utilisateur possède — acheté ou ajouté au cœur — organisable en
/// dossiers avec notes privées. Ce n'est pas un système parallèle aux
/// Favoris : c'est leur usage étendu. Un contenu simplement acheté apparaît
/// ici sans être "un favori" au sens propre tant qu'il n'est pas organisé
/// (dossier/tag/note) ; dès qu'on l'organise, on écrit dans la même table
/// Favorite que le cœur du catalogue utilise déjà — un seul mécanisme, pas
/// deux bibliothèques à maintenir en parallèle.
/// </summary>
[ApiController]
[Route("api/library")]
[Authorize]
public class LibraryController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<LibraryController> _logger;

    public LibraryController(ApplicationDbContext db, ILogger<LibraryController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Toute la bibliothèque de l'utilisateur. `tag`/`collectionId` filtrent
    /// sur les éléments déjà organisés ; `annotatedOnly` = "Mes annotations"
    /// (US-CAT-08, contenus avec au moins un tag ou une note).
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMyLibrary(
        [FromQuery] string? tag = null,
        [FromQuery] int? collectionId = null,
        [FromQuery] bool annotatedOnly = false,
        [FromQuery] string? q = null)
    {
        var userId = User.GetUserId();

        var favorites = await _db.Favorites.AsNoTracking()
            .Include(f => f.Subject)
            .Include(f => f.Collection)
            .Where(f => f.UserId == userId)
            .ToListAsync();

        var items = favorites.Select(f => new LibraryItemDto
        {
            SubjectId = f.SubjectId,
            Title = f.Subject?.Title,
            ThumbnailUrl = f.Subject?.ThumbnailUrl,
            Category = f.Subject?.Category,
            FavoriteId = f.Id,
            CollectionId = f.CollectionId,
            CollectionName = f.Collection?.Name,
            Tags = ParseTags(f.Tags),
            Notes = f.Notes,
            AcquiredAt = f.AddedAt,
            Source = "organized",
        }).ToList();

        var organizedSubjectIds = items.Select(i => i.SubjectId).ToHashSet();

        var purchases = await _db.OrderItems.AsNoTracking()
            .Where(oi => oi.Order.UserId == userId && oi.Order.Status == "completed"
                && !organizedSubjectIds.Contains(oi.SubjectId))
            .Select(oi => new { oi.SubjectId, oi.Order.CreatedAt })
            .ToListAsync();

        if (purchases.Count > 0)
        {
            var purchasedIds = purchases.Select(p => p.SubjectId).Distinct().ToList();
            var subjectMap = await _db.Subjects.AsNoTracking()
                .Where(s => purchasedIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id);

            // Un même contenu peut avoir été acheté plusieurs fois (rare, mais
            // possible côté commande) : on ne garde que le premier achat.
            foreach (var group in purchases.GroupBy(p => p.SubjectId))
            {
                if (!subjectMap.TryGetValue(group.Key, out var subject)) continue;
                items.Add(new LibraryItemDto
                {
                    SubjectId = subject.Id,
                    Title = subject.Title,
                    ThumbnailUrl = subject.ThumbnailUrl,
                    Category = subject.Category,
                    AcquiredAt = group.Min(g => g.CreatedAt),
                    Source = "purchased",
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(tag))
            items = items.Where(i => i.Tags.Contains(tag.Trim(), StringComparer.OrdinalIgnoreCase)).ToList();
        if (collectionId.HasValue)
            items = items.Where(i => i.CollectionId == collectionId.Value).ToList();
        if (annotatedOnly)
            items = items.Where(i => i.Tags.Count > 0 || !string.IsNullOrWhiteSpace(i.Notes)).ToList();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLowerInvariant();
            items = items.Where(i =>
                (i.Title?.ToLowerInvariant().Contains(needle) ?? false) ||
                (i.Category?.ToLowerInvariant().Contains(needle) ?? false)).ToList();
        }

        return Ok(new { data = items.OrderByDescending(i => i.AcquiredAt).ToList(), success = true });
    }

    /// <summary>
    /// Range un contenu (acheté ou non) dans un dossier avec tags/notes.
    /// Crée le Favorite sous-jacent s'il n'existait pas encore  "organiser"
    /// un achat dans la bibliothèque revient à le mettre au cœur.
    /// </summary>
    [HttpPost("{subjectId:int}/organize")]
    public async Task<IActionResult> Organize(int subjectId, [FromBody] OrganizeLibraryItemRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            var favorite = await _db.Favorites.FirstOrDefaultAsync(f => f.UserId == userId && f.SubjectId == subjectId);

            if (favorite == null)
            {
                var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.Id == subjectId && !s.IsDeleted);
                if (subject == null) return NotFound(new { success = false, error = "Contenu introuvable." });
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null) return Unauthorized();

                favorite = new Favorite { UserId = userId, SubjectId = subjectId, User = user, Subject = subject };
                _db.Favorites.Add(favorite);
            }

            if (request.ClearCollection)
            {
                favorite.CollectionId = null;
            }
            else if (request.CollectionId.HasValue)
            {
                var ownsCollection = await _db.FavoriteCollections.AnyAsync(c => c.Id == request.CollectionId && c.UserId == userId);
                if (!ownsCollection) return BadRequest(new { success = false, error = "Dossier introuvable." });
                favorite.CollectionId = request.CollectionId;
            }

            if (request.Tags != null)
            {
                var cleanTags = request.Tags
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .ToList();
                favorite.Tags = JsonSerializer.Serialize(cleanTags);
            }

            if (request.Notes != null)
                favorite.Notes = request.Notes.Length > 300 ? request.Notes[..300] : request.Notes;

            await _db.SaveChangesAsync();
            return Ok(new { success = true, favoriteId = favorite.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error organizing library item {SubjectId}", subjectId);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    private static List<string> ParseTags(string? tagsJson)
    {
        if (string.IsNullOrWhiteSpace(tagsJson)) return new List<string>();
        try { return JsonSerializer.Deserialize<List<string>>(tagsJson) ?? new List<string>(); }
        catch { return new List<string>(); }
    }
}
