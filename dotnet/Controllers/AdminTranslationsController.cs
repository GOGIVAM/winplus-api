using Backend.Data;
using Backend.Models.Entities;
using Backend.Services.Translation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Page admin « Traductions » :
///
///   GET    /api/admin/translations/summary               états, coût cumulé, dernier passage
///   GET    /api/admin/translations/items?filter=&kind=    contenus à relire (auto), à revoir, en échec, en attente
///   POST   /api/admin/translations/items/{kind}/{id}/approve   traduction relue, acceptée telle quelle (devient « human »)
///   POST   /api/admin/translations/translate-missing?dryRun=   traduction initiale / rattrapage
///   GET    /api/admin/translations/taxonomy?kind=&needsReview=
///   POST   /api/admin/translations/taxonomy
///   PUT    /api/admin/translations/taxonomy/{id}
///   DELETE /api/admin/translations/taxonomy/{id}
/// </summary>
[ApiController]
[Route("api/admin/translations")]
[Authorize(Policy = "AdminOnly")]
public class AdminTranslationsController : ControllerBase
{
    private static readonly string[] TaxonomyKinds = { "category", "examType", "level", "series", "cycle", "subsystem", "status", "session", "difficulty" };

    private readonly ApplicationDbContext _db;
    private readonly ContentTranslationService _service;
    private readonly IContentTranslationQueue _queue;
    private readonly IContentTranslationIndex _index;

    public AdminTranslationsController(ApplicationDbContext db, ContentTranslationService service,
        IContentTranslationQueue queue, IContentTranslationIndex index)
    {
        _db = db;
        _service = service;
        _queue = queue;
        _index = index;
    }

    public sealed record ItemRow(string Kind, int Id, int? ParentId, string Title, string? TitleEn, string? Origin, string? State,
        DateTime? TranslatedAt, bool Published);

    private async Task<List<ItemRow>> AllRowsAsync()
    {
        var rows = new List<ItemRow>();
        rows.AddRange((await _db.Subjects.AsNoTracking().Where(s => !s.IsDeleted)
            .Select(s => new { s.Id, s.Title, s.TitleEn, s.TranslationOrigin, s.TranslationState, s.TranslatedAt, s.IsPublished,
                IsExam = _db.Exams.Any(e => e.SubjectId == s.Id) }).ToListAsync())
            .Select(s => new ItemRow(s.IsExam ? "subject" : "book", s.Id, null, s.Title, s.TitleEn, s.TranslationOrigin, s.TranslationState, s.TranslatedAt, s.IsPublished)));
        rows.AddRange((await _db.Exams.AsNoTracking().Where(e => !e.IsDeleted)
            .Select(e => new { e.Id, e.SubjectId, e.Title, e.TitleEn, e.TranslationOrigin, e.TranslationState, e.TranslatedAt, e.IsPublished }).ToListAsync())
            .Select(e => new ItemRow(ContentKinds.Exam, e.Id, e.SubjectId, e.Title, e.TitleEn, e.TranslationOrigin, e.TranslationState, e.TranslatedAt, e.IsPublished)));
        rows.AddRange((await _db.Courses.AsNoTracking()
            .Select(c => new { c.Id, c.Title, c.TitleEn, c.TranslationOrigin, c.TranslationState, c.TranslatedAt, c.Status }).ToListAsync())
            .Select(c => new ItemRow(ContentKinds.Course, c.Id, null, c.Title, c.TitleEn, c.TranslationOrigin, c.TranslationState, c.TranslatedAt, c.Status == "published")));
        // Chapitres et leçons : seulement ceux qui demandent une action (la formation les regroupe).
        rows.AddRange((await _db.CourseSections.AsNoTracking()
            .Where(s => s.TranslationState == TranslationStates.Review || s.TranslationState == TranslationStates.Failed)
            .Select(s => new { s.Id, s.CourseId, s.Title, s.TitleEn, s.TranslationOrigin, s.TranslationState, s.TranslatedAt }).ToListAsync())
            .Select(s => new ItemRow("section", s.Id, s.CourseId, s.Title, s.TitleEn, s.TranslationOrigin, s.TranslationState, s.TranslatedAt, true)));
        rows.AddRange((await _db.CourseLessons.AsNoTracking()
            .Where(s => s.TranslationState == TranslationStates.Review || s.TranslationState == TranslationStates.Failed)
            .Select(s => new { s.Id, s.CourseId, s.Title, s.TitleEn, s.TranslationOrigin, s.TranslationState, s.TranslatedAt }).ToListAsync())
            .Select(s => new ItemRow("lesson", s.Id, s.CourseId, s.Title, s.TitleEn, s.TranslationOrigin, s.TranslationState, s.TranslatedAt, true)));
        foreach (var ev in await _db.ConcoursEvents.AsNoTracking().ToListAsync())
        {
            var meta = ConcoursTranslationMeta.Parse(ev.TranslationMetaJson);
            var state = meta.Failed ? TranslationStates.Failed : meta.Review.Count > 0 ? TranslationStates.Review
                : meta.Pending ? TranslationStates.Pending : meta.Auto.Count > 0 || meta.Human.Count > 0 ? TranslationStates.Ok : null;
            var origin = meta.Auto.Count > 0 ? TranslationOrigins.Auto : meta.Human.Count > 0 ? TranslationOrigins.Human : null;
            rows.Add(new ItemRow(ContentKinds.ConcoursEvent, ev.Id, null, $"{ev.Name} {ev.Year}", ev.NameEn, origin, state, ev.UpdatedAt, ev.IsPublished));
        }
        return rows;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var rows = await AllRowsAsync();
        var published = rows.Where(r => r.Published && r.Kind is not ("section" or "lesson")).ToList();
        var tokens = await _db.TranslationCache.AsNoTracking().SumAsync(c => (long)c.TokensUsed);
        var (missing, chars) = await _service.FindMissingAsync(includeNeverRequested: true);
        return Ok(new
        {
            published = published.Count,
            toReview = published.Count(r => r.Origin == TranslationOrigins.Auto && r.State == TranslationStates.Ok),
            review = rows.Count(r => r.State == TranslationStates.Review),
            failed = rows.Count(r => r.State == TranslationStates.Failed),
            pending = published.Count(r => r.State == TranslationStates.Pending),
            neverTranslated = published.Count(r => r.State == null && r.Origin == null),
            human = published.Count(r => r.Origin == TranslationOrigins.Human),
            taxonomyToReview = await _db.ContentTranslations.CountAsync(t => t.NeedsReview),
            taxonomyTotal = await _db.ContentTranslations.CountAsync(),
            tokensUsed = tokens,
            missing = missing.Count,
            estimatedTokens = ContentTranslationService.EstimateTokens(chars, missing.Count),
            lastRun = ContentTranslationWorker.LastRun,
        });
    }

    [HttpGet("items")]
    public async Task<IActionResult> Items([FromQuery] string filter = "attention", [FromQuery] string? kind = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var rows = await AllRowsAsync();
        IEnumerable<ItemRow> q = filter switch
        {
            "toReview" => rows.Where(r => r.Origin == TranslationOrigins.Auto && r.State == TranslationStates.Ok),
            "review" => rows.Where(r => r.State == TranslationStates.Review),
            "failed" => rows.Where(r => r.State == TranslationStates.Failed),
            "pending" => rows.Where(r => r.State == TranslationStates.Pending),
            "missing" => rows.Where(r => r.Published && r.State == null && r.Origin == null),
            "human" => rows.Where(r => r.Origin == TranslationOrigins.Human),
            "all" => rows,
            // Par défaut : tout ce qui demande une action.
            _ => rows.Where(r => r.State is TranslationStates.Review or TranslationStates.Failed
                                 || (r.Origin == TranslationOrigins.Auto && r.State == TranslationStates.Ok)),
        };
        if (!string.IsNullOrWhiteSpace(kind)) q = q.Where(r => r.Kind == kind);
        var list = q.OrderBy(r => r.State == TranslationStates.Review ? 0 : r.State == TranslationStates.Failed ? 1 : 2)
            .ThenByDescending(r => r.TranslatedAt).ToList();
        return Ok(new { total = list.Count, page, pageSize, items = list.Skip((page - 1) * pageSize).Take(pageSize) });
    }

    /// <summary>Traduction automatique relue et acceptée telle quelle : elle devient « human » (protégée).</summary>
    [HttpPost("items/{kind}/{id:int}/approve")]
    public async Task<IActionResult> Approve(string kind, int id)
    {
        ITranslatableContent? row = kind switch
        {
            "subject" or "book" => await _db.Subjects.FirstOrDefaultAsync(s => s.Id == id),
            ContentKinds.Exam => await _db.Exams.FirstOrDefaultAsync(e => e.Id == id),
            ContentKinds.Course => await _db.Courses.FirstOrDefaultAsync(c => c.Id == id),
            "section" => await _db.CourseSections.FirstOrDefaultAsync(c => c.Id == id),
            "lesson" => await _db.CourseLessons.FirstOrDefaultAsync(c => c.Id == id),
            _ => null,
        };
        if (row == null) return NotFound(new { error = "Contenu introuvable." });
        row.TranslationOrigin = TranslationOrigins.Human;
        row.TranslationState = TranslationStates.Ok;
        row.TranslationSourceHash = ContentTranslationService.SourceHash(row);
        row.TranslatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _index.Invalidate();
        return Ok(new { success = true });
    }

    /// <summary>
    /// Traduction initiale et rattrapage : tous les contenus publiés sans
    /// traduction à jour (jamais traduits, en échec, en attente). Les
    /// traductions humaines ne sont jamais concernées.
    /// dryRun=true (par défaut) : nombre de contenus et estimation en tokens, rien n'est lancé.
    /// dryRun=false : les contenus passent « pending » et la tâche de fond les traduit.
    /// limit : nombre maximal de contenus mis en file (essai sur 5 éléments).
    /// </summary>
    [HttpPost("translate-missing")]
    public async Task<IActionResult> TranslateMissing([FromQuery] bool dryRun = true, [FromQuery] int limit = 0)
    {
        var (refs, chars) = await _service.FindMissingAsync(includeNeverRequested: true);
        if (limit > 0) refs = refs.Take(limit).ToList();
        var estimate = ContentTranslationService.EstimateTokens(chars, refs.Count);
        var byKind = refs.GroupBy(r => r.Kind).ToDictionary(g => g.Key, g => g.Count());
        if (dryRun) return Ok(new { dryRun, count = refs.Count, byKind, chars, estimatedTokens = estimate });

        var ids = refs.GroupBy(r => r.Kind).ToDictionary(g => g.Key, g => g.Select(r => r.Id).ToList());
        var rows = new List<ITranslatableContent>();
        if (ids.TryGetValue(ContentKinds.Subject, out var sIds)) rows.AddRange(await _db.Subjects.Where(s => sIds.Contains(s.Id)).ToListAsync());
        if (ids.TryGetValue(ContentKinds.Exam, out var eIds)) rows.AddRange(await _db.Exams.Where(s => eIds.Contains(s.Id)).ToListAsync());
        if (ids.TryGetValue(ContentKinds.Course, out var cIds)) rows.AddRange(await _db.Courses.Where(s => cIds.Contains(s.Id)).ToListAsync());
        foreach (var row in rows.Where(r => r.TranslationOrigin != TranslationOrigins.Human))
            row.TranslationState = TranslationStates.Pending;
        await _db.SaveChangesAsync();
        foreach (var r in refs) _queue.Enqueue(r);
        return Accepted(new { dryRun, count = refs.Count, byKind, chars, estimatedTokens = estimate,
            message = "Traduction lancée en tâche de fond. Suivez l'avancement dans le résumé." });
    }

    // ── Taxonomie ───────────────────────────────────────────────────────────

    public sealed record TaxonomyWrite(string? Kind, string? ValueFr, string? ValueEn);

    [HttpGet("taxonomy")]
    public async Task<IActionResult> Taxonomy([FromQuery] string? kind = null, [FromQuery] bool? needsReview = null, [FromQuery] string? q = null)
    {
        var query = _db.ContentTranslations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(kind)) query = query.Where(t => t.Kind == kind);
        if (needsReview.HasValue) query = query.Where(t => t.NeedsReview == needsReview.Value);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(t => t.ValueFr.ToLower().Contains(term) || t.ValueEn.ToLower().Contains(term));
        }
        var items = await query.OrderByDescending(t => t.NeedsReview).ThenBy(t => t.Kind).ThenBy(t => t.ValueFr).ToListAsync();
        return Ok(new { kinds = TaxonomyKinds, items });
    }

    [HttpPost("taxonomy")]
    public async Task<IActionResult> CreateTaxonomy([FromBody] TaxonomyWrite body)
    {
        var kind = body.Kind?.Trim();
        var fr = body.ValueFr?.Trim();
        var en = body.ValueEn?.Trim();
        if (kind == null || !TaxonomyKinds.Contains(kind)) return BadRequest(new { error = "Type de libellé inconnu." });
        if (string.IsNullOrEmpty(fr) || string.IsNullOrEmpty(en) || fr.Length > 300 || en.Length > 300)
            return BadRequest(new { error = "Libellés français et anglais obligatoires (300 caractères au plus)." });
        if (await _db.ContentTranslations.AnyAsync(t => t.Kind == kind && t.ValueFr == fr))
            return Conflict(new { error = "Ce libellé existe déjà." });
        var row = new ContentTranslation { Kind = kind, ValueFr = fr, ValueEn = en, Origin = TranslationOrigins.Human };
        _db.ContentTranslations.Add(row);
        await _db.SaveChangesAsync();
        _index.Invalidate();
        return Ok(row);
    }

    /// <summary>Correction (ou validation) d'un libellé : il devient « human » et n'est plus à relire.</summary>
    [HttpPut("taxonomy/{id:int}")]
    public async Task<IActionResult> UpdateTaxonomy(int id, [FromBody] TaxonomyWrite body)
    {
        var row = await _db.ContentTranslations.FirstOrDefaultAsync(t => t.Id == id);
        if (row == null) return NotFound(new { error = "Libellé introuvable." });
        var en = body.ValueEn?.Trim();
        if (string.IsNullOrEmpty(en) || en.Length > 300) return BadRequest(new { error = "Libellé anglais obligatoire (300 caractères au plus)." });
        row.ValueEn = en;
        row.Origin = TranslationOrigins.Human;
        row.NeedsReview = false;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _index.Invalidate();
        return Ok(row);
    }

    [HttpDelete("taxonomy/{id:int}")]
    public async Task<IActionResult> DeleteTaxonomy(int id)
    {
        var row = await _db.ContentTranslations.FirstOrDefaultAsync(t => t.Id == id);
        if (row == null) return NotFound(new { error = "Libellé introuvable." });
        _db.ContentTranslations.Remove(row);
        await _db.SaveChangesAsync();
        _index.Invalidate();
        return Ok(new { success = true });
    }
}
