using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Espace institution, phase 2 (étape 3) : devoirs par classe.
///
/// GET    /api/institution/homework                 devoirs de l'établissement (avec avancement)
/// GET    /api/institution/homework/{id}/progress   avancement élève par élève
/// POST   /api/institution/homework                 donner un devoir à une classe (quiz ou épreuve)
/// DELETE /api/institution/homework/{id}            retirer un devoir
/// GET    /api/institution/homework/mine            devoirs reçus (élève)
///
/// Un devoir est fait quand l'élève a terminé le quiz après la date du devoir
/// (QuizAttempts.IsCompleted, CompletedAt ≥ CreatedAt) ; la note retenue est
/// la meilleure de ces tentatives.
/// </summary>
[ApiController]
[Route("api/institution/homework")]
[Authorize]
public class InstitutionHomeworkController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IQuizService _quizzes;
    private readonly ILogger<InstitutionHomeworkController> _logger;
    private readonly IServiceScopeFactory _scopes;

    public InstitutionHomeworkController(ApplicationDbContext db, IQuizService quizzes,
        ILogger<InstitutionHomeworkController> logger, IServiceScopeFactory scopes)
    {
        _db = db;
        _quizzes = quizzes;
        _logger = logger;
        _scopes = scopes;
    }

    public record CreateHomeworkRequest(int StudyGroupId, int? QuizId, int? ExamId, string? Title, string? Instructions, DateTime? DueDate);

    private async Task<int?> MyInstitutionIdAsync(int userId) =>
        await _db.Users.Where(u => u.Id == userId).Select(u => u.InstitutionId).FirstOrDefaultAsync();

    private Task<List<int>> StudentMembersAsync(int groupId) =>
        _db.StudyGroupMembers.AsNoTracking()
            .Where(m => m.StudyGroupId == groupId && m.Role != InstitutionTeachersController.TeacherRole)
            .Select(m => m.UserId).ToListAsync();

    /// <summary>Meilleure note terminée de chaque élève sur ce quiz depuis la date du devoir.</summary>
    private async Task<Dictionary<int, (decimal Score, DateTime CompletedAt)>> BestAttemptsAsync(int quizId, DateTime since, List<int> userIds)
    {
        var rows = await _db.QuizAttempts.AsNoTracking()
            .Where(a => a.QuizId == quizId && a.IsCompleted && a.CompletedAt >= since && userIds.Contains(a.UserId))
            .Select(a => new { a.UserId, a.Score, a.CompletedAt })
            .ToListAsync();
        return rows.GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => { var b = g.OrderByDescending(x => x.Score).First(); return (b.Score, b.CompletedAt); });
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? groupId)
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var institutionId = await MyInstitutionIdAsync(me);
            if (institutionId == null) return Ok(new { data = Array.Empty<object>(), success = true });

            var items = await _db.InstitutionHomework.AsNoTracking()
                .Where(h => h.InstitutionId == institutionId && !h.IsDeleted && (groupId == null || h.StudyGroupId == groupId))
                .OrderByDescending(h => h.CreatedAt)
                .Take(100)
                .Select(h => new
                {
                    h.Id, h.Title, h.Instructions, h.DueDate, h.CreatedAt, h.QuizId, h.ExamId, h.StudyGroupId,
                    groupName = h.StudyGroup != null ? h.StudyGroup.Name : null,
                    quizTitle = h.Quiz != null ? h.Quiz.Title : null,
                    quizSubject = h.Quiz != null ? h.Quiz.Subject : null,
                })
                .ToListAsync();

            var result = new List<object>();
            var membersCache = new Dictionary<int, List<int>>();
            foreach (var h in items)
            {
                if (!membersCache.TryGetValue(h.StudyGroupId, out var members))
                    membersCache[h.StudyGroupId] = members = await StudentMembersAsync(h.StudyGroupId);
                var best = await BestAttemptsAsync(h.QuizId, h.CreatedAt, members);
                result.Add(new
                {
                    h.Id, h.Title, h.Instructions, h.DueDate, h.CreatedAt, h.QuizId, h.ExamId,
                    groupId = h.StudyGroupId, h.groupName, h.quizTitle, h.quizSubject,
                    students = members.Count,
                    done = best.Count,
                    averageScore = best.Count == 0 ? (double?)null : Math.Round((double)best.Values.Average(b => b.Score), 1),
                });
            }
            return Ok(new { data = result, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing institution homework");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("{id:int}/progress")]
    public async Task<IActionResult> Progress([FromRoute] int id)
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var institutionId = await MyInstitutionIdAsync(me);
            var h = await _db.InstitutionHomework.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.InstitutionId == institutionId && !x.IsDeleted);
            if (h == null) return NotFound(new { error = "Devoir introuvable." });

            var members = await StudentMembersAsync(h.StudyGroupId);
            var best = await BestAttemptsAsync(h.QuizId, h.CreatedAt, members);
            var users = await _db.Users.AsNoTracking()
                .Where(u => members.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
                .ToListAsync();
            var now = DateTime.UtcNow;

            var rows = users
                .Select(u =>
                {
                    var has = best.TryGetValue(u.Id, out var b);
                    var status = has ? (h.DueDate != null && b.CompletedAt > h.DueDate ? "late" : "done")
                                     : (h.DueDate != null && now > h.DueDate ? "missing" : "pending");
                    return new
                    {
                        id = u.Id, u.FirstName, u.LastName, u.Email, status,
                        score = has ? Math.Round((double)b.Score, 1) : (double?)null,
                        completedAt = has ? b.CompletedAt : (DateTime?)null,
                    };
                })
                .OrderBy(r => r.status == "missing" ? 0 : r.status == "pending" ? 1 : 2)
                .ThenBy(r => r.LastName).ThenBy(r => r.FirstName)
                .ToList();
            return Ok(new { data = rows, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting homework progress");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateHomeworkRequest req)
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var institutionId = await MyInstitutionIdAsync(me);
            if (institutionId == null) return BadRequest(new { error = "Compte non rattaché à un établissement." });

            var group = await _db.StudyGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == req.StudyGroupId && g.OwnerId == me && g.IsActive);
            if (group == null) return NotFound(new { error = "Classe introuvable." });
            if ((req.QuizId == null) == (req.ExamId == null)) return BadRequest(new { error = "Choisissez un quiz ou une épreuve." });
            if (req.DueDate != null && req.DueDate <= DateTime.UtcNow) return BadRequest(new { error = "La date limite doit être dans le futur." });

            int quizId;
            string defaultTitle;
            if (req.QuizId != null)
            {
                var quiz = await _db.Quizzes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == req.QuizId && !q.IsDeleted);
                if (quiz == null) return NotFound(new { error = "Quiz introuvable." });
                quizId = quiz.Id;
                defaultTitle = quiz.Title;
            }
            else
            {
                // Épreuve : son quiz est créé (ou réutilisé) par WinAI à partir du PDF.
                try
                {
                    var q = await _quizzes.GetOrCreateExamQuizAsync(req.ExamId!.Value);
                    quizId = q.Id;
                    defaultTitle = q.Title;
                }
                catch (KeyNotFoundException) { return NotFound(new { error = "Épreuve introuvable." }); }
                catch (InvalidOperationException e) { return BadRequest(new { error = e.Message }); }
            }

            var title = string.IsNullOrWhiteSpace(req.Title) ? defaultTitle : req.Title.Trim();
            if (title.Length > 200) title = title[..200];
            var homework = new InstitutionHomework
            {
                InstitutionId = institutionId.Value,
                StudyGroupId = group.Id,
                CreatedByUserId = me,
                QuizId = quizId,
                ExamId = req.ExamId,
                Title = title,
                Instructions = string.IsNullOrWhiteSpace(req.Instructions) ? null : req.Instructions.Trim(),
                DueDate = req.DueDate,
            };
            _db.InstitutionHomework.Add(homework);
            await _db.SaveChangesAsync();

            var members = await StudentMembersAsync(group.Id);
            Notify(group.Name, homework.Id, title, req.DueDate, members);
            return Ok(new { data = new { homework.Id, students = members.Count }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating institution homework");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    private void Notify(string groupName, int homeworkId, string title, DateTime? due, IReadOnlyList<int> userIds)
    {
        if (userIds.Count == 0) return;
        var scopes = _scopes;
        var logger = _logger;
        var dueText = due != null ? $" À rendre avant le {due.Value.ToLocalTime():dd/MM à HH\\hmm}." : string.Empty;
        _ = Task.Run(async () =>
        {
            using var scope = scopes.CreateScope();
            var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();
            foreach (var id in userIds)
            {
                try
                {
                    await ntfy.PublishAsync($"winplus-user-{id}", $"Nouveau devoir · {groupName}",
                        $"« {title} » t'attend dans Mes devoirs.{dueText}",
                        userId: id, type: "InstitutionHomework", relatedEntityType: "InstitutionHomework", relatedEntityId: homeworkId);
                }
                catch (Exception ex) { logger.LogWarning(ex, "Devoir {HomeworkId} non notifié à {UserId}", homeworkId, id); }
            }
        });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var institutionId = await MyInstitutionIdAsync(me);
            var h = await _db.InstitutionHomework.FirstOrDefaultAsync(x => x.Id == id && x.InstitutionId == institutionId && !x.IsDeleted);
            if (h == null) return NotFound(new { error = "Devoir introuvable." });
            h.IsDeleted = true;
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting institution homework");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Devoirs des classes d'établissement dont l'élève est membre, avec son statut.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        try
        {
            var me = User.GetUserId();
            var groupIds = await _db.StudyGroupMembers.AsNoTracking()
                .Where(m => m.UserId == me && m.Role != InstitutionTeachersController.TeacherRole)
                .Select(m => m.StudyGroupId).ToListAsync();
            if (groupIds.Count == 0) return Ok(new { data = Array.Empty<object>(), success = true });

            var items = await _db.InstitutionHomework.AsNoTracking()
                .Where(h => !h.IsDeleted && groupIds.Contains(h.StudyGroupId))
                .OrderByDescending(h => h.CreatedAt)
                .Take(50)
                .Select(h => new
                {
                    h.Id, h.Title, h.Instructions, h.DueDate, h.CreatedAt, h.QuizId,
                    groupName = h.StudyGroup != null ? h.StudyGroup.Name : null,
                    quizSubject = h.Quiz != null ? h.Quiz.Subject : null,
                })
                .ToListAsync();

            var quizIds = items.Select(i => i.QuizId).Distinct().ToList();
            var attempts = await _db.QuizAttempts.AsNoTracking()
                .Where(a => a.UserId == me && a.IsCompleted && quizIds.Contains(a.QuizId))
                .Select(a => new { a.QuizId, a.Score, a.CompletedAt })
                .ToListAsync();

            var now = DateTime.UtcNow;
            var result = items.Select(h =>
            {
                var best = attempts.Where(a => a.QuizId == h.QuizId && a.CompletedAt >= h.CreatedAt)
                    .OrderByDescending(a => a.Score).FirstOrDefault();
                var status = best != null ? "done" : (h.DueDate != null && now > h.DueDate ? "missing" : "pending");
                return new
                {
                    h.Id, h.Title, h.Instructions, h.DueDate, h.CreatedAt, h.QuizId, h.groupName, h.quizSubject, status,
                    myScore = best != null ? Math.Round((double)best.Score, 1) : (double?)null,
                };
            }).ToList();
            return Ok(new { data = result, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting my institution homework");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
