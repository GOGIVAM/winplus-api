using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Services;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Models.Entities;

namespace Backend.Controllers;

public record AddChildRequest(string Email);

/// <summary>
/// Controller pour les fonctionnalités des parents
/// </summary>
[ApiController]
[Route("api/parent")]
[Produces("application/json")]
[Authorize]
public class ParentController : ControllerBase
{
    private readonly IParentService _parentService;
    private readonly ILogger<ParentController> _logger;
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;

    public ParentController(IParentService parentService, ILogger<ParentController> logger, ApplicationDbContext db, IHttpClientFactory httpClientFactory)
    {
        _parentService = parentService;
        _logger = logger;
        _db = db;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Récupère les statistiques de l'enfant
    /// </summary>
    /// <param name="parentId">ID du parent</param>
    /// <param name="childId">ID de l'enfant</param>
    /// <returns>Statistiques de l'enfant</returns>
    [HttpGet("children/{childId:int}/stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetChildStats([FromRoute] int childId)
    {
        try
        {
            var parentId = User.GetUserId();
            var linked = await _db.ParentStudentLinks.AnyAsync(l => l.ParentId == parentId && l.StudentId == childId);
            if (!linked) return Forbid();

            var now = DateTime.UtcNow;
            var weekStart = now.AddDays(-(int)now.DayOfWeek);

            var downloadsThisWeek = await _db.DownloadHistories
                .CountAsync(d => d.UserId == childId && d.CreatedAt >= weekStart);

            var quizzesThisWeek = await _db.QuizAttempts
                .CountAsync(a => a.UserId == childId && a.CompletedAt >= weekStart);

            var avgScore = await _db.QuizAttempts
                .Where(a => a.UserId == childId && a.CompletedAt >= now.AddDays(-30))
                .AverageAsync(a => (double?)a.Score) ?? 0;

            return Ok(new
            {
                downloadsThisWeek,
                quizzesThisWeek,
                averageScore = Math.Round(avgScore, 1),
                aiSessionsThisWeek = 0,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting child stats");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les activités récentes de l'enfant
    /// </summary>
    /// <param name="parentId">ID du parent</param>
    /// <param name="childId">ID de l'enfant</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Activités récentes</returns>
    [HttpGet("activities/recent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetRecentActivities([FromQuery] int parentId, [FromQuery] int childId, [FromQuery] int limit = 10)
    {
        try
        {
            var activities = await _parentService.GetChildActivitiesAsync(parentId, childId, limit);
            return Ok(new { data = activities, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent activities");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les paiements à venir
    /// </summary>
    /// <param name="parentId">ID du parent</param>
    /// <returns>Paiements à venir</returns>
    [HttpGet("payments/upcoming")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetUpcomingPayments([FromQuery] int parentId)
    {
        try
        {
            var payments = await _parentService.GetUpcomingPaymentsAsync(parentId);
            return Ok(new { data = payments, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting upcoming payments");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les événements à venir
    /// </summary>
    /// <param name="parentId">ID du parent</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Événements à venir</returns>
    [HttpGet("events/upcoming")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetUpcomingEvents([FromQuery] int parentId, [FromQuery] int limit = 10)
    {
        try
        {
            var events = await _parentService.GetUpcomingEventsAsync(parentId, limit);
            return Ok(new { data = events, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting upcoming events");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les quizzes de l'enfant
    /// </summary>
    /// <param name="parentId">ID du parent</param>
    /// <param name="childId">ID de l'enfant</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Quizzes de l'enfant</returns>
    [HttpGet("quizzes/available")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAvailableQuizzes([FromQuery] int parentId, [FromQuery] int childId, [FromQuery] int limit = 10)
    {
        try
        {
            var quizzes = await _parentService.GetChildQuizzesAsync(parentId, childId, limit);
            return Ok(new { data = quizzes, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available quizzes");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les révisions de l'enfant
    /// </summary>
    /// <param name="parentId">ID du parent</param>
    /// <param name="childId">ID de l'enfant</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Révisions de l'enfant</returns>
    [HttpGet("revisions/available")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAvailableRevisions([FromQuery] int parentId,[FromQuery] int childId, [FromQuery] int limit = 10)
    {
        try
        {
            var revisions = await _parentService.GetChildRevisionsAsync(parentId, childId, limit);
            return Ok(new { data = revisions, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available revisions");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère le profil du parent
    /// </summary>
    [HttpGet("profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            var parentId = User.GetUserId();
            var profile = await _parentService.GetParentProfileAsync(parentId);
            return Ok(new { data = profile, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting parent profile");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les objectifs d'un enfant
    /// </summary>
    [HttpGet("children/{childId}/goals")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetChildGoals([FromRoute] int childId, [FromQuery] int parentId)
    {
        try
        {
            var goals = await _parentService.GetChildGoalsAsync(parentId, childId);
            return Ok(new { data = goals, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting child goals");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les enfants du parent via la table ParentStudentLinks
    /// </summary>
    [HttpGet("children")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetChildren()
    {
        try
        {
            var parentId = User.GetUserId();
            var children = await _db.ParentStudentLinks
                .Where(l => l.ParentId == parentId)
                .Include(l => l.Student)
                .Select(l => new
                {
                    id         = l.StudentId,
                    firstName  = l.Student != null ? l.Student.FirstName : null,
                    lastName   = l.Student != null ? l.Student.LastName  : null,
                    email      = l.Student != null ? l.Student.Email     : null,
                    level      = l.Student != null ? l.Student.Level     : null,
                    avatarUrl  = l.Student != null ? l.Student.AvatarUrl : null,
                    schoolName = (string?)null,
                    linkedAt   = l.CreatedAt
                })
                .ToListAsync();

            return Ok(children);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting children for parent");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les analytiques d'un enfant  vérifie que l'enfant appartient bien au parent
    /// </summary>
    [HttpGet("analytics/{childId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetChildAnalytics([FromRoute] int childId)
    {
        try
        {
            var parentId = User.GetUserId();

            var linked = await _db.ParentStudentLinks
                .AnyAsync(l => l.ParentId == parentId && l.StudentId == childId);

            if (!linked)
                return StatusCode(403, new { success = false, error = "Accès refusé : cet enfant n'est pas lié à votre compte" });

            var stats = await _parentService.GetChildStatsAsync(parentId, childId);
            return Ok(new { data = stats, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting child analytics");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les messages reçus par le parent (messagerie directe)
    /// </summary>
    [HttpGet("messages")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetMessages([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var parentId = User.GetUserId();
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var messages = await _db.DirectMessages
                .Where(m => m.ToUserId == parentId)
                .OrderByDescending(m => m.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(m => new
                {
                    m.Id,
                    m.Content,
                    m.IsRead,
                    m.CreatedAt,
                    m.ReadAt,
                    from = m.From == null ? null : new
                    {
                        id        = m.FromUserId,
                        firstName = m.From.FirstName,
                        lastName  = m.From.LastName,
                        avatarUrl = m.From.AvatarUrl
                    }
                })
                .ToListAsync();

            var unreadCount = await _db.DirectMessages
                .CountAsync(m => m.ToUserId == parentId && !m.IsRead);

            return Ok(new { data = messages, unreadCount, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting messages for parent");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// GET /api/parent/engagement/{parentId}
    /// Proxy → Python /api/parent-engagement/{parentId}  Score de mobilisation parental
    /// </summary>
    [HttpGet("engagement/{parentId:int}")]
    public async Task<IActionResult> GetEngagementScore([FromRoute] int parentId, CancellationToken ct)
    {
        var httpClient = _httpClientFactory.CreateClient("FastApiClient");
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/parent-engagement/{parentId}");
        var auth = HttpContext.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(auth)) req.Headers.TryAddWithoutValidation("Authorization", auth);
        var res = await httpClient.SendAsync(req, ct);
        var content = await res.Content.ReadAsStringAsync(ct);
        return Content(content, "application/json");
    }

    /// <summary>
    /// POST /api/parent/educational-roi
    /// Proxy → Python /api/parent/educational-roi  ROI éducatif
    /// </summary>
    [HttpPost("educational-roi")]
    public async Task<IActionResult> GetEducationalROI([FromBody] object body, CancellationToken ct)
    {
        var httpClient = _httpClientFactory.CreateClient("FastApiClient");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/parent/educational-roi");
        req.Content = System.Net.Http.Json.JsonContent.Create(body);
        var auth = HttpContext.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(auth)) req.Headers.TryAddWithoutValidation("Authorization", auth);
        var res = await httpClient.SendAsync(req, ct);
        var content = await res.Content.ReadAsStringAsync(ct);
        return Content(content, "application/json");
    }

    /// <summary>
    /// GET /api/parent/children-insights
    /// Proxy → Python /api/parent/children-insights  Comparaison inter-enfants
    /// </summary>
    [HttpGet("children-insights")]
    public async Task<IActionResult> GetChildrenInsights([FromQuery] string childIds, CancellationToken ct)
    {
        var httpClient = _httpClientFactory.CreateClient("FastApiClient");
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/parent/children-insights?child_ids={Uri.EscapeDataString(childIds ?? "")}");
        var auth = HttpContext.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(auth)) req.Headers.TryAddWithoutValidation("Authorization", auth);
        var res = await httpClient.SendAsync(req, ct);
        var content = await res.Content.ReadAsStringAsync(ct);
        return Content(content, "application/json");
    }

    /// <summary>
    /// PUT /api/parent/settings
    /// Mise à jour des préférences du parent (rapport hebdomadaire, comparaison enfants…)
    /// </summary>
    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] ParentSettingsDto settings)
    {
        try
        {
            var parentId = User.GetUserId();
            var parent = await _db.Users.FindAsync(parentId);
            if (parent == null) return NotFound(new { message = "Parent not found" });

            // Store settings in Bio field as JSON metadata (lightweight settings store)
            System.Collections.Generic.Dictionary<string, object?> meta;
            try { meta = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object?>>(parent.Bio ?? "{}") ?? new(); }
            catch { meta = new(); }

            if (settings.WeeklyReport.HasValue) meta["weeklyReport"] = settings.WeeklyReport.Value;
            if (settings.ChildrenComparison.HasValue) meta["childrenComparison"] = settings.ChildrenComparison.Value;

            // Only write back if Bio looks like a settings JSON (not a real bio string)
            if (parent.Bio == null || parent.Bio.TrimStart().StartsWith("{"))
                parent.Bio = System.Text.Json.JsonSerializer.Serialize(meta);

            await _db.SaveChangesAsync();
            return Ok(new { message = "Settings updated", settings = meta });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating parent settings");
            return StatusCode(500, new { message = "An error occurred" });
        }
    }

    /// <summary>POST /api/parent/children — lier un enfant au parent par email.</summary>
    [HttpPost("children")]
    public async Task<IActionResult> AddChild([FromBody] AddChildRequest req)
    {
        try
        {
            var parentId = User.GetUserId();
            var student = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email && u.Role == "student");
            if (student == null) return NotFound(new { error = "Aucun élève trouvé avec cet email" });

            var already = await _db.ParentStudentLinks.AnyAsync(l => l.ParentId == parentId && l.StudentId == student.Id);
            if (already) return Conflict(new { error = "Cet enfant est déjà lié à votre compte" });

            _db.ParentStudentLinks.Add(new ParentStudentLink { ParentId = parentId, StudentId = student.Id });
            await _db.SaveChangesAsync();
            return Ok(new { success = true, studentId = student.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding child");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>DELETE /api/parent/children/{childId} — délier un enfant.</summary>
    [HttpDelete("children/{childId:int}")]
    public async Task<IActionResult> RemoveChild([FromRoute] int childId)
    {
        try
        {
            var parentId = User.GetUserId();
            var link = await _db.ParentStudentLinks.FirstOrDefaultAsync(l => l.ParentId == parentId && l.StudentId == childId);
            if (link == null) return NotFound(new { error = "Lien non trouvé" });
            _db.ParentStudentLinks.Remove(link);
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing child");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>GET /api/parent/children/{childId}/activity — activités récentes de l'enfant.</summary>
    [HttpGet("children/{childId:int}/activity")]
    public async Task<IActionResult> GetChildActivity([FromRoute] int childId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var parentId = User.GetUserId();
            var linked = await _db.ParentStudentLinks.AnyAsync(l => l.ParentId == parentId && l.StudentId == childId);
            if (!linked) return Forbid();

            if (pageSize > 50) pageSize = 50;

            var downloads = await _db.DownloadHistories
                .AsNoTracking()
                .Where(d => d.UserId == childId)
                .Include(d => d.Subject)
                .OrderByDescending(d => d.CreatedAt)
                .Take(pageSize)
                .Select(d => new
                {
                    type = "download",
                    description = d.Subject != null ? d.Subject.Title : d.FileName ?? "Téléchargement",
                    occurredAt = d.CreatedAt,
                    score = (int?)null,
                })
                .ToListAsync();

            var quizzes = await _db.QuizAttempts
                .AsNoTracking()
                .Where(a => a.UserId == childId)
                .Include(a => a.Quiz)
                .OrderByDescending(a => a.CompletedAt)
                .Take(pageSize)
                .Select(a => new
                {
                    type = "quiz",
                    description = a.Quiz != null ? a.Quiz.Title : "Quiz",
                    occurredAt = a.CompletedAt,
                    score = (int?)((int)a.Score),
                })
                .ToListAsync();

            var activity = downloads.Cast<object>()
                .Concat(quizzes.Cast<object>())
                .OrderByDescending(x => (DateTime)((dynamic)x).occurredAt)
                .Take(pageSize)
                .ToList();

            return Ok(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting child activity");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>GET /api/parent/alerts — alertes WinAI pour tous les enfants du parent.</summary>
    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts()
    {
        try
        {
            var parentId = User.GetUserId();
            var childIds = await _db.ParentStudentLinks
                .Where(l => l.ParentId == parentId)
                .Select(l => l.StudentId)
                .ToListAsync();

            var alerts = await _db.Notifications
                .AsNoTracking()
                .Where(n => n.UserId == parentId && n.Type == "ParentAlert")
                .OrderByDescending(n => n.CreatedAt)
                .Take(30)
                .Select(n => new
                {
                    id = n.Id,
                    type = n.RelatedEntityType ?? "tip",
                    message = n.Message,
                    createdAt = n.CreatedAt,
                    isRead = n.IsRead,
                    childId = n.RelatedEntityId,
                    childName = (string?)null,
                })
                .ToListAsync();

            return Ok(alerts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting parent alerts");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>PUT /api/parent/alerts/{id}/read — marquer une alerte comme lue.</summary>
    [HttpPut("alerts/{id:int}/read")]
    public async Task<IActionResult> MarkAlertRead([FromRoute] int id)
    {
        try
        {
            var parentId = User.GetUserId();
            var notif = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == parentId);
            if (notif == null) return NotFound(new { error = "Alerte introuvable" });
            notif.IsRead = true;
            notif.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking alert read");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// GET /api/parent/ai-alerts/{childId}
    /// Proxy vers Python /api/parent-alerts/{childId}  détection d'anomalies + messages WinAI
    /// </summary>
    [HttpGet("ai-alerts/{childId:int}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetAIAlerts([FromRoute] int childId)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient("FastApiClient");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/parent-alerts/{childId}");
            var auth = HttpContext.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(auth))
                req.Headers.TryAddWithoutValidation("Authorization", auth);

            var res = await httpClient.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogError("Python parent-alerts returned {Status}: {Body}", res.StatusCode, content);
                return StatusCode((int)res.StatusCode, new { message = "AI alert service unavailable" });
            }

            return Content(content, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching AI alerts for child {ChildId}", childId);
            return StatusCode(502, new { message = "Could not reach AI service" });
        }
    }
}
