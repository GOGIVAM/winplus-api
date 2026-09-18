using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Backend.Data;
using Backend.Models.Entities;

namespace Backend.Controllers;

[ApiController]
[Route("api/exam-coach")]
[Authorize]
public class ExamCoachController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ExamCoachController> _logger;

    public ExamCoachController(
        ApplicationDbContext db,
        IHttpClientFactory httpClientFactory,
        ILogger<ExamCoachController> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst("sub") ?? User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim == null || !int.TryParse(claim.Value, out var id))
            throw new UnauthorizedAccessException("Invalid user token");
        return id;
    }

    // POST /api/exam-coach
    [HttpPost]
    public async Task<IActionResult> CreatePlan([FromBody] CreateExamCoachPlanRequest request)
    {
        int userId;
        try { userId = GetCurrentUserId(); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }

        // Deactivate any existing active plan for this user
        var existingPlans = await _db.ExamCoachPlans
            .Where(p => p.UserId == userId && p.IsActive)
            .ToListAsync();

        foreach (var existing in existingPlans)
            existing.IsActive = false;

        // Call Python FastAPI to generate the plan
        var (planJson, confidenceScore, genError) = await GeneratePlanFromPythonAsync(
            userId, request.ExamType, request.ExamDate, request.HoursPerDay);
        if (genError != null) return genError;

        var plan = new ExamCoachPlan
        {
            UserId          = userId,
            ExamType        = request.ExamType,
            ExamDate        = request.ExamDate,
            HoursPerDay     = request.HoursPerDay,
            PlanJson        = planJson.GetRawText(),
            ConfidenceScore = confidenceScore,
            IsActive        = true,
            CreatedAt       = DateTime.UtcNow
        };

        _db.ExamCoachPlans.Add(plan);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            id            = plan.Id,
            userId        = plan.UserId,
            examType      = plan.ExamType,
            examDate      = plan.ExamDate.ToString("yyyy-MM-dd"),
            hoursPerDay   = plan.HoursPerDay,
            confidenceScore = plan.ConfidenceScore,
            createdAt     = plan.CreatedAt.ToString("o"),
            isActive      = plan.IsActive,
            planJson      = plan.PlanJson,
            completedDays = Array.Empty<object>()
        });
    }

    // GET /api/exam-coach/active
    [HttpGet("active")]
    public async Task<IActionResult> GetActivePlan()
    {
        int userId;
        try { userId = GetCurrentUserId(); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }

        var plan = await _db.ExamCoachPlans
            .Include(p => p.DayCompletions)
            .FirstOrDefaultAsync(p => p.UserId == userId && p.IsActive);

        if (plan == null)
            return NotFound(new { message = "No active exam coach plan found" });

        return Ok(new
        {
            id              = plan.Id,
            userId          = plan.UserId,
            examType        = plan.ExamType,
            examDate        = plan.ExamDate.ToString("yyyy-MM-dd"),
            hoursPerDay     = plan.HoursPerDay,
            confidenceScore = plan.ConfidenceScore,
            createdAt       = plan.CreatedAt.ToString("o"),
            lastRecalibratedAt = plan.LastRecalibratedAt?.ToString("o"),
            isActive        = plan.IsActive,
            planJson        = plan.PlanJson,
            completedDays   = plan.DayCompletions.Select(d => new
            {
                dayNumber   = d.DayNumber,
                completedAt = d.CompletedAt.ToString("o"),
                quizScore   = d.QuizScore
            })
        });
    }

    // PUT /api/exam-coach/{id}/complete-day
    [HttpPut("{id}/complete-day")]
    public async Task<IActionResult> CompleteDay([FromRoute] int id, [FromBody] CompleteDayRequest request)
    {
        int userId;
        try { userId = GetCurrentUserId(); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }

        var plan = await _db.ExamCoachPlans
            .Include(p => p.DayCompletions)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (plan == null)
            return NotFound(new { message = "Plan not found" });

        if (plan.UserId != userId)
            return Forbid();

        // Check for duplicate day completion
        var alreadyCompleted = plan.DayCompletions.Any(d => d.DayNumber == request.DayNumber);
        if (alreadyCompleted)
            return Conflict(new { message = $"Day {request.DayNumber} has already been marked as completed" });

        var completion = new ExamCoachDayCompletion
        {
            PlanId      = id,
            DayNumber   = request.DayNumber,
            QuizScore   = request.QuizScore,
            CompletedAt = DateTime.UtcNow
        };

        _db.ExamCoachDayCompletions.Add(completion);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            completion.Id,
            completion.PlanId,
            completion.DayNumber,
            completion.QuizScore,
            completion.CompletedAt
        });
    }

    // DELETE /api/exam-coach/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePlan([FromRoute] int id)
    {
        int userId;
        try { userId = GetCurrentUserId(); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }

        var plan = await _db.ExamCoachPlans.FirstOrDefaultAsync(p => p.Id == id);

        if (plan == null)
            return NotFound(new { message = "Plan not found" });

        if (plan.UserId != userId)
            return Forbid();

        plan.IsActive = false;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Plan deactivated successfully" });
    }

    // ── Mode veille d'examen (parent) ──────────────────────────────────────
    // Ajoute un état d'attention parentale sur le plan actif de l'enfant, sans
    // dupliquer ExamCoachPlan : ParentWatchModeActivatedAt réutilise l'entité
    // existante. La désactivation automatique (ExamDate dépassée) est gérée
    // par ExamWatchModeExpirationService, pas ici.

    /// <summary>
    /// Active la veille d'examen pour un enfant lié. Réutilise le plan actif
    /// existant s'il y en a un ; sinon en crée un (mêmes paramètres que
    /// CreatePlan, appel à l'IA Python) avant d'y activer la veille.
    /// </summary>
    [HttpPost("{childId:int}/watch-mode")]
    [Authorize(Roles = "parent")]
    public async Task<IActionResult> ActivateWatchMode(int childId, [FromBody] ActivateWatchModeRequest request)
    {
        int parentId;
        try { parentId = GetCurrentUserId(); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }

        var linked = await _db.ParentStudentLinks.AnyAsync(l =>
            l.ParentId == parentId && l.StudentId == childId && l.Status == "accepted");
        if (!linked)
            return StatusCode(403, new { message = "Cet enfant n'est pas lié à votre compte." });

        var existing = await _db.ExamCoachPlans.FirstOrDefaultAsync(p => p.UserId == childId && p.IsActive);
        if (existing != null)
        {
            existing.ParentWatchModeActivatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(ToWatchModeDto(existing));
        }

        if (string.IsNullOrWhiteSpace(request.ExamType) || request.ExamDate == null)
            return BadRequest(new { message = "exam_type et exam_date sont requis : aucun plan actif n'existe pour cet enfant." });

        var (planJson, confidenceScore, genError) = await GeneratePlanFromPythonAsync(
            childId, request.ExamType, request.ExamDate.Value, request.HoursPerDay);
        if (genError != null) return genError;

        var plan = new ExamCoachPlan
        {
            UserId                     = childId,
            ExamType                   = request.ExamType,
            ExamDate                   = request.ExamDate.Value,
            HoursPerDay                = request.HoursPerDay,
            PlanJson                   = planJson.GetRawText(),
            ConfidenceScore            = confidenceScore,
            IsActive                   = true,
            CreatedAt                  = DateTime.UtcNow,
            ParentWatchModeActivatedAt = DateTime.UtcNow,
        };
        _db.ExamCoachPlans.Add(plan);
        await _db.SaveChangesAsync();

        return Ok(ToWatchModeDto(plan));
    }

    /// <summary>Désactivation manuelle par le parent  remet ParentWatchModeActivatedAt à null.</summary>
    [HttpDelete("{childId:int}/watch-mode")]
    [Authorize(Roles = "parent")]
    public async Task<IActionResult> DeactivateWatchMode(int childId)
    {
        int parentId;
        try { parentId = GetCurrentUserId(); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }

        var linked = await _db.ParentStudentLinks.AnyAsync(l =>
            l.ParentId == parentId && l.StudentId == childId && l.Status == "accepted");
        if (!linked)
            return StatusCode(403, new { message = "Cet enfant n'est pas lié à votre compte." });

        await _db.ExamCoachPlans
            .Where(p => p.UserId == childId && p.IsActive && p.ParentWatchModeActivatedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ParentWatchModeActivatedAt, (DateTime?)null));

        return Ok(new { success = true });
    }

    /// <summary>État actuel de la veille pour un enfant lié : actif/inactif, ExamDate, ExamType.</summary>
    [HttpGet("{childId:int}/watch-mode")]
    [Authorize(Roles = "parent")]
    public async Task<IActionResult> GetWatchMode(int childId)
    {
        int parentId;
        try { parentId = GetCurrentUserId(); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }

        var linked = await _db.ParentStudentLinks.AnyAsync(l =>
            l.ParentId == parentId && l.StudentId == childId && l.Status == "accepted");
        if (!linked)
            return StatusCode(403, new { message = "Cet enfant n'est pas lié à votre compte." });

        var plan = await _db.ExamCoachPlans.AsNoTracking()
            .Where(p => p.UserId == childId && p.IsActive)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        return Ok(plan == null
            ? new { active = false, examType = (string?)null, examDate = (string?)null, parentWatchModeActivatedAt = (string?)null }
            : ToWatchModeDto(plan));
    }

    private static object ToWatchModeDto(ExamCoachPlan plan) => new
    {
        active = plan.ParentWatchModeActivatedAt != null,
        examType = plan.ExamType,
        examDate = plan.ExamDate.ToString("yyyy-MM-dd"),
        parentWatchModeActivatedAt = plan.ParentWatchModeActivatedAt?.ToString("o"),
    };

    /// <summary>Appelle l'IA Python pour générer un plan (factorisé depuis CreatePlan, réutilisé par ActivateWatchMode).</summary>
    private async Task<(JsonElement PlanJson, float ConfidenceScore, IActionResult? Error)> GeneratePlanFromPythonAsync(
        int userId, string examType, DateTime examDate, float hoursPerDay)
    {
        var httpClient = _httpClientFactory.CreateClient("FastApiClient");
        var body = new
        {
            user_id       = userId,
            exam_type     = examType,
            exam_date     = examDate.ToString("yyyy-MM-dd"),
            hours_per_day = hoursPerDay
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/exam-coach/generate");
            req.Content = JsonContent.Create(body);

            var auth = HttpContext.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(auth))
                req.Headers.TryAddWithoutValidation("Authorization", auth);

            var res = await httpClient.SendAsync(req);

            if (!res.IsSuccessStatusCode)
            {
                var errorBody = await res.Content.ReadAsStringAsync();
                _logger.LogError("Python exam-coach/generate returned {Status}: {Body}", res.StatusCode, errorBody);
                return (default, 0f, StatusCode((int)res.StatusCode, new { message = "AI plan generation failed", detail = errorBody }));
            }

            var responseText = await res.Content.ReadAsStringAsync();
            var fullResponse = JsonSerializer.Deserialize<JsonElement>(responseText);
            // Python wraps responses in { success, data }  extract inner plan
            var planJson = fullResponse.TryGetProperty("data", out var dataEl) ? dataEl : fullResponse;

            float confidenceScore = 0f;
            if (planJson.TryGetProperty("confidence_score", out var cs))
                confidenceScore = cs.GetSingle();

            return (planJson, confidenceScore, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Python exam-coach/generate");
            return (default, 0f, StatusCode(502, new { message = "Could not reach AI service" }));
        }
    }
}

// DTOs
public class CreateExamCoachPlanRequest
{
    public string ExamType { get; set; } = string.Empty;
    public DateTime ExamDate { get; set; }
    public float HoursPerDay { get; set; } = 2.0f;
}

public class CompleteDayRequest
{
    public int DayNumber { get; set; }
    public float? QuizScore { get; set; }
}

public class ActivateWatchModeRequest
{
    /// <summary>Requis seulement si l'enfant n'a aucun plan actif à réutiliser.</summary>
    public string? ExamType { get; set; }
    public DateTime? ExamDate { get; set; }
    public float HoursPerDay { get; set; } = 2.0f;
}
