using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;

namespace Backend.Controllers;

public class UpdateTeacherContentRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }
    public int? DurationMinutes { get; set; }
}

/// <summary>
/// Actions sur les publications d'un enseignant (S4-1 / S4-2) et insights
/// dérivés des données réelles (S4-3). Le filtre CreatedByUserId corrige un
/// défaut d'UX majeur : un enseignant voyait les contenus de tous les autres.
///
/// GET    /api/teacher/contents/mine
/// GET    /api/teacher/contents/{id}/stats
/// PATCH  /api/teacher/contents/{id}
/// DELETE /api/teacher/contents/{id}
/// GET    /api/teacher/insights
/// GET    /api/teacher/revenue-share
/// </summary>
[ApiController]
[Route("api/teacher")]
[Authorize]
public class TeacherContentController : ControllerBase
{
    private static readonly string[] AllowedStatuses = { "published", "review", "draft", "archived" };

    private readonly ApplicationDbContext _db;
    private readonly ILogger<TeacherContentController> _logger;

    public TeacherContentController(ApplicationDbContext db, ILogger<TeacherContentController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Publications de l'enseignant connecté, filtrables par statut.</summary>
    [HttpGet("contents/mine")]
    public async Task<IActionResult> GetMine([FromQuery] string? status, [FromQuery] int limit = 100)
    {
        try
        {
            if (limit < 1 || limit > 300) limit = 100;
            var teacherId = User.GetUserId();

            var query = _db.CourseContents.AsNoTracking()
                .Where(c => c.CreatedByUserId == teacherId);

            if (!string.IsNullOrWhiteSpace(status) && status != "all")
                query = query.Where(c => c.Status == status);

            var items = await query
                .OrderByDescending(c => c.CreatedAt)
                .Take(limit)
                .Select(c => new
                {
                    c.Id, c.Title, c.Description, c.Status, c.SubjectId,
                    c.DocumentUrl, c.DurationMinutes, c.CreatedAt, c.UpdatedAt,
                    subjectTitle = c.Subject != null ? c.Subject.Title : null,
                    category     = c.Subject != null ? c.Subject.Category : null,
                    downloads    = c.Subject != null ? (c.Subject.DownloadCount ?? 0) : 0,
                    rating       = c.Subject != null ? c.Subject.AverageRating : 0,
                    ratingCount  = c.Subject != null ? c.Subject.TotalRatings : 0
                })
                .ToListAsync();

            var counts = await _db.CourseContents.AsNoTracking()
                .Where(c => c.CreatedByUserId == teacherId)
                .GroupBy(c => c.Status)
                .Select(g => new { status = g.Key, count = g.Count() })
                .ToListAsync();

            return Ok(new { data = items, counts, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher own contents");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Statistiques d'une publication : téléchargements, note, revenus encaissés.</summary>
    [HttpGet("contents/{id:int}/stats")]
    public async Task<IActionResult> GetStats([FromRoute] int id)
    {
        try
        {
            var teacherId = User.GetUserId();

            var content = await _db.CourseContents.AsNoTracking()
                .Where(c => c.Id == id && c.CreatedByUserId == teacherId)
                .Select(c => new { c.Id, c.Title, c.Status, c.SubjectId })
                .FirstOrDefaultAsync();

            if (content == null)
                return NotFound(new { success = false, error = "Publication introuvable ou non autorisée." });

            var subject = await _db.Subjects.AsNoTracking()
                .Where(s => s.Id == content.SubjectId)
                .Select(s => new { s.DownloadCount, s.AverageRating, s.TotalRatings, s.EnrollmentCount, s.Price })
                .FirstOrDefaultAsync();

            var gross = await _db.OrderItems.AsNoTracking()
                .Where(oi => oi.SubjectId == content.SubjectId)
                .SumAsync(oi => (decimal?)oi.PriceAtPurchase) ?? 0m;

            var sales = await _db.OrderItems.AsNoTracking()
                .CountAsync(oi => oi.SubjectId == content.SubjectId);

            var share = await GetRevenueShareAsync(teacherId);

            return Ok(new
            {
                data = new
                {
                    contentId   = content.Id,
                    title       = content.Title,
                    status      = content.Status,
                    downloads   = subject?.DownloadCount ?? 0,
                    rating      = subject?.AverageRating ?? 0m,
                    ratingCount = subject?.TotalRatings ?? 0,
                    enrollments = subject?.EnrollmentCount ?? 0,
                    sales,
                    grossRevenue = gross,
                    revenueShare = share,
                    netRevenue   = share.HasValue ? Math.Round(gross * share.Value, 0) : (decimal?)null
                },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting content stats {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPatch("contents/{id:int}")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] UpdateTeacherContentRequest request)
    {
        try
        {
            var teacherId = User.GetUserId();

            var content = await _db.CourseContents
                .FirstOrDefaultAsync(c => c.Id == id && c.CreatedByUserId == teacherId);
            if (content == null)
                return NotFound(new { success = false, error = "Publication introuvable ou non autorisée." });

            if (request.Title != null)
            {
                var title = request.Title.Trim();
                if (title.Length is < 3 or > 255)
                    return BadRequest(new { success = false, error = "Le titre doit contenir entre 3 et 255 caractères." });
                content.Title = title;
            }

            if (request.Description != null)
                content.Description = request.Description.Trim();

            if (request.Status != null)
            {
                if (!AllowedStatuses.Contains(request.Status))
                    return BadRequest(new { success = false, error = "Statut invalide." });
                content.Status = request.Status;
            }

            if (request.DurationMinutes.HasValue)
            {
                if (request.DurationMinutes < 0 || request.DurationMinutes > 6000)
                    return BadRequest(new { success = false, error = "Durée invalide." });
                content.DurationMinutes = request.DurationMinutes.Value;
            }

            content.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                data = new { content.Id, content.Title, content.Description, content.Status, content.DurationMinutes, content.UpdatedAt },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating content {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Suppression logique : la publication passe en "archived".</summary>
    [HttpDelete("contents/{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id, [FromQuery] bool permanent = false)
    {
        try
        {
            var teacherId = User.GetUserId();

            var content = await _db.CourseContents
                .FirstOrDefaultAsync(c => c.Id == id && c.CreatedByUserId == teacherId);
            if (content == null) return NoContent();

            if (permanent)
            {
                var hasSales = await _db.OrderItems.AnyAsync(oi => oi.SubjectId == content.SubjectId);
                if (hasSales)
                    return BadRequest(new { success = false, error = "Cette publication a déjà été vendue : elle ne peut être qu'archivée." });
                _db.CourseContents.Remove(content);
            }
            else
            {
                content.Status = "archived";
                content.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting content {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Insights construits à partir des ventes et téléchargements réels.
    /// Renvoie une liste vide tant qu'il n'y a pas assez d'activité.
    /// </summary>
    [HttpGet("insights")]
    public async Task<IActionResult> GetInsights()
    {
        try
        {
            var teacherId = User.GetUserId();
            var insights = new List<object>();

            var subjectIds = await _db.CourseContents.AsNoTracking()
                .Where(c => c.CreatedByUserId == teacherId)
                .Select(c => c.SubjectId)
                .Distinct()
                .ToListAsync();

            if (subjectIds.Count == 0)
                return Ok(new { data = insights, success = true });

            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            // Meilleure vente du mois
            var best = await _db.OrderItems.AsNoTracking()
                .Where(oi => subjectIds.Contains(oi.SubjectId) && oi.Order.CreatedAt >= monthStart)
                .GroupBy(oi => oi.SubjectId)
                .Select(g => new { subjectId = g.Key, revenue = g.Sum(x => x.PriceAtPurchase), sales = g.Count() })
                .OrderByDescending(x => x.revenue)
                .FirstOrDefaultAsync();

            if (best != null)
            {
                var title = await _db.Subjects.Where(s => s.Id == best.subjectId).Select(s => s.Title).FirstOrDefaultAsync();
                insights.Add(new
                {
                    type  = "best_seller",
                    icon  = "flame",
                    text  = $"{title}  meilleure vente ce mois ({best.sales} vente(s), {best.revenue:N0} XAF)"
                });
            }

            // Tendance des téléchargements sur 7 jours
            var weekStart = DateTime.UtcNow.Date.AddDays(-7);
            var prevWeekStart = DateTime.UtcNow.Date.AddDays(-14);

            var thisWeek = await _db.DownloadHistories.AsNoTracking()
                .CountAsync(d => subjectIds.Contains(d.SubjectId) && d.CreatedAt >= weekStart);
            var lastWeek = await _db.DownloadHistories.AsNoTracking()
                .CountAsync(d => subjectIds.Contains(d.SubjectId) && d.CreatedAt >= prevWeekStart && d.CreatedAt < weekStart);

            if (thisWeek + lastWeek > 0)
            {
                var delta = lastWeek == 0 ? 100 : (int)Math.Round((thisWeek - lastWeek) * 100d / lastWeek);
                insights.Add(new
                {
                    type = "downloads_trend",
                    icon = delta >= 0 ? "trending-up" : "trending-down",
                    text = $"{(delta >= 0 ? "+" : "")}{delta}% de téléchargements cette semaine ({thisWeek} vs {lastWeek})"
                });
            }

            // Niveau dominant de l'audience
            var audience = await _db.Enrollments.AsNoTracking()
                .Where(e => subjectIds.Contains(e.SubjectId) && e.User != null && e.User.Level != null)
                .GroupBy(e => e.User!.Level!)
                .Select(g => new { level = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync();

            if (audience.Count > 0)
            {
                var total = audience.Sum(a => a.count);
                var top = audience.First();
                insights.Add(new
                {
                    type = "audience",
                    icon = "users",
                    text = $"{top.level} représente {(int)Math.Round(top.count * 100d / total)}% de votre audience"
                });
            }

            return Ok(new { data = insights, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building teacher insights");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Commission réelle issue du plan souscrit (70/75/80%).</summary>
    [HttpGet("revenue-share")]
    public async Task<IActionResult> GetRevenueShare()
    {
        try
        {
            var teacherId = User.GetUserId();
            var share = await GetRevenueShareAsync(teacherId);

            var plan = await _db.Subscriptions.AsNoTracking()
                .Where(s => s.UserId == teacherId && s.Status == "active" && !s.IsDeleted)
                .OrderByDescending(s => s.StartDate)
                .Select(s => s.PricingPlan != null ? s.PricingPlan.Name : null)
                .FirstOrDefaultAsync();

            return Ok(new
            {
                data = new
                {
                    planName        = plan,
                    teacherShare    = share,
                    platformShare   = share.HasValue ? 1m - share.Value : (decimal?)null
                },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting revenue share");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Part enseignant lue sur le plan actif. Null si aucun plan : le frontend
    /// affiche alors un état vide au lieu d'inventer un pourcentage.
    /// </summary>
    private async Task<decimal?> GetRevenueShareAsync(int teacherId) =>
        await _db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == teacherId && s.Status == "active" && !s.IsDeleted)
            .OrderByDescending(s => s.StartDate)
            .Select(s => s.PricingPlan != null ? s.PricingPlan.TeacherRevenueShare : null)
            .FirstOrDefaultAsync();
}
