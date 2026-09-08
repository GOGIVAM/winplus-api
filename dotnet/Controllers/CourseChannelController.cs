using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

public record SendChannelMessageRequest(string Content);
public record SetCanalMessagerieRequest(bool CanalMessagerie);
public record CorrectInteractionRequest(string CorrectedAnswer);

file sealed class CanalQaResponse
{
    public string? Action { get; set; }
    public string? Answer { get; set; }
    public double Confidence { get; set; }
}

/// <summary>
/// Canal de discussion par formation + Q&amp;A automatique WinAI (Module 7, 3C).
/// Distinct de MessagesController (messagerie directe 1-1) : ici, un fil
/// public à tous les inscrits d'une formation, avec réponse automatique
/// WinAI optionnelle basée sur le contenu des leçons.
/// </summary>
[ApiController]
[Route("api/formations/{courseId:int}/canal")]
[Authorize]
public class CourseChannelController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly INtfyService _ntfy;
    private readonly ILogger<CourseChannelController> _logger;
    private const double ConfidenceThreshold = 0.70;

    public CourseChannelController(ApplicationDbContext db, IHttpClientFactory httpClientFactory, INtfyService ntfy, ILogger<CourseChannelController> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _ntfy = ntfy;
        _logger = logger;
    }

    private async Task<Course?> GetAccessibleCourseAsync(int courseId, int userId)
    {
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == courseId);
        if (course == null) return null;
        if (course.InstructorId == userId) return course;
        var enrolled = await _db.CourseEnrollments.AnyAsync(e => e.CourseId == courseId && e.UserId == userId && e.IsActive);
        return enrolled ? course : null;
    }

    /// <summary>Fil de discussion de la formation (inscrits + professeur uniquement).</summary>
    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages(int courseId)
    {
        var me = User.GetUserId();
        var course = await GetAccessibleCourseAsync(courseId, me);
        if (course == null) return Forbid();

        var messages = await _db.CourseChannelMessages
            .AsNoTracking()
            .Where(m => m.CourseId == courseId)
            .Include(m => m.Sender)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new
            {
                id = m.Id,
                content = m.Content,
                isAiGenerated = m.IsAiGenerated,
                aiConfidence = m.AiConfidence,
                taggedProfessor = m.TaggedProfessor,
                senderId = m.SenderUserId,
                senderName = m.Sender != null ? $"{m.Sender.FirstName} {m.Sender.LastName}".Trim() : null,
                isFromMe = m.SenderUserId == me,
                createdAt = m.CreatedAt,
            })
            .ToListAsync();

        return Ok(messages);
    }

    /// <summary>Poste un message dans le canal ; déclenche le Q&amp;A WinAI si activé et si l'auteur n'est pas le professeur.</summary>
    [HttpPost("messages")]
    public async Task<IActionResult> SendMessage(int courseId, [FromBody] SendChannelMessageRequest req)
    {
        var me = User.GetUserId();
        var course = await GetAccessibleCourseAsync(courseId, me);
        if (course == null) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Content)) return BadRequest(new { message = "Message vide." });

        var msg = new CourseChannelMessage { CourseId = courseId, SenderUserId = me, Content = req.Content };
        _db.CourseChannelMessages.Add(msg);
        await _db.SaveChangesAsync();

        var isStudentQuestion = course.InstructorId != me;
        if (course.CanalMessagerie && isStudentQuestion)
            await TryAnswerWithWinAiAsync(course, msg, me);

        return Ok(new { id = msg.Id, createdAt = msg.CreatedAt });
    }

    private async Task TryAnswerWithWinAiAsync(Course course, CourseChannelMessage questionMsg, int studentUserId)
    {
        try
        {
            var lessons = await _db.CourseLessons
                .Where(l => l.CourseId == course.Id)
                .OrderBy(l => l.Position)
                .Select(l => new { l.Title, l.Description, l.ArticleContent })
                .ToListAsync();

            var context = lessons
                .Select(l => $"{l.Title} — {l.Description}\n{l.ArticleContent}")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Take(20); // évite un prompt trop long sur une formation à beaucoup de leçons

            var client = _httpClientFactory.CreateClient("FastApiClient");
            using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/api/winai/canal-qa")
            {
                Content = JsonContent.Create(new
                {
                    question = questionMsg.Content,
                    course_title = course.Title,
                    lessons_context = context,
                })
            };
            var auth = HttpContext.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrEmpty(auth)) httpReq.Headers.TryAddWithoutValidation("Authorization", auth);

            var res = await client.SendAsync(httpReq);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("WinAI canal-qa a répondu {Status} pour la formation {CourseId}", res.StatusCode, course.Id);
                return;
            }

            var body = await res.Content.ReadAsStringAsync();
            var qa = JsonSerializer.Deserialize<CanalQaResponse>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            });
            if (qa == null) return;

            var log = new WinAIInteractionLog
            {
                CourseId = course.Id,
                StudentUserId = studentUserId,
                QuestionMessageId = questionMsg.Id,
                Question = questionMsg.Content,
            };

            if (qa.Action == "tag_professor" || qa.Confidence < ConfidenceThreshold || string.IsNullOrWhiteSpace(qa.Answer))
            {
                questionMsg.TaggedProfessor = true;
                log.Action = "tag_professor";
                log.Confidence = qa.Confidence;
                _db.WinAIInteractionLogs.Add(log);
                await _db.SaveChangesAsync();

                await _ntfy.PublishAsync($"winplus-user-{course.InstructorId}", "Question signalée dans le canal",
                    $"WinAI n'est pas assez confiant pour répondre dans « {course.Title} » — ta réponse est attendue.",
                    priority: "high", userId: course.InstructorId, type: "CourseChannel");
                return;
            }

            var aiMsg = new CourseChannelMessage
            {
                CourseId = course.Id,
                SenderUserId = null,
                IsAiGenerated = true,
                AiConfidence = qa.Confidence,
                Content = qa.Answer,
            };
            _db.CourseChannelMessages.Add(aiMsg);

            log.Answer = qa.Answer;
            log.Confidence = qa.Confidence;
            log.Action = "answered";
            _db.WinAIInteractionLogs.Add(log);

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'appel WinAI canal-qa pour la formation {CourseId}", course.Id);
        }
    }

    /// <summary>Active/désactive le Q&amp;A automatique WinAI sur cette formation (professeur uniquement).</summary>
    [HttpPut("settings")]
    public async Task<IActionResult> SetSettings(int courseId, [FromBody] SetCanalMessagerieRequest req)
    {
        var me = User.GetUserId();
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == courseId);
        if (course == null) return NotFound();
        if (course.InstructorId != me) return Forbid();

        course.CanalMessagerie = req.CanalMessagerie;
        course.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { canalMessagerie = course.CanalMessagerie });
    }

    /// <summary>Journal des interactions WinAI de la formation (professeur uniquement).</summary>
    [HttpGet("interactions")]
    public async Task<IActionResult> GetInteractions(int courseId)
    {
        var me = User.GetUserId();
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == courseId);
        if (course == null) return NotFound();
        if (course.InstructorId != me) return Forbid();

        var logs = await _db.WinAIInteractionLogs
            .AsNoTracking()
            .Where(l => l.CourseId == courseId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
        return Ok(logs);
    }

    /// <summary>Le professeur corrige une réponse WinAI passée (US-3C).</summary>
    [HttpPut("interactions/{interactionId:int}/correct")]
    public async Task<IActionResult> CorrectInteraction(int courseId, int interactionId, [FromBody] CorrectInteractionRequest req)
    {
        var me = User.GetUserId();
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == courseId);
        if (course == null) return NotFound();
        if (course.InstructorId != me) return Forbid();

        var log = await _db.WinAIInteractionLogs.FirstOrDefaultAsync(l => l.Id == interactionId && l.CourseId == courseId);
        if (log == null) return NotFound();

        log.CorrectedByTeacher = true;
        log.CorrectedAnswer = req.CorrectedAnswer;
        log.CorrectedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Poste la correction dans le canal pour que l'élève la voie.
        _db.CourseChannelMessages.Add(new CourseChannelMessage
        {
            CourseId = courseId,
            SenderUserId = me,
            Content = $"[Correction du professeur] {req.CorrectedAnswer}",
        });
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }
}
