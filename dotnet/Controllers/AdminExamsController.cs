using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.Entities;

namespace Backend.Controllers;

/// <summary>
/// CRUD complet des épreuves, côté administration.
///
/// L'API n'exposait que « approuver » et « rejeter » un sujet, plus l'envoi
/// d'un PDF : impossible de créer, corriger ou retirer une épreuve depuis le
/// dashboard. Ce contrôleur couvre le cycle complet.
///
///   GET    /api/admin/exams                 liste paginée, recherche + filtres
///   GET    /api/admin/exams/{id}            fiche complète
///   POST   /api/admin/exams                 création
///   PUT    /api/admin/exams/{id}            modification
///   DELETE /api/admin/exams/{id}            suppression douce (corbeille)
///   POST   /api/admin/exams/{id}/restore    sortie de corbeille
///   DELETE /api/admin/exams/{id}/hard       suppression définitive
///   POST   /api/admin/exams/{id}/publish    publier / dépublier
///   POST   /api/admin/exams/bulk            action groupée (publier, corbeille…)
///   GET    /api/admin/exams/filters         valeurs distinctes pour les filtres
///
/// Les fichiers (document, corrigé, image) sont envoyés séparément par
/// AdminUploadsController, qui renvoie une URL ; on la pose ensuite ici via
/// POST ou PUT. Un fichier de plusieurs Go ne transite donc jamais par cette
/// route.
/// </summary>
[ApiController]
[Route("api/admin/exams")]
[Produces("application/json")]
[Authorize(Policy = "AdminOnly")]
public class AdminExamsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AdminExamsController> _logger;

    public AdminExamsController(ApplicationDbContext db, ILogger<AdminExamsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── DTOs ────────────────────────────────────────────────────────────────

    public class ExamWriteDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? ExamType { get; set; }
        public string? Category { get; set; }
        public int? Year { get; set; }
        public string? Session { get; set; }
        public string? Level { get; set; }
        public int? DurationMinutes { get; set; }
        public string? Difficulty { get; set; }
        public string? DocumentUrl { get; set; }
        public string? CorrectionUrl { get; set; }
        public string? ThumbnailUrl { get; set; }
        public bool? IsPublished { get; set; }
        public int? SubjectId { get; set; }
    }

    public record BulkDto(List<int> Ids, string Action);

    private static object Shape(Exam e) => new
    {
        id              = e.Id,
        title           = e.Title,
        description     = e.Description,
        examType        = e.ExamType,
        category        = e.Category,
        year            = e.Year,
        session         = e.Session,
        level           = e.Level,
        durationMinutes = e.DurationMinutes,
        difficulty      = e.Difficulty,
        documentUrl     = e.DocumentUrl,
        correctionUrl   = e.CorrectionUrl,
        thumbnailUrl    = e.ThumbnailUrl,
        downloadCount   = e.DownloadCount,
        isPublished     = e.IsPublished,
        isDeleted       = e.IsDeleted,
        subjectId       = e.SubjectId,
        createdAt       = e.CreatedAt,
        updatedAt       = e.UpdatedAt,
    };

    // ── Lecture ─────────────────────────────────────────────────────────────

    /// <summary>Liste paginée. `status` = published | draft | trash | all.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] string? q = null,
        [FromQuery] string? category = null,
        [FromQuery] string? examType = null,
        [FromQuery] string? level = null,
        [FromQuery] int? year = null,
        [FromQuery] string status = "all",
        [FromQuery] string sort = "recent")
    {
        page  = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 200);

        try
        {
            var query = _db.Exams.AsNoTracking().AsQueryable();

            query = status switch
            {
                "trash"     => query.Where(e => e.IsDeleted),
                "published" => query.Where(e => !e.IsDeleted && e.IsPublished),
                "draft"     => query.Where(e => !e.IsDeleted && !e.IsPublished),
                _           => query.Where(e => !e.IsDeleted),
            };

            if (!string.IsNullOrWhiteSpace(q))
            {
                var needle = q.Trim().ToLower();
                query = query.Where(e =>
                    e.Title.ToLower().Contains(needle) ||
                    (e.Description != null && e.Description.ToLower().Contains(needle)) ||
                    e.Category.ToLower().Contains(needle));
            }

            if (!string.IsNullOrWhiteSpace(category)) query = query.Where(e => e.Category == category);
            if (!string.IsNullOrWhiteSpace(examType)) query = query.Where(e => e.ExamType == examType);
            if (!string.IsNullOrWhiteSpace(level))    query = query.Where(e => e.Level == level);
            if (year.HasValue)                        query = query.Where(e => e.Year == year.Value);

            query = sort switch
            {
                "title"     => query.OrderBy(e => e.Title),
                "year"      => query.OrderByDescending(e => e.Year).ThenBy(e => e.Title),
                "downloads" => query.OrderByDescending(e => e.DownloadCount),
                _           => query.OrderByDescending(e => e.CreatedAt ?? DateTime.MinValue).ThenByDescending(e => e.Id),
            };

            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * limit).Take(limit).ToListAsync();

            return Ok(new
            {
                success = true,
                data = items.Select(Shape),
                pagination = new { page, limit, total, totalPages = (int)Math.Ceiling(total / (double)limit) },
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur liste des épreuves");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOne(int id)
    {
        var exam = await _db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
        if (exam == null) return NotFound(new { error = "Épreuve introuvable" });
        return Ok(new { success = true, data = Shape(exam) });
    }

    /// <summary>Valeurs réellement présentes en base, pour alimenter les filtres.</summary>
    [HttpGet("filters")]
    public async Task<IActionResult> Filters()
    {
        var live = _db.Exams.AsNoTracking().Where(e => !e.IsDeleted);
        return Ok(new
        {
            success = true,
            data = new
            {
                categories = await live.Where(e => e.Category != null).Select(e => e.Category).Distinct().OrderBy(x => x).ToListAsync(),
                examTypes  = await live.Where(e => e.ExamType != null).Select(e => e.ExamType).Distinct().OrderBy(x => x).ToListAsync(),
                levels     = await live.Where(e => e.Level != null).Select(e => e.Level!).Distinct().OrderBy(x => x).ToListAsync(),
                years      = await live.Select(e => e.Year).Distinct().OrderByDescending(x => x).ToListAsync(),
                counts = new
                {
                    published = await _db.Exams.CountAsync(e => !e.IsDeleted && e.IsPublished),
                    draft     = await _db.Exams.CountAsync(e => !e.IsDeleted && !e.IsPublished),
                    trash     = await _db.Exams.CountAsync(e => e.IsDeleted),
                },
            },
        });
    }

    // ── Écriture ────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ExamWriteDto dto)
    {
        var error = Validate(dto, creating: true);
        if (error != null) return BadRequest(new { error });

        try
        {
            var exam = new Exam
            {
                Title           = dto.Title!.Trim(),
                Description     = dto.Description?.Trim(),
                ExamType        = dto.ExamType!.Trim(),
                Category        = dto.Category!.Trim(),
                Year            = dto.Year!.Value,
                Session         = dto.Session?.Trim(),
                Level           = dto.Level?.Trim(),
                DurationMinutes = dto.DurationMinutes,
                Difficulty      = dto.Difficulty?.Trim(),
                DocumentUrl     = dto.DocumentUrl?.Trim(),
                CorrectionUrl   = dto.CorrectionUrl?.Trim(),
                ThumbnailUrl    = dto.ThumbnailUrl?.Trim(),
                // Une épreuve sans document ne peut pas être publiée : on la
                // garde en brouillon plutôt que d'annoncer un contenu vide.
                IsPublished     = (dto.IsPublished ?? false) && !string.IsNullOrWhiteSpace(dto.DocumentUrl),
                SubjectId       = dto.SubjectId,
                CreatedAt       = DateTime.UtcNow,
                UpdatedAt       = DateTime.UtcNow,
            };

            _db.Exams.Add(exam);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Épreuve créée #{Id} — {Title}", exam.Id, exam.Title);
            return CreatedAtAction(nameof(GetOne), new { id = exam.Id }, new { success = true, data = Shape(exam) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur création d'épreuve");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ExamWriteDto dto)
    {
        var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == id);
        if (exam == null) return NotFound(new { error = "Épreuve introuvable" });

        var error = Validate(dto, creating: false);
        if (error != null) return BadRequest(new { error });

        try
        {
            // Mise à jour partielle : un champ absent du corps n'est pas touché.
            if (dto.Title       != null) exam.Title       = dto.Title.Trim();
            if (dto.Description != null) exam.Description = dto.Description.Trim();
            if (dto.ExamType    != null) exam.ExamType    = dto.ExamType.Trim();
            if (dto.Category    != null) exam.Category    = dto.Category.Trim();
            if (dto.Year        != null) exam.Year        = dto.Year.Value;
            if (dto.Session     != null) exam.Session     = dto.Session.Trim();
            if (dto.Level       != null) exam.Level       = dto.Level.Trim();
            if (dto.Difficulty  != null) exam.Difficulty  = dto.Difficulty.Trim();
            if (dto.DurationMinutes != null) exam.DurationMinutes = dto.DurationMinutes;
            if (dto.DocumentUrl   != null) exam.DocumentUrl   = Blank(dto.DocumentUrl);
            if (dto.CorrectionUrl != null) exam.CorrectionUrl = Blank(dto.CorrectionUrl);
            if (dto.ThumbnailUrl  != null) exam.ThumbnailUrl  = Blank(dto.ThumbnailUrl);
            if (dto.SubjectId     != null) exam.SubjectId     = dto.SubjectId;

            if (dto.IsPublished != null)
            {
                if (dto.IsPublished.Value && string.IsNullOrWhiteSpace(exam.DocumentUrl))
                    return BadRequest(new { error = "Impossible de publier une épreuve sans document" });
                exam.IsPublished = dto.IsPublished.Value;
            }

            exam.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new { success = true, data = Shape(exam) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur mise à jour épreuve {Id}", id);
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>Corbeille : l'épreuve disparaît du site mais reste récupérable.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> SoftDelete(int id)
    {
        var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == id);
        if (exam == null) return NotFound(new { error = "Épreuve introuvable" });

        exam.IsDeleted   = true;
        exam.IsPublished = false;
        exam.UpdatedAt   = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id)
    {
        var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == id);
        if (exam == null) return NotFound(new { error = "Épreuve introuvable" });

        exam.IsDeleted = false;
        exam.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { success = true, data = Shape(exam) });
    }

    /// <summary>
    /// Suppression définitive. Refusée si des quiz ou des révisions y sont
    /// rattachés : on ne casse pas silencieusement le travail des étudiants.
    /// </summary>
    [HttpDelete("{id:int}/hard")]
    public async Task<IActionResult> HardDelete(int id)
    {
        var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == id);
        if (exam == null) return NotFound(new { error = "Épreuve introuvable" });

        var quizzes   = await _db.Quizzes.CountAsync(q => q.ExamId == id);
        var revisions = await _db.Revisions.CountAsync(r => r.ExamId == id);
        if (quizzes > 0 || revisions > 0)
            return Conflict(new
            {
                error = $"Épreuve rattachée à {quizzes} quiz et {revisions} révision(s). Mettez-la à la corbeille au lieu de la supprimer.",
            });

        _db.Exams.Remove(exam);
        await _db.SaveChangesAsync();
        _logger.LogWarning("Épreuve #{Id} supprimée définitivement", id);
        return Ok(new { success = true });
    }

    [HttpPost("{id:int}/publish")]
    public async Task<IActionResult> Publish(int id, [FromQuery] bool value = true)
    {
        var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == id);
        if (exam == null) return NotFound(new { error = "Épreuve introuvable" });

        if (value && string.IsNullOrWhiteSpace(exam.DocumentUrl))
            return BadRequest(new { error = "Impossible de publier une épreuve sans document" });

        exam.IsPublished = value;
        exam.UpdatedAt   = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { success = true, data = Shape(exam) });
    }

    /// <summary>Action groupée : publish | unpublish | trash | restore.</summary>
    [HttpPost("bulk")]
    public async Task<IActionResult> Bulk([FromBody] BulkDto body)
    {
        if (body.Ids == null || body.Ids.Count == 0)
            return BadRequest(new { error = "Aucune épreuve sélectionnée" });
        if (body.Ids.Count > 500)
            return BadRequest(new { error = "500 épreuves maximum par action groupée" });

        var exams = await _db.Exams.Where(e => body.Ids.Contains(e.Id)).ToListAsync();
        var skipped = 0;

        foreach (var e in exams)
        {
            switch (body.Action)
            {
                case "publish":
                    if (string.IsNullOrWhiteSpace(e.DocumentUrl)) { skipped++; continue; }
                    e.IsPublished = true; break;
                case "unpublish": e.IsPublished = false; break;
                case "trash":     e.IsDeleted = true; e.IsPublished = false; break;
                case "restore":   e.IsDeleted = false; break;
                default: return BadRequest(new { error = $"Action inconnue : {body.Action}" });
            }
            e.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true, data = new { affected = exams.Count - skipped, skipped } });
    }

    // ── Validation ──────────────────────────────────────────────────────────

    private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Validate(ExamWriteDto dto, bool creating)
    {
        if (creating)
        {
            if (string.IsNullOrWhiteSpace(dto.Title))    return "Titre requis";
            if (string.IsNullOrWhiteSpace(dto.ExamType)) return "Type d'examen requis";
            if (string.IsNullOrWhiteSpace(dto.Category)) return "Matière requise";
            if (dto.Year == null)                        return "Année requise";
        }
        if (dto.Title != null && dto.Title.Trim().Length is 0 or > 255)
            return "Le titre doit faire entre 1 et 255 caractères";
        if (dto.Year is { } y && (y < 1960 || y > DateTime.UtcNow.Year + 1))
            return "Année hors limites";
        if (dto.DurationMinutes is { } d && (d < 0 || d > 24 * 60))
            return "Durée hors limites";
        return null;
    }
}
