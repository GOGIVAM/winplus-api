using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
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
    private readonly ILogger<TeacherController> _logger;
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;

    public TeacherController(ITeacherService teacherService, ILogger<TeacherController> logger, ApplicationDbContext db, IHttpClientFactory httpClientFactory)
    {
        _teacherService = teacherService;
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
    public async Task<IActionResult> GetContents([FromQuery] int teacherId, [FromQuery] int limit = 50)
    {
        try
        {
            var contents = await _teacherService.GetTeacherContentsAsync(teacherId, limit);
            return Ok(new { data = contents, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher contents");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les étudiants récents du professeur
    /// </summary>
    /// <param name="teacherId">ID du professeur</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Étudiants récents</returns>
    [HttpGet("students/recent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetRecentStudents([FromQuery] int teacherId, [FromQuery] int limit = 10)
    {
        try
        {
            var students = await _teacherService.GetTeacherStudentsAsync(teacherId, limit);
            return Ok(new { data = students, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher students");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les corrections en attente
    /// </summary>
    /// <param name="teacherId">ID du professeur</param>
    /// <returns>Corrections en attente</returns>
    [HttpGet("corrections/pending")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPendingCorrections([FromQuery] int teacherId)
    {
        try
        {
            var corrections = await _teacherService.GetPendingCorrectionsAsync(teacherId);
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
    public async Task<IActionResult> GetUpcomingSessions([FromQuery] int teacherId, [FromQuery] int limit = 10)
    {
        try
        {
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
    public async Task<IActionResult> GetAvailableQuizzes([FromQuery] int teacherId, [FromQuery] int limit = 10)
    {
        try
        {
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
    public async Task<IActionResult> GetAvailableRevisions([FromQuery] int teacherId, [FromQuery] int limit = 10)
    {
        try
        {
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
    public async Task<IActionResult> GetStats([FromQuery] int teacherId)
    {
        try
        {
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
    public async Task<IActionResult> GetRevenues([FromQuery] int teacherId)
    {
        try
        {
            var revenues = await _teacherService.GetTeacherRevenuesAsync(teacherId);
            return Ok(new { data = revenues, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher revenues");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
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
    public async Task<IActionResult> GetPublications([FromQuery] int teacherId, [FromQuery] int limit = 50)
    {
        try
        {
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
