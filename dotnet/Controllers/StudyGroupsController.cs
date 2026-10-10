using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;

namespace Backend.Controllers;

public class CreateStudyGroupRequest
{
    public string? Name { get; set; }
    public string? Subject { get; set; }
    public string? Description { get; set; }
}

public class JoinStudyGroupRequest
{
    public string? Code { get; set; }
}

/// <summary>
/// Groupes d'étude collaboratifs (S7-2).
/// GET    /api/study-groups/me
/// GET    /api/study-groups/{id}
/// GET    /api/study-groups/{id}/leaderboard
/// POST   /api/study-groups
/// POST   /api/study-groups/join
/// DELETE /api/study-groups/{id}/leave
/// </summary>
[ApiController]
[Route("api/study-groups")]
[Authorize]
public class StudyGroupsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<StudyGroupsController> _logger;

    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public StudyGroupsController(ApplicationDbContext db, ILogger<StudyGroupsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Groupes dont l'utilisateur est membre, avec effectif réel.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMine()
    {
        try
        {
            var userId = User.GetUserId();

            var groups = await _db.StudyGroupMembers
                .AsNoTracking()
                .Where(m => m.UserId == userId && m.StudyGroup != null && m.StudyGroup.IsActive)
                .OrderByDescending(m => m.StudyGroup!.LastActivityAt ?? m.StudyGroup.CreatedAt)
                .Select(m => new
                {
                    id             = m.StudyGroupId,
                    name           = m.StudyGroup!.Name,
                    subject        = m.StudyGroup.Subject,
                    description    = m.StudyGroup.Description,
                    joinCode       = m.StudyGroup.JoinCode,
                    role           = m.Role,
                    isOwner        = m.StudyGroup.OwnerId == userId,
                    memberCount    = _db.StudyGroupMembers.Count(x => x.StudyGroupId == m.StudyGroupId),
                    createdAt      = m.StudyGroup.CreatedAt,
                    lastActivityAt = m.StudyGroup.LastActivityAt
                })
                .ToListAsync();

            return Ok(new { data = groups, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing study groups");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Détail d'un groupe : membres réels et leur niveau.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOne([FromRoute] int id)
    {
        try
        {
            var userId = User.GetUserId();

            var isMember = await _db.StudyGroupMembers.AnyAsync(m => m.StudyGroupId == id && m.UserId == userId);
            if (!isMember)
                return StatusCode(403, new { success = false, error = "Vous n'êtes pas membre de ce groupe." });

            var group = await _db.StudyGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
            if (group == null) return NotFound(new { success = false, error = "Groupe introuvable." });

            var members = await _db.StudyGroupMembers
                .AsNoTracking()
                .Where(m => m.StudyGroupId == id)
                .OrderBy(m => m.JoinedAt)
                .Select(m => new
                {
                    userId    = m.UserId,
                    firstName = m.User!.FirstName,
                    lastName  = m.User.LastName,
                    level     = m.User.Level,
                    avatarUrl = m.User.AvatarUrl,
                    role      = m.Role,
                    joinedAt  = m.JoinedAt
                })
                .ToListAsync();

            return Ok(new
            {
                data = new
                {
                    id          = group.Id,
                    name        = group.Name,
                    subject     = group.Subject,
                    description = group.Description,
                    joinCode    = group.JoinCode,
                    isOwner     = group.OwnerId == userId,
                    members
                },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting study group {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Classement hebdomadaire réel du groupe (restait fictif après le lot 8,
    /// §16.3 du suivi) : classe les membres sur leur activité des 7 derniers
    /// jours, à partir de données déjà en base (quiz réussis, minutes
    /// étudiées) -- même fenêtre et mêmes tables que le calcul de série
    /// (streak) de UsersController, aucune nouvelle mesure à instrumenter.
    /// Points = (quiz réussis x 10) + minutes de session d'étude.
    /// </summary>
    [HttpGet("{id:int}/leaderboard")]
    public async Task<IActionResult> GetLeaderboard([FromRoute] int id)
    {
        try
        {
            var userId = User.GetUserId();

            var isMember = await _db.StudyGroupMembers.AnyAsync(m => m.StudyGroupId == id && m.UserId == userId);
            if (!isMember)
                return StatusCode(403, new { success = false, error = "Vous n'êtes pas membre de ce groupe." });

            var since = DateTime.UtcNow.AddDays(-7);

            var members = await _db.StudyGroupMembers
                .AsNoTracking()
                .Where(m => m.StudyGroupId == id)
                .Select(m => new { m.UserId, m.User!.FirstName, m.User.LastName, m.User.AvatarUrl })
                .ToListAsync();
            var memberIds = members.Select(m => m.UserId).ToList();

            var quizPoints = await _db.QuizAttempts
                .Where(a => memberIds.Contains(a.UserId) && a.Passed && a.CompletedAt >= since)
                .GroupBy(a => a.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToListAsync();

            var studyMinutes = await _db.StudySessions
                .Where(s => memberIds.Contains(s.UserId) && s.CreatedAt >= since)
                .GroupBy(s => s.UserId)
                .Select(g => new { UserId = g.Key, Minutes = g.Sum(s => s.Duration) })
                .ToListAsync();

            var quizPointsByUser = quizPoints.ToDictionary(x => x.UserId, x => x.Count);
            var minutesByUser = studyMinutes.ToDictionary(x => x.UserId, x => x.Minutes);

            var ranking = members
                .Select(m =>
                {
                    var quizzesPassed = quizPointsByUser.GetValueOrDefault(m.UserId, 0);
                    var minutes = minutesByUser.GetValueOrDefault(m.UserId, 0);
                    return new
                    {
                        userId = m.UserId,
                        firstName = m.FirstName,
                        lastName = m.LastName,
                        avatarUrl = m.AvatarUrl,
                        quizzesPassed,
                        studyMinutes = minutes,
                        points = quizzesPassed * 10 + minutes
                    };
                })
                .OrderByDescending(x => x.points)
                .ThenByDescending(x => x.quizzesPassed)
                .ToList();

            return Ok(new
            {
                data = new { periodDays = 7, since, ranking },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing study group leaderboard {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStudyGroupRequest request)
    {
        try
        {
            var name = (request.Name ?? "").Trim();
            if (name.Length < 3 || name.Length > 80)
                return BadRequest(new { success = false, error = "Le nom doit contenir entre 3 et 80 caractères." });

            var userId = User.GetUserId();

            var group = new StudyGroup
            {
                OwnerId        = userId,
                Name           = name,
                Subject        = string.IsNullOrWhiteSpace(request.Subject) ? null : request.Subject!.Trim(),
                Description    = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description!.Trim(),
                JoinCode       = await GenerateUniqueCodeAsync(),
                LastActivityAt = DateTime.UtcNow
            };

            _db.StudyGroups.Add(group);
            await _db.SaveChangesAsync();

            _db.StudyGroupMembers.Add(new StudyGroupMember
            {
                StudyGroupId = group.Id,
                UserId       = userId,
                Role         = "owner"
            });
            await _db.SaveChangesAsync();

            return Ok(new
            {
                data = new
                {
                    id = group.Id, name = group.Name, subject = group.Subject,
                    description = group.Description, joinCode = group.JoinCode,
                    isOwner = true, memberCount = 1, createdAt = group.CreatedAt
                },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating study group");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPost("join")]
    public async Task<IActionResult> Join([FromBody] JoinStudyGroupRequest request)
    {
        try
        {
            var code = (request.Code ?? "").Trim().ToUpperInvariant();
            if (code.Length < 4)
                return BadRequest(new { success = false, error = "Code d'invitation invalide." });

            var userId = User.GetUserId();

            var group = await _db.StudyGroups.FirstOrDefaultAsync(g => g.JoinCode == code && g.IsActive);
            if (group == null)
                return NotFound(new { success = false, error = "Aucun groupe ne correspond à ce code." });

            var already = await _db.StudyGroupMembers
                .AnyAsync(m => m.StudyGroupId == group.Id && m.UserId == userId);
            if (already)
                return Ok(new { data = new { id = group.Id, name = group.Name, alreadyMember = true }, success = true });

            _db.StudyGroupMembers.Add(new StudyGroupMember { StudyGroupId = group.Id, UserId = userId });
            group.LastActivityAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var memberCount = await _db.StudyGroupMembers.CountAsync(m => m.StudyGroupId == group.Id);

            return Ok(new
            {
                data = new { id = group.Id, name = group.Name, subject = group.Subject, memberCount, alreadyMember = false },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining study group");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Quitter un groupe. Le propriétaire qui part archive le groupe.</summary>
    [HttpDelete("{id:int}/leave")]
    public async Task<IActionResult> Leave([FromRoute] int id)
    {
        try
        {
            var userId = User.GetUserId();

            var member = await _db.StudyGroupMembers
                .FirstOrDefaultAsync(m => m.StudyGroupId == id && m.UserId == userId);
            if (member == null) return NoContent();

            _db.StudyGroupMembers.Remove(member);

            var group = await _db.StudyGroups.FirstOrDefaultAsync(g => g.Id == id);
            if (group != null && group.OwnerId == userId) group.IsActive = false;

            await _db.SaveChangesAsync();
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving study group {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    private async Task<string> GenerateUniqueCodeAsync()
    {
        var rng = Random.Shared;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var code = new string(Enumerable.Range(0, 6)
                .Select(_ => CodeAlphabet[rng.Next(CodeAlphabet.Length)]).ToArray());
            if (!await _db.StudyGroups.AnyAsync(g => g.JoinCode == code)) return code;
        }
        return Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }
}
