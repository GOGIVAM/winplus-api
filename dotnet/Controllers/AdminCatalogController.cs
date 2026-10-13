using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Catalogue de l'administration (lot 9.7a, suivi 18.1) : remplace l'ancien
/// GET /api/admin/subjects, qui ignorait le statut demandé, plafonnait à 100
/// lignes et ne renvoyait ni type, ni niveau, ni téléchargements.
///
///   GET    /api/admin/catalog                       liste filtrée et paginée (≤ 100)
///   PUT    /api/admin/catalog/{id}                  modification partielle (un champ absent n'est jamais effacé)
///   GET    /api/admin/catalog/{id}/removal-impact   acheteurs remboursés si on supprime
///   DELETE /api/admin/catalog/{id}?reason=          suppression avec remboursement des acheteurs
///
/// Les formations (Course) restent servies par /api/admin/courses.
/// </summary>
[ApiController]
[Route("api/admin/catalog")]
[Authorize(Policy = "AdminOnly")]
[Produces("application/json")]
public class AdminCatalogController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICatalogRemovalService _removal;
    private readonly IAdminAuditService _audit;

    public AdminCatalogController(ApplicationDbContext db, ICatalogRemovalService removal, IAdminAuditService audit)
    {
        _db = db;
        _removal = removal;
        _audit = audit;
    }

    public sealed class CatalogUpdateRequest
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
        /// <summary>Subject.Level : niveaux de classe ciblés (ciblage WinAI). Chaîne vide = effacer volontairement.</summary>
        public string? SubjectLevel { get; set; }
        public decimal? Price { get; set; }
        public bool? IsPublished { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q,
        [FromQuery] string? type,
        [FromQuery] string? status = "published",
        [FromQuery] string? level = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Subjects.AsNoTracking().Where(s => !s.IsDeleted);
        query = (status ?? "published") switch
        {
            "published" => query.Where(s => s.IsPublished),
            "pending" or "draft" => query.Where(s => !s.IsPublished),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(s => s.Title.ToLower().Contains(term) || (s.Category != null && s.Category.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(level))
        {
            var lv = level.Trim().ToLower();
            query = query.Where(s => (s.Level != null && s.Level.ToLower().Contains(lv))
                || _db.Exams.Any(e => e.SubjectId == s.Id && e.Level != null && e.Level.ToLower() == lv));
        }

        var exams = _db.Exams;
        var contents = _db.Set<CourseContent>();
        query = (type ?? "all") switch
        {
            "epreuve" => query.Where(s => exams.Any(e => e.SubjectId == s.Id)),
            "video" => query.Where(s => !exams.Any(e => e.SubjectId == s.Id) && contents.Any(c => c.SubjectId == s.Id && c.VideoUrl != null)),
            "livre" => query.Where(s => !exams.Any(e => e.SubjectId == s.Id)
                && !contents.Any(c => c.SubjectId == s.Id && c.VideoUrl != null)
                && contents.Any(c => c.SubjectId == s.Id && c.DocumentUrl != null)),
            _ => query,
        };

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new
            {
                s.Id,
                s.Title,
                s.Description,
                s.Category,
                SubjectLevel = s.Level,
                s.Price,
                s.IsPublished,
                s.ThumbnailUrl,
                s.CreatedAt,
                s.UpdatedAt,
                s.AuthorUserId,
                AuthorName = s.Author != null ? (s.Author.FirstName + " " + s.Author.LastName).Trim() : null,
                HasExam = exams.Any(e => e.SubjectId == s.Id),
                HasVideo = contents.Any(c => c.SubjectId == s.Id && c.VideoUrl != null),
                HasDocument = contents.Any(c => c.SubjectId == s.Id && c.DocumentUrl != null),
                ExamLevel = exams.Where(e => e.SubjectId == s.Id).Select(e => e.Level).FirstOrDefault(),
                ExamType = exams.Where(e => e.SubjectId == s.Id).Select(e => e.ExamType).FirstOrDefault(),
                Downloads = _db.DownloadHistories.Count(d => d.SubjectId == s.Id),
                Buyers = _db.OrderItems.Where(oi => oi.SubjectId == s.Id && oi.Order.UserId != null
                        && PaidOrderStatus.All.Contains(oi.Order.Status.ToLower()))
                    .Select(oi => oi.Order.UserId).Distinct().Count(),
            })
            .ToListAsync();

        var items = rows.Select(r => new
        {
            id = r.Id,
            title = r.Title,
            description = r.Description,
            category = r.Category,
            type = r.HasExam ? "epreuve" : r.HasVideo ? "video" : r.HasDocument ? "livre" : "autre",
            examType = r.ExamType,
            examLevel = r.ExamLevel,
            subjectLevel = r.SubjectLevel,
            price = r.Price,
            isFree = r.Price <= 0,
            isPublished = r.IsPublished,
            thumbnailUrl = r.ThumbnailUrl,
            downloads = r.Downloads,
            buyers = r.Buyers,
            authorId = r.AuthorUserId,
            authorName = string.IsNullOrWhiteSpace(r.AuthorName) ? null : r.AuthorName,
            createdAt = r.CreatedAt,
            updatedAt = r.UpdatedAt,
        }).ToList();

        return Ok(new { success = true, data = new { items, total, page, pageSize } });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CatalogUpdateRequest request)
    {
        var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);
        if (subject == null) return NotFound(new { success = false, error = "Contenu introuvable." });

        if (request.Title != null && string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { success = false, error = "Le titre ne peut pas être vide." });
        if (request.Price is < 0)
            return BadRequest(new { success = false, error = "Le prix ne peut pas être négatif." });

        var before = new { subject.Title, subject.Category, subjectLevel = subject.Level, subject.Price, subject.IsPublished };

        // Modification partielle : seul un champ envoyé change. L'ancien
        // écran envoyait « level: '' » et effaçait Subject.Level à chaque
        // enregistrement, via PUT /api/subjects/{id} qui écrase tout.
        if (request.Title != null) subject.Title = request.Title.Trim();
        if (request.Description != null) subject.Description = request.Description.Trim();
        if (request.Category != null) subject.Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
        if (request.SubjectLevel != null) subject.Level = string.IsNullOrWhiteSpace(request.SubjectLevel) ? null : request.SubjectLevel.Trim();
        if (request.Price.HasValue) subject.Price = decimal.Round(request.Price.Value, 0, MidpointRounding.AwayFromZero);
        if (request.IsPublished.HasValue) subject.IsPublished = request.IsPublished.Value;
        subject.UpdatedAt = DateTime.UtcNow;

        this.AuditAdd(_audit, AdminAuditActions.CatalogItemUpdated, "Subject", subject.Id, subject.Title, before,
            new { subject.Title, subject.Category, subjectLevel = subject.Level, subject.Price, subject.IsPublished });
        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            data = new { id = subject.Id, title = subject.Title, category = subject.Category, subjectLevel = subject.Level, price = subject.Price, isPublished = subject.IsPublished },
        });
    }

    [HttpGet("{id:int}/removal-impact")]
    public async Task<IActionResult> RemovalImpact(int id)
    {
        if (!await _db.Subjects.AnyAsync(s => s.Id == id && !s.IsDeleted))
            return NotFound(new { success = false, error = "Contenu introuvable." });
        var impact = await _removal.GetImpactAsync(id);
        return Ok(new { success = true, data = new { buyers = impact.Buyers, totalRefundXaf = impact.TotalRefundXaf } });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < AdminUsersController.MinReasonLength)
            return BadRequest(new { success = false, code = "reason_required", error = $"Un motif d'au moins {AdminUsersController.MinReasonLength} caractères est obligatoire." });

        var title = await _db.Subjects.AsNoTracking().Where(s => s.Id == id && !s.IsDeleted).Select(s => s.Title).FirstOrDefaultAsync();
        if (title == null) return NotFound(new { success = false, error = "Contenu introuvable." });

        var result = await _removal.RemoveAsync(id);
        if (result == null) return NotFound(new { success = false, error = "Contenu introuvable." });

        await this.AuditAsync(_audit, AdminAuditActions.CatalogItemDeleted, "Subject", id, title,
            after: new { result.RefundedBuyers, result.RefundedXaf, result.Failures }, reason: reason);

        return Ok(new
        {
            success = true,
            data = new { refundedBuyers = result.RefundedBuyers, refundedXaf = result.RefundedXaf, failures = result.Failures },
        });
    }
}
