using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.Entities;

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

    public AdminLibraryController(ApplicationDbContext db, ILogger<AdminLibraryController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public class LibraryWriteDto
    {
        /// <summary>livre | video</summary>
        public string? ContentType { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
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

        var content = await _db.Set<CourseContent>()
            .Where(c => c.SubjectId == id)
            .OrderBy(c => c.OrderIndex)
            .FirstOrDefaultAsync();

        if (content != null)
        {
            if (dto.DocumentUrl     != null) content.DocumentUrl     = dto.DocumentUrl.Trim();
            if (dto.VideoUrl        != null) content.VideoUrl        = dto.VideoUrl.Trim();
            if (dto.DurationMinutes != null) content.DurationMinutes = dto.DurationMinutes.Value;
            if (dto.IsPublished     != null) content.Status          = dto.IsPublished.Value ? "published" : "draft";
            content.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true, data = new { subjectId = subject.Id } });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.Id == id);
        if (subject == null) return NotFound(new { error = "Fiche introuvable" });

        subject.IsDeleted   = true;
        subject.IsPublished = false;
        subject.UpdatedAt   = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}
