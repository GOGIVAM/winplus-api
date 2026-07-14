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
        var httpClient = _httpClientFactory.CreateClient("FastApiClient");
        var body = new
        {
            user_id      = userId,
            exam_type    = request.ExamType,
            exam_date    = request.ExamDate.ToString("yyyy-MM-dd"),
            hours_per_day = request.HoursPerDay
        };

        JsonElement planJson;
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
                return StatusCode((int)res.StatusCode, new { message = "AI plan generation failed", detail = errorBody });
            }

            var responseText = await res.Content.ReadAsStringAsync();
            var fullResponse = JsonSerializer.Deserialize<JsonElement>(responseText);
            // Python wraps responses in { success, data } — extract inner plan
            planJson = fullResponse.TryGetProperty("data", out var dataEl) ? dataEl : fullResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Python exam-coach/generate");
            return StatusCode(502, new { message = "Could not reach AI service" });
        }

        // Extract confidence score from inner plan data
        float confidenceScore = 0f;
        if (planJson.TryGetProperty("confidence_score", out var cs))
            confidenceScore = cs.GetSingle();

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
