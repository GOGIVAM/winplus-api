using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

public class ProposeGoalRequest
{
    public int ChildId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Type { get; set; }
    public DateTime Deadline { get; set; }
}

public class ModifyGoalRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime Deadline { get; set; }
}

public class CreateGoalRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Type { get; set; }
    public DateTime Deadline { get; set; }
}

/// <summary>
/// Workflow de proposition d'objectifs (Goal) entre parent et élève. Deux
/// origines possibles pour un objectif actif : créé directement par l'élève,
/// ou proposé par un parent lié puis accepté/modifié par l'élève — jamais
/// l'inverse, le parent ne peut pas créer un objectif déjà actif pour son
/// enfant sans son accord.
///
/// POST /api/goals/propose      (parent)  crée en Pending, notifie l'enfant
/// POST /api/goals/{id}/accept  (élève)   Pending → Active, notifie le parent
/// POST /api/goals/{id}/refuse  (élève)   Pending → Refused, notifie le parent
/// PUT  /api/goals/{id}/modify  (élève)   édite + Pending → Active en un geste, notifie le parent
/// POST /api/goals              (élève)   création directe, Active dès l'origine, pas de notification
///
/// Ne touche pas à WeeklyGoal (cibles hebdomadaires d'heures/quiz/téléchargements,
/// entité distincte et sans rapport avec ce workflow).
/// </summary>
[ApiController]
[Route("api/goals")]
[Authorize]
public class GoalsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<GoalsController> _logger;
    private readonly INtfyService _ntfy;

    public GoalsController(ApplicationDbContext db, ILogger<GoalsController> logger, INtfyService ntfy)
    {
        _db = db;
        _logger = logger;
        _ntfy = ntfy;
    }

    private static object ToDto(Goal g) => new
    {
        g.Id,
        g.UserId,
        g.Title,
        g.Description,
        g.Type,
        g.Progress,
        g.Status,
        g.ProposedByUserId,
        g.TargetDate,
        g.CreatedAt,
        g.UpdatedAt,
        g.CompletedAt,
    };

    private async Task<string> DisplayNameAsync(int userId)
    {
        var u = await _db.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new { x.FirstName, x.LastName }).FirstOrDefaultAsync();
        return u == null ? "Quelqu'un" : $"{u.FirstName} {u.LastName}".Trim();
    }

    /// <summary>Le parent propose un objectif à un enfant lié. Toujours Pending à la création.</summary>
    [HttpPost("propose")]
    [Authorize(Roles = "parent")]
    public async Task<IActionResult> Propose([FromBody] ProposeGoalRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return BadRequest(new { success = false, error = "Le titre est requis." });
            if (request.Deadline == default)
                return BadRequest(new { success = false, error = "L'échéance est requise." });

            var parentId = User.GetUserId();
            var linked = await _db.ParentStudentLinks.AnyAsync(l =>
                l.ParentId == parentId && l.StudentId == request.ChildId && l.Status == "accepted");
            if (!linked)
                return StatusCode(403, new { success = false, error = "Cet enfant n'est pas lié à votre compte." });

            var goal = new Goal
            {
                UserId = request.ChildId,
                Title = request.Title.Trim(),
                Description = request.Description,
                Type = request.Type,
                TargetDate = request.Deadline,
                Status = GoalStatus.Pending,
                ProposedByUserId = parentId,
            };
            _db.Goals.Add(goal);
            await _db.SaveChangesAsync();

            var parentName = await DisplayNameAsync(parentId);
            await _ntfy.PublishAsync(
                topic: $"winplus-user-{request.ChildId}",
                title: "Nouvel objectif proposé",
                message: $"{parentName} vous propose un nouvel objectif : « {goal.Title} ». Acceptez, modifiez ou refusez-le.",
                userId: request.ChildId,
                type: "goal",
                relatedEntityType: "Goal",
                relatedEntityId: goal.Id);

            return Ok(new { data = ToDto(goal), success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proposing goal");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>L'élève accepte tel quel un objectif proposé. Pending → Active.</summary>
    [HttpPost("{id:int}/accept")]
    [Authorize(Roles = "student")]
    public async Task<IActionResult> Accept(int id)
    {
        try
        {
            var studentId = User.GetUserId();
            var goal = await _db.Goals.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
            if (goal == null) return NotFound();
            if (goal.UserId != studentId) return Forbid();
            if (goal.Status != GoalStatus.Pending)
                return Conflict(new { success = false, error = "Cet objectif a déjà été traité." });

            var claimed = await _db.Goals
                .Where(g => g.Id == id && g.UserId == studentId && g.Status == GoalStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(g => g.Status, GoalStatus.Active)
                    .SetProperty(g => g.UpdatedAt, DateTime.UtcNow));
            if (claimed == 0)
                return Conflict(new { success = false, error = "Cet objectif a déjà été traité." });

            if (goal.ProposedByUserId is int parentId)
            {
                var childName = await DisplayNameAsync(studentId);
                await _ntfy.PublishAsync(
                    topic: $"winplus-user-{parentId}",
                    title: "Objectif accepté",
                    message: $"{childName} a accepté l'objectif « {goal.Title} ».",
                    userId: parentId,
                    type: "goal",
                    relatedEntityType: "Goal",
                    relatedEntityId: goal.Id);
            }

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting goal {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>L'élève refuse un objectif proposé. Pending → Refused, définitif.</summary>
    [HttpPost("{id:int}/refuse")]
    [Authorize(Roles = "student")]
    public async Task<IActionResult> Refuse(int id)
    {
        try
        {
            var studentId = User.GetUserId();
            var goal = await _db.Goals.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
            if (goal == null) return NotFound();
            if (goal.UserId != studentId) return Forbid();
            if (goal.Status != GoalStatus.Pending)
                return Conflict(new { success = false, error = "Cet objectif a déjà été traité." });

            var claimed = await _db.Goals
                .Where(g => g.Id == id && g.UserId == studentId && g.Status == GoalStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(g => g.Status, GoalStatus.Refused)
                    .SetProperty(g => g.UpdatedAt, DateTime.UtcNow));
            if (claimed == 0)
                return Conflict(new { success = false, error = "Cet objectif a déjà été traité." });

            if (goal.ProposedByUserId is int parentId)
            {
                var childName = await DisplayNameAsync(studentId);
                await _ntfy.PublishAsync(
                    topic: $"winplus-user-{parentId}",
                    title: "Objectif refusé",
                    message: $"{childName} a refusé l'objectif « {goal.Title} ».",
                    userId: parentId,
                    type: "goal",
                    relatedEntityType: "Goal",
                    relatedEntityId: goal.Id);
            }

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refusing goal {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// L'élève modifie une proposition (titre/description/échéance) et l'active
    /// dans le même geste — pas de re-validation parent, le parent est
    /// seulement notifié du résultat.
    /// </summary>
    [HttpPut("{id:int}/modify")]
    [Authorize(Roles = "student")]
    public async Task<IActionResult> Modify(int id, [FromBody] ModifyGoalRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return BadRequest(new { success = false, error = "Le titre est requis." });
            if (request.Deadline == default)
                return BadRequest(new { success = false, error = "L'échéance est requise." });

            var studentId = User.GetUserId();
            var goal = await _db.Goals.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
            if (goal == null) return NotFound();
            if (goal.UserId != studentId) return Forbid();
            if (goal.Status != GoalStatus.Pending)
                return Conflict(new { success = false, error = "Cet objectif a déjà été traité." });

            var claimed = await _db.Goals
                .Where(g => g.Id == id && g.UserId == studentId && g.Status == GoalStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(g => g.Title, request.Title.Trim())
                    .SetProperty(g => g.Description, request.Description)
                    .SetProperty(g => g.TargetDate, request.Deadline)
                    .SetProperty(g => g.Status, GoalStatus.Active)
                    .SetProperty(g => g.UpdatedAt, DateTime.UtcNow));
            if (claimed == 0)
                return Conflict(new { success = false, error = "Cet objectif a déjà été traité." });

            if (goal.ProposedByUserId is int parentId)
            {
                var childName = await DisplayNameAsync(studentId);
                await _ntfy.PublishAsync(
                    topic: $"winplus-user-{parentId}",
                    title: "Objectif modifié",
                    message: $"{childName} a modifié l'objectif proposé et l'a activé : « {request.Title.Trim()} ».",
                    userId: parentId,
                    type: "goal",
                    relatedEntityType: "Goal",
                    relatedEntityId: goal.Id);
            }

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error modifying goal {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>L'élève crée directement un objectif, sans proposition parent. Active dès l'origine.</summary>
    [HttpPost]
    [Authorize(Roles = "student")]
    public async Task<IActionResult> Create([FromBody] CreateGoalRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return BadRequest(new { success = false, error = "Le titre est requis." });
            if (request.Deadline == default)
                return BadRequest(new { success = false, error = "L'échéance est requise." });

            var studentId = User.GetUserId();
            var goal = new Goal
            {
                UserId = studentId,
                Title = request.Title.Trim(),
                Description = request.Description,
                Type = request.Type,
                TargetDate = request.Deadline,
                Status = GoalStatus.Active,
                ProposedByUserId = null,
            };
            _db.Goals.Add(goal);
            await _db.SaveChangesAsync();

            return Ok(new { data = ToDto(goal), success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating goal");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
