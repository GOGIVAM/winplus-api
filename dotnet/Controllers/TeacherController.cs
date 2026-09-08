using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using Backend.Data;
using Backend.Services;
using Backend.Extensions;

namespace Backend.Controllers;

/// <summary>
/// Controller pour les fonctionnalités des professeurs
/// </summary>
[ApiController]
[Route("api/teacher")]
[Produces("application/json")]
[Authorize]
public class TeacherController : ControllerBase
{
    private readonly ITeacherService _teacherService;
    private readonly IAssignmentService _assignmentService;
    private readonly ILogger<TeacherController> _logger;
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;

    public TeacherController(ITeacherService teacherService, IAssignmentService assignmentService, ILogger<TeacherController> logger, ApplicationDbContext db, IHttpClientFactory httpClientFactory)
    {
        _teacherService = teacherService;
        _assignmentService = assignmentService;
        _logger = logger;
        _db = db;
        _httpClientFactory = httpClientFactory;
    }

    // ── WinAI proxy helpers ──────────────────────────────────────────────────

    private HttpClient PyClient() => _httpClientFactory.CreateClient("FastApiClient");

    private void ForwardAuth(HttpRequestMessage req)
    {
        var auth = HttpContext.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(auth))
            req.Headers.TryAddWithoutValidation("Authorization", auth);
    }

    // ── Feature 2  POST /api/teacher/class-analysis ─────────────────────────

    /// <summary>Analyse collective WinAI des apprenants d'un contenu</summary>
    [HttpPost("class-analysis")]
    public async Task<IActionResult> GetClassAnalysis([FromBody] object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/teacher/class-analysis");
        req.Content = System.Net.Http.Json.JsonContent.Create(body);
        ForwardAuth(req);
        var res = await PyClient().SendAsync(req, ct);
        return Content(await res.Content.ReadAsStringAsync(ct), "application/json");
    }

    // ── Feature 3  GET /api/teacher/content-impact/{contentId} ─────────────

    /// <summary>Score d'impact pédagogique d'un contenu</summary>
    [HttpGet("content-impact/{contentId:int}")]
    public async Task<IActionResult> GetContentImpact([FromRoute] int contentId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/teacher/content-impact/{contentId}");
        ForwardAuth(req);
        var res = await PyClient().SendAsync(req, ct);
        return Content(await res.Content.ReadAsStringAsync(ct), "application/json");
    }

    // ── Module 2, US-CAT-07  GET /api/teacher/recommended-purchases ────────

    /// <summary>WinAI — contenus recommandés à l'achat pour ce professeur</summary>
    [HttpGet("recommended-purchases")]
    public async Task<IActionResult> GetRecommendedPurchases(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/teacher/recommended-purchases");
        ForwardAuth(req);
        var res = await PyClient().SendAsync(req, ct);
        return Content(await res.Content.ReadAsStringAsync(ct), "application/json");
    }

    // ── Module 2, US-CAT-09  GET /api/teacher/editorial-watch ───────────────

    /// <summary>WinAI — veille éditoriale : matières en demande peu couvertes</summary>
    [HttpGet("editorial-watch")]
    public async Task<IActionResult> GetEditorialWatch(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/teacher/editorial-watch");
        ForwardAuth(req);
        var res = await PyClient().SendAsync(req, ct);
        return Content(await res.Content.ReadAsStringAsync(ct), "application/json");
    }

    // ── Feature 4  POST /api/teacher/generate-correction ───────────────────

    /// <summary>Génère une correction IA d'une épreuve</summary>
    [HttpPost("generate-correction")]
    public async Task<IActionResult> GenerateCorrection([FromBody] object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/teacher/generate-correction");
        req.Content = System.Net.Http.Json.JsonContent.Create(body);
        ForwardAuth(req);
        var res = await PyClient().SendAsync(req, ct);
        return Content(await res.Content.ReadAsStringAsync(ct), "application/json");
    }

    // ── Feature 5  POST /api/teacher/predict-popularity ────────────────────

    /// <summary>Prédiction de popularité d'un contenu avant publication</summary>
    [HttpPost("predict-popularity")]
    public async Task<IActionResult> PredictPopularity([FromBody] object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/teacher/predict-popularity");
        req.Content = System.Net.Http.Json.JsonContent.Create(body);
        ForwardAuth(req);
        var res = await PyClient().SendAsync(req, ct);
        return Content(await res.Content.ReadAsStringAsync(ct), "application/json");
    }

    // ── Feature 6  POST /api/teacher/analyze-submission ────────────────────

    /// <summary>Analyse IA d'une soumission d'élève</summary>
    [HttpPost("analyze-submission")]
    public async Task<IActionResult> AnalyzeSubmission([FromBody] object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/teacher/analyze-submission");
        req.Content = System.Net.Http.Json.JsonContent.Create(body);
        ForwardAuth(req);
        var res = await PyClient().SendAsync(req, ct);
        return Content(await res.Content.ReadAsStringAsync(ct), "application/json");
    }

    /// <summary>
    /// Récupère le contenu du professeur
    /// </summary>
    /// <param name="teacherId">ID du professeur</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Contenu du professeur</returns>
    [HttpGet("contents")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetContents([FromQuery] int limit = 50)
    {
        try
        {
            var teacherId = User.GetUserId();
            var contents = await _teacherService.GetTeacherContentsAsync(teacherId, limit);
            return Ok(new { data = contents, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher contents");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("students/recent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetRecentStudents([FromQuery] int limit = 10)
    {
        try
        {
            var teacherId = User.GetUserId();
            var students = await _teacherService.GetTeacherStudentsAsync(teacherId, limit);
            return Ok(new { data = students, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher students");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>File de correction (US-COR-01) — filter: pending (défaut) | corrected | all.</summary>
    [HttpGet("corrections/pending")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPendingCorrections([FromQuery] string filter = "pending")
    {
        try
        {
            var teacherId = User.GetUserId();
            var corrections = await _assignmentService.GetPendingCorrectionsAsync(teacherId, filter);
            return Ok(new { data = corrections, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending corrections");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les sessions à venir
    /// </summary>
    /// <param name="teacherId">ID du professeur</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Sessions à venir</returns>
    [HttpGet("sessions/upcoming")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetUpcomingSessions([FromQuery] int limit = 10)
    {
        try
        {
            var teacherId = User.GetUserId();
            var sessions = await _teacherService.GetUpcomingSessionsAsync(teacherId, limit);
            return Ok(new { data = sessions, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting upcoming sessions");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les quizzes disponibles
    /// </summary>
    /// <param name="teacherId">ID du professeur</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Quizzes disponibles</returns>
    [HttpGet("quizzes/available")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAvailableQuizzes([FromQuery] int limit = 10)
    {
        try
        {
            var teacherId = User.GetUserId();
            var quizzes = await _teacherService.GetTeacherQuizzesAsync(teacherId, limit);
            return Ok(new { data = quizzes, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available quizzes");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les révisions disponibles
    /// </summary>
    /// <param name="teacherId">ID du professeur</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Révisions disponibles</returns>
    [HttpGet("revisions/available")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAvailableRevisions([FromQuery] int limit = 10)
    {
        try
        {
            var teacherId = User.GetUserId();
            var revisions = await _teacherService.GetTeacherRevisionsAsync(teacherId, limit);
            return Ok(new { data = revisions, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available revisions");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les statistiques du professeur
    /// </summary>
    /// <param name="teacherId">ID du professeur</param>
    /// <returns>Statistiques</returns>
    [HttpGet("stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetStats()
    {
        try
        {
            var teacherId = User.GetUserId();
            var stats = await _teacherService.GetTeacherStatsAsync(teacherId);
            return Ok(new { data = stats, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher stats");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère le profil du professeur
    /// </summary>
    [HttpGet("profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            var teacherId = User.GetUserId();
            var profile = await _teacherService.GetTeacherProfileAsync(teacherId);
            return Ok(new { data = profile, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher profile");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les revenus du professeur
    /// </summary>
    [HttpGet("revenues")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetRevenues()
    {
        try
        {
            var teacherId = User.GetUserId();
            var revenues = await _teacherService.GetTeacherRevenuesAsync(teacherId);
            return Ok(new { data = revenues, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher revenues");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Historique journalier des revenus (Catalogue + Cours particuliers, US-REP-10).</summary>
    [HttpGet("revenue/history")]
    public async Task<IActionResult> GetRevenueHistory([FromQuery] int period = 30)
    {
        var teacherId = User.GetUserId();
        return Ok(await _teacherService.GetRevenueHistoryAsync(teacherId, period));
    }

    /// <summary>Ventilation des revenus catalogue par contenu.</summary>
    [HttpGet("revenue/by-content")]
    public async Task<IActionResult> GetRevenueByContent([FromQuery] string sort = "revenue", [FromQuery] string dir = "desc")
    {
        var teacherId = User.GetUserId();
        return Ok(await _teacherService.GetRevenueByContentAsync(teacherId, sort, dir));
    }

    /// <summary>Détail des transactions "cours particuliers" (US-REP-10).</summary>
    [HttpGet("revenue/tutoring-transactions")]
    public async Task<IActionResult> GetTutoringTransactions()
    {
        var teacherId = User.GetUserId();
        return Ok(await _teacherService.GetTutoringTransactionsAsync(teacherId));
    }

    /// <summary>Historique unifié filtrable par source — catalogue / cours_particulier / achat (Module 7, 7B).</summary>
    [HttpGet("revenue/transactions")]
    public async Task<IActionResult> GetTransactions([FromQuery] string? source = null)
    {
        var teacherId = User.GetUserId();
        return Ok(await _teacherService.GetTransactionsAsync(teacherId, source));
    }

    /// <summary>Solde réellement disponible au retrait (revenus - dépenses - déjà retiré), utilisé par le dashboard Revenus et par le flux de retrait (Module 7).</summary>
    [HttpGet("revenue/balance")]
    public async Task<IActionResult> GetRevenueBalance()
    {
        var teacherId = User.GetUserId();
        var balance = await _teacherService.GetSpendableBalanceAsync(teacherId);
        return Ok(new { balance });
    }

    // ── Module 8, 8A — Détection de décrochage ──────────────────────────────
    // Vue transversale toutes formations : CourseInactivityAlertService détecte et
    // persiste les alertes quotidiennement ; ces endpoints les consultent/traitent.

    /// <summary>Alertes de décrochage non traitées, toutes formations du professeur confondues.</summary>
    [HttpGet("alertes-decrochage")]
    public async Task<IActionResult> GetAlertesDecrochage()
    {
        var teacherId = User.GetUserId();
        var raw = await _db.AlertesDecrochage.AsNoTracking()
            .Where(a => !a.Traitee && a.Course.InstructorId == teacherId)
            .Include(a => a.Course)
            .Include(a => a.Student)
            .OrderByDescending(a => a.DateDetection)
            .Select(a => new
            {
                id = a.Id,
                courseId = a.CourseId,
                courseTitle = a.Course.Title,
                studentUserId = a.StudentUserId,
                studentName = (a.Student.FirstName + " " + a.Student.LastName).Trim(),
                studentAvatarUrl = a.Student.AvatarUrl,
                niveau = a.Niveau,
                signauxJson = a.SignauxJson,
                dateDetection = a.DateDetection,
            })
            .ToListAsync();

        // Désérialisation JSON en mémoire — non traduisible en SQL par EF Core.
        var alerts = raw.Select(a => new
        {
            a.id,
            a.courseId,
            a.courseTitle,
            a.studentUserId,
            a.studentName,
            a.studentAvatarUrl,
            a.niveau,
            signaux = System.Text.Json.JsonSerializer.Deserialize<List<string>>(a.signauxJson) ?? new List<string>(),
            a.dateDetection,
        });
        return Ok(alerts);
    }

    /// <summary>Marque l'alerte comme traitée et envoie un message de relance WinAI à l'élève concerné.</summary>
    [HttpPost("alertes/{id:int}/relancer")]
    public async Task<IActionResult> RelancerAlerteDecrochage(int id)
    {
        var teacherId = User.GetUserId();
        var alert = await _db.AlertesDecrochage.Include(a => a.Course)
            .FirstOrDefaultAsync(a => a.Id == id && a.Course.InstructorId == teacherId);
        if (alert == null) return NotFound(new { error = "Alerte introuvable" });
        if (alert.Traitee) return Ok(new { alreadyTreated = true });

        string messageText;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/teacher/inactivity-relaunch-message")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { course_title = alert.Course.Title }),
            };
            ForwardAuth(req);
            var res = await PyClient().SendAsync(req);
            var json = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            messageText = json.TryGetProperty("message_text", out var m) ? m.GetString() ?? "" : "";
        }
        catch { messageText = ""; }
        if (string.IsNullOrWhiteSpace(messageText))
            messageText = $"On continue « {alert.Course.Title} » ? Reprends où tu t'es arrêté(e), je suis là si tu as des questions !";

        _db.DirectMessages.Add(new Backend.Models.Entities.DirectMessage { FromUserId = teacherId, ToUserId = alert.StudentUserId, Content = messageText });
        _db.CourseInactivityRelaunches.Add(new Backend.Models.Entities.CourseInactivityRelaunch { CourseId = alert.CourseId, UserId = alert.StudentUserId });
        alert.Traitee = true;
        alert.TraiteeAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { sent = true, message = messageText });
    }

    /// <summary>
    /// Récupère les classes du professeur
    /// </summary>
    [HttpGet("classes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetClasses()
    {
        try
        {
            var teacherId = User.GetUserId();
            var classes = await _db.TeacherClasses
                .Where(c => c.TeacherId == teacherId && c.IsActive)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Level,
                    c.AcademicYear,
                    c.Description,
                    c.StudentCount,
                    c.CreatedAt
                })
                .ToListAsync();
            return Ok(new { data = classes, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher classes");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les publications du professeur
    /// </summary>
    [HttpGet("publications")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPublications([FromQuery] int limit = 50)
    {
        try
        {
            var teacherId = User.GetUserId();
            var contents = await _teacherService.GetTeacherContentsAsync(teacherId, limit);
            return Ok(new { data = contents, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher publications");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
