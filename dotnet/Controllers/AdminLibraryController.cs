using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Services.Translation;

namespace Backend.Controllers;

/// <summary>
/// Mise en ligne d'un livre ou d'une vidéo depuis l'administration.
///
/// L'ancienne page d'upload postait tout  métadonnées ET fichier  sur
/// `/admin/upload`, une route qui n'existait pas côté API : rien n'était jamais
/// enregistré. Le fichier passe maintenant par AdminUploadsController (envoi
/// direct vers S3, en plusieurs parties), et cette route ne reçoit que les
/// métadonnées plus les URL renvoyées.
///
///   POST   /api/admin/library        crée un livre ou une vidéo
///   PUT    /api/admin/library/{id}   modifie
///   DELETE /api/admin/library/{id}   retire du catalogue (suppression douce)
///
/// Un livre ou une vidéo, dans ce schéma, c'est un `Subject` (fiche catalogue :
/// titre, matière, prix, image) porteur d'un `CourseContent` (le fichier).
/// </summary>
[ApiController]
[Route("api/admin/library")]
[Produces("application/json")]
[Authorize(Policy = "AdminOnly")]
public class AdminLibraryController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AdminLibraryController> _logger;
    private readonly IFastApiClient _fastApi;
    private readonly ICatalogRemovalService _removal;
    private readonly IAdminAuditService _audit;

    public AdminLibraryController(ApplicationDbContext db, ILogger<AdminLibraryController> logger, IFastApiClient fastApi,
        ICatalogRemovalService removal, IAdminAuditService audit)
    {
        _db = db;
        _logger = logger;
        _fastApi = fastApi;
        _removal = removal;
        _audit = audit;
    }

    /// <summary>
    /// Lot 9.7a : livres et vidéos mis en ligne par l'administration, pour
    /// l'édition et la suppression depuis l'écran Upload (paginé).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.Subjects.AsNoTracking()
            .Where(s => !s.IsDeleted && s.AuthorUserId == null
                     && !_db.Exams.Any(e => e.SubjectId == s.Id)
                     && _db.Set<CourseContent>().Any(c => c.SubjectId == s.Id && (c.VideoUrl != null || c.DocumentUrl != null)));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(s => s.Title.ToLower().Contains(term) || (s.Category != null && s.Category.ToLower().Contains(term)));
        }
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(s => s.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new
            {
                id = s.Id,
                title = s.Title,
                description = s.Description,
                category = s.Category,
                price = s.Price,
                isPublished = s.IsPublished,
                thumbnailUrl = s.ThumbnailUrl,
                createdAt = s.CreatedAt,
                contentType = _db.Set<CourseContent>().Where(c => c.SubjectId == s.Id).OrderBy(c => c.OrderIndex)
                    .Select(c => c.VideoUrl != null ? "video" : "livre").FirstOrDefault(),
                durationMinutes = _db.Set<CourseContent>().Where(c => c.SubjectId == s.Id).OrderBy(c => c.OrderIndex)
                    .Select(c => (int?)c.DurationMinutes).FirstOrDefault(),
            })
            .ToListAsync();
        return Ok(new { success = true, data = new { items, total, page, pageSize } });
    }

    public class LibraryWriteDto
    {
        /// <summary>livre | video</summary>
        public string? ContentType { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
        /// <summary>Lot 10.7 : langue d'origine du livre ("fr" ou "en"), déduite si absente.</summary>
        public string? Language { get; set; }
        public decimal? Price { get; set; }
        public string? DocumentUrl { get; set; }
        public string? VideoUrl { get; set; }
        public string? ThumbnailUrl { get; set; }
        public int? DurationMinutes { get; set; }
        public bool? IsPublished { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LibraryWriteDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))       return BadRequest(new { error = "Titre requis" });
        if (dto.ContentType is not ("livre" or "video")) return BadRequest(new { error = "Type attendu : livre ou video" });

        var fileUrl = dto.ContentType == "video" ? dto.VideoUrl : dto.DocumentUrl;
        if (string.IsNullOrWhiteSpace(fileUrl))
            return BadRequest(new { error = "Aucun fichier : envoyez-le d'abord via /api/admin/uploads" });

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var subject = new Subject
            {
                Title        = dto.Title!.Trim(),
                Description  = dto.Description?.Trim(),
                Category     = dto.Category?.Trim(),
                Language     = ContentSourceLanguage.Normalize(dto.Language),
                Price        = dto.Price ?? 0m,
                ThumbnailUrl = dto.ThumbnailUrl?.Trim(),
                IsPublished  = dto.IsPublished ?? false,
                CreatedAt    = DateTime.UtcNow,
                UpdatedAt    = DateTime.UtcNow,
            };
            _db.Subjects.Add(subject);
            await _db.SaveChangesAsync();

            var content = new CourseContent
            {
                SubjectId       = subject.Id,
                Title           = subject.Title,
                Description     = subject.Description,
                DocumentUrl     = dto.ContentType == "livre" ? fileUrl!.Trim() : null,
                VideoUrl        = dto.ContentType == "video" ? fileUrl!.Trim() : null,
                DurationMinutes = dto.DurationMinutes ?? 0,
                OrderIndex      = 0,
                Status          = (dto.IsPublished ?? false) ? "published" : "draft",
                CreatedByUserId = null,
                CreatedAt       = DateTime.UtcNow,
            };
            _db.Set<CourseContent>().Add(content);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            // RAG (voir topo validé) : indexe le contenu pour que WinAI puisse
            // s'appuyer dessus dans le chat. Fire-and-forget, ne bloque jamais
            // la réponse de cette route (contrainte "pas de latence à l'upload").
            _fastApi.QueueRagIngestion(
                docId: $"coursecontent_{content.Id}",
                title: subject.Title,
                fileUrl: fileUrl!.Trim(),
                authorizationHeader: Request.Headers["Authorization"].ToString(),
                category: dto.Category,
                subjectId: subject.Id,
                courseId: content.Id);

            _logger.LogInformation("{Type} mis en ligne : subject #{SubjectId}", dto.ContentType, subject.Id);
            return Ok(new
            {
                success = true,
                data = new { subjectId = subject.Id, contentId = content.Id, title = subject.Title },
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Erreur mise en ligne {Type}", dto.ContentType);
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] LibraryWriteDto dto)
    {
        var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);
        if (subject == null) return NotFound(new { error = "Fiche introuvable" });

        if (dto.Title        != null) subject.Title        = dto.Title.Trim();
        if (dto.Description  != null) subject.Description  = dto.Description.Trim();
        if (dto.Category     != null) subject.Category     = dto.Category.Trim();
        if (dto.Price        != null) subject.Price        = dto.Price.Value;
        if (dto.ThumbnailUrl != null) subject.ThumbnailUrl = dto.ThumbnailUrl.Trim();
        if (dto.IsPublished  != null) subject.IsPublished  = dto.IsPublished.Value;
        subject.UpdatedAt = DateTime.UtcNow;
        this.AuditAdd(_audit, AdminAuditActions.CatalogItemUpdated, "Subject", subject.Id, subject.Title,
            after: new { dto.Title, dto.Category, dto.Price, dto.IsPublished });

        var content = await _db.Set<CourseContent>()
            .Where(c => c.SubjectId == id)
            .OrderBy(c => c.OrderIndex)
            .FirstOrDefaultAsync();

        var fileChanged = false;
        if (content != null)
        {
            if (dto.DocumentUrl != null && dto.DocumentUrl.Trim() != content.DocumentUrl) { content.DocumentUrl = dto.DocumentUrl.Trim(); fileChanged = true; }
            if (dto.VideoUrl    != null && dto.VideoUrl.Trim()    != content.VideoUrl)    { content.VideoUrl    = dto.VideoUrl.Trim();    fileChanged = true; }
            if (dto.DurationMinutes != null) content.DurationMinutes = dto.DurationMinutes.Value;
            if (dto.IsPublished     != null) content.Status          = dto.IsPublished.Value ? "published" : "draft";
            content.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        // Nouveau fichier posé sur une fiche existante : ré-indexer. NOTE
        // (voir RAG/README.md « Limites connues ») : les anciens chunks ne
        // sont pas encore marqués "superseded" automatiquement depuis cette
        // route  la ré-ingestion s'ajoute au corpus plutôt que remplacer.
        if (content != null && fileChanged)
        {
            var newFileUrl = content.VideoUrl ?? content.DocumentUrl;
            if (!string.IsNullOrWhiteSpace(newFileUrl))
            {
                _fastApi.QueueRagIngestion(
                    docId: $"coursecontent_{content.Id}",
                    title: subject.Title,
                    fileUrl: newFileUrl,
                    authorizationHeader: Request.Headers["Authorization"].ToString(),
                    category: subject.Category,
                    subjectId: subject.Id,
                    courseId: content.Id);
            }
        }
        return Ok(new { success = true, data = new { subjectId = subject.Id } });
    }

    /// <summary>
    /// Retire un livre ou une vidéo : ses acheteurs sont remboursés au
    /// portefeuille (lot 9.7a, même règle que toute suppression de contenu).
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] string? reason = null)
    {
        var title = await _db.Subjects.AsNoTracking().Where(s => s.Id == id).Select(s => s.Title).FirstOrDefaultAsync();
        var result = await _removal.RemoveAsync(id);
        if (result == null) return NotFound(new { error = "Fiche introuvable" });
        await this.AuditAsync(_audit, AdminAuditActions.CatalogItemDeleted, "Subject", id, title,
            after: new { result.RefundedBuyers, result.RefundedXaf, result.Failures }, reason: reason);
        return Ok(new { success = true, data = result });
    }
}
