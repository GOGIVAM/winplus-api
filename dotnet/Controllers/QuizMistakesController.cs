using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;

namespace Backend.Controllers;

public class RecordMistakeItem
{
    public string? Question { get; set; }
    public string? GivenAnswer { get; set; }
    public string? CorrectAnswer { get; set; }
    public string? Subject { get; set; }
}

public class RecordMistakesRequest
{
    public int? QuizId { get; set; }
    public int? QuizAttemptId { get; set; }
    public List<RecordMistakeItem> Mistakes { get; set; } = new();
}

/// <summary>
/// Questions ratées et quiz de révision (S7-4).
/// GET  /api/quiz/mistakes?subject=chimie
/// POST /api/quiz/mistakes            → enregistré à la fin d'une tentative
/// POST /api/quiz/mistakes/{id}/resolve
/// GET  /api/quiz/mistakes/subjects   → matières réellement concernées
/// </summary>
[ApiController]
[Route("api/quiz/mistakes")]
[Authorize]
public class QuizMistakesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<QuizMistakesController> _logger;

    public QuizMistakesController(ApplicationDbContext db, ILogger<QuizMistakesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? subject, [FromQuery] int limit = 50)
    {
        try
        {
            if (limit < 1 || limit > 200) limit = 50;
            var userId = User.GetUserId();

            var query = _db.QuizMistakes.AsNoTracking()
                .Where(m => m.UserId == userId && !m.IsResolved);

            if (!string.IsNullOrWhiteSpace(subject) && subject != "Toutes")
                query = query.Where(m => m.Subject == subject);

            var items = await query
                .OrderByDescending(m => m.CreatedAt)
                .Take(limit)
                .Select(m => new
                {
                    m.Id, m.QuizId, m.Subject, m.Question,
                    m.GivenAnswer, m.CorrectAnswer, m.CreatedAt,
                    quizTitle = m.Quiz != null ? m.Quiz.Title : null
                })
                .ToListAsync();

            return Ok(new { data = items, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting quiz mistakes");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Matières où l'élève a réellement des questions à revoir.</summary>
    [HttpGet("subjects")]
    public async Task<IActionResult> GetSubjects()
    {
        try
        {
            var userId = User.GetUserId();
            var subjects = await _db.QuizMistakes.AsNoTracking()
                .Where(m => m.UserId == userId && !m.IsResolved && m.Subject != null)
                .GroupBy(m => m.Subject!)
                .Select(g => new { subject = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync();

            return Ok(new { data = subjects, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting mistake subjects");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Record([FromBody] RecordMistakesRequest request)
    {
        try
        {
            if (request.Mistakes.Count == 0)
                return BadRequest(new { success = false, error = "Aucune question fournie." });

            var userId = User.GetUserId();

            var quizSubject = request.QuizId.HasValue
                ? await _db.Quizzes.Where(q => q.Id == request.QuizId).Select(q => q.Subject).FirstOrDefaultAsync()
                : null;

            var added = 0;
            foreach (var item in request.Mistakes)
            {
                var question = (item.Question ?? "").Trim();
                if (question.Length == 0) continue;
                if (question.Length > 1000) question = question[..1000];

                // Idempotence : une même question non résolue n'est pas dupliquée.
                var exists = await _db.QuizMistakes.AnyAsync(
                    m => m.UserId == userId && m.Question == question && !m.IsResolved);
                if (exists) continue;

                _db.QuizMistakes.Add(new QuizMistake
                {
                    UserId        = userId,
                    QuizId        = request.QuizId,
                    QuizAttemptId = request.QuizAttemptId,
                    Subject       = item.Subject ?? quizSubject,
                    Question      = question,
                    GivenAnswer   = item.GivenAnswer,
                    CorrectAnswer = item.CorrectAnswer
                });
                added++;
            }

            if (added > 0) await _db.SaveChangesAsync();

            return Ok(new { data = new { recorded = added }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording quiz mistakes");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPost("{id:int}/resolve")]
    public async Task<IActionResult> Resolve([FromRoute] int id)
    {
        try
        {
            var userId = User.GetUserId();
            var mistake = await _db.QuizMistakes.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
            if (mistake == null) return NotFound(new { success = false, error = "Question introuvable." });

            mistake.IsResolved = true;
            mistake.ResolvedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new { data = new { mistake.Id, mistake.IsResolved }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving quiz mistake {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
