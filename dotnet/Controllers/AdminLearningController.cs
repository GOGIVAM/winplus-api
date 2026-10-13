using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;

namespace Backend.Controllers;

/// <summary>
/// Listes admin dédiées des quiz et des fiches de révision (lot 9.7a, suivi
/// 18.1) : paginées et filtrées en base (≤ 100 par page), y compris les
/// éléments non publiés. L'écran utilisait les routes publiques /api/quizzes
/// et /api/revisions avec pageSize=500 ou 1000, sans plafond côté serveur.
/// Publication, dépublication et suppression restent sur les routes
/// existantes (POST /api/quizzes/{id}/publish|unpublish, DELETE…), réservées
/// à l'admin et journalisées.
/// </summary>
[ApiController]
[Authorize(Policy = "AdminOnly")]
[Produces("application/json")]
public class AdminLearningController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public AdminLearningController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet("api/admin/quizzes")]
    public async Task<IActionResult> Quizzes([FromQuery] string? q, [FromQuery] string? published, [FromQuery] string? origin,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.Quizzes.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(x => x.Title.ToLower().Contains(term) || x.Subject.ToLower().Contains(term));
        }
        if (published == "true") query = query.Where(x => x.IsPublished);
        else if (published == "false") query = query.Where(x => !x.IsPublished);
        if (origin == "ai") query = query.Where(x => x.IsAIGenerated);
        else if (origin == "manual") query = query.Where(x => !x.IsAIGenerated);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                id = x.Id, title = x.Title, subject = x.Subject, isAIGenerated = x.IsAIGenerated,
                isPublished = x.IsPublished, usage = x.Attempts, createdAt = x.CreatedAt,
            })
            .ToListAsync();
        return Ok(new { success = true, data = new { items, total, page, pageSize } });
    }

    [HttpGet("api/admin/revisions")]
    public async Task<IActionResult> Revisions([FromQuery] string? q, [FromQuery] string? published, [FromQuery] string? origin,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.Revisions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(x => x.Title.ToLower().Contains(term) || x.Subject.ToLower().Contains(term));
        }
        if (published == "true") query = query.Where(x => x.IsPublished);
        else if (published == "false") query = query.Where(x => !x.IsPublished);
        if (origin == "ai") query = query.Where(x => x.IsAIGenerated);
        else if (origin == "manual") query = query.Where(x => !x.IsAIGenerated);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                id = x.Id, title = x.Title, subject = x.Subject,
                isAIGenerated = x.IsAIGenerated,
                isPublished = x.IsPublished,
                usage = _db.RevisionEnrollments.Count(p => p.RevisionId == x.Id),
                createdAt = x.CreatedAt,
            })
            .ToListAsync();
        return Ok(new { success = true, data = new { items, total, page, pageSize } });
    }
}
