using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;

namespace Backend.Controllers;

public record CreateGroupRequest(string Name, string Level, string? Description);
public record AddGroupMemberRequest(string Email);

/// <summary>
/// Endpoints institution côté mobile.
/// Route: /api/institution  (distinct de /api/institutions qui gère l'annuaire global)
///
/// Les groupes d'une institution sont des StudyGroups créés par l'utilisateur institution.
/// Ses élèves sont les InstitutionStudents liés à son InstitutionId.
/// </summary>
[ApiController]
[Route("api/institution")]
[Authorize]
public class InstitutionMobileController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<InstitutionMobileController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public InstitutionMobileController(ApplicationDbContext db,
        ILogger<InstitutionMobileController> logger,
        IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    // ── Groupes ──────────────────────────────────────────────────────────────

    /// <summary>GET /api/institution/groups — liste les groupes de l'institution.</summary>
    [HttpGet("groups")]
    public async Task<IActionResult> GetGroups()
    {
        try
        {
            var userId = User.GetUserId();
            var groups = await _db.StudyGroups
                .AsNoTracking()
                .Where(g => g.OwnerId == userId && g.IsActive)
                .Select(g => new
                {
                    id = g.Id,
                    name = g.Name,
                    description = g.Description,
                    level = g.Subject ?? "",
                    memberCount = g.Members.Count(m => m.UserId != userId),
                })
                .ToListAsync();

            return Ok(groups);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institution groups");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>POST /api/institution/groups — créer un groupe.</summary>
    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest req)
    {
        try
        {
            var userId = User.GetUserId();
            var joinCode = Guid.NewGuid().ToString("N")[..6].ToUpper();

            var group = new StudyGroup
            {
                OwnerId = userId,
                Name = req.Name,
                Subject = req.Level,
                Description = req.Description,
                JoinCode = joinCode,
                IsActive = true,
            };
            _db.StudyGroups.Add(group);
            await _db.SaveChangesAsync();

            return Ok(new { id = group.Id, joinCode = group.JoinCode });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating group");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>DELETE /api/institution/groups/{id} — supprimer un groupe.</summary>
    [HttpDelete("groups/{id:int}")]
    public async Task<IActionResult> DeleteGroup([FromRoute] int id)
    {
        try
        {
            var userId = User.GetUserId();
            var group = await _db.StudyGroups.FirstOrDefaultAsync(g => g.Id == id && g.OwnerId == userId);
            if (group == null) return NotFound(new { error = "Groupe introuvable" });

            group.IsActive = false;
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting group");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>GET /api/institution/groups/{id}/members — membres d'un groupe.</summary>
    [HttpGet("groups/{id:int}/members")]
    public async Task<IActionResult> GetGroupMembers([FromRoute] int id)
    {
        try
        {
            var userId = User.GetUserId();
            var group = await _db.StudyGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id && g.OwnerId == userId);
            if (group == null) return NotFound(new { error = "Groupe introuvable" });

            var members = await _db.StudyGroupMembers
                .AsNoTracking()
                .Where(m => m.StudyGroupId == id && m.UserId != userId)
                .Include(m => m.User)
                .Select(m => new
                {
                    id = m.UserId,
                    firstName = m.User != null ? m.User.FirstName : null,
                    lastName = m.User != null ? m.User.LastName : null,
                    email = m.User != null ? m.User.Email : null,
                    averageScore = 0.0,
                    activityScore = 0,
                })
                .ToListAsync();

            return Ok(members);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting group members");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>POST /api/institution/groups/{id}/members — ajouter un membre par email.</summary>
    [HttpPost("groups/{id:int}/members")]
    public async Task<IActionResult> AddMember([FromRoute] int id, [FromBody] AddGroupMemberRequest req)
    {
        try
        {
            var userId = User.GetUserId();
            var group = await _db.StudyGroups.FirstOrDefaultAsync(g => g.Id == id && g.OwnerId == userId);
            if (group == null) return NotFound(new { error = "Groupe introuvable" });

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email);
            if (user == null) return NotFound(new { error = "Utilisateur introuvable" });

            var already = await _db.StudyGroupMembers.AnyAsync(m => m.StudyGroupId == id && m.UserId == user.Id);
            if (already) return Conflict(new { error = "Déjà membre du groupe" });

            _db.StudyGroupMembers.Add(new StudyGroupMember
            {
                StudyGroupId = id,
                UserId = user.Id,
                Role = "member",
            });
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding group member");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>DELETE /api/institution/groups/{id}/members/{memberId} — retirer un membre.</summary>
    [HttpDelete("groups/{id:int}/members/{memberId:int}")]
    public async Task<IActionResult> RemoveMember([FromRoute] int id, [FromRoute] int memberId)
    {
        try
        {
            var userId = User.GetUserId();
            var group = await _db.StudyGroups.FirstOrDefaultAsync(g => g.Id == id && g.OwnerId == userId);
            if (group == null) return NotFound(new { error = "Groupe introuvable" });

            var member = await _db.StudyGroupMembers.FirstOrDefaultAsync(m => m.StudyGroupId == id && m.UserId == memberId);
            if (member == null) return NotFound(new { error = "Membre introuvable" });

            _db.StudyGroupMembers.Remove(member);
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing group member");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── Analytics ─────────────────────────────────────────────────────────────

    /// <summary>GET /api/institution/analytics — KPIs agrégés de l'institution.</summary>
    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics()
    {
        try
        {
            var userId = User.GetUserId();
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            var institutionId = user?.InstitutionId;

            int totalStudents = 0, activeStudents = 0, downloadsThisMonth = 0, quizzesThisMonth = 0;
            double avgScore = 0;

            if (institutionId.HasValue)
            {
                var studentIds = await _db.InstitutionStudents
                    .Where(s => s.InstitutionId == institutionId.Value && s.IsActive)
                    .Select(s => s.StudentId)
                    .ToListAsync();

                totalStudents = studentIds.Count;
                var monthStart = DateTime.UtcNow.AddDays(-30);

                activeStudents = await _db.DownloadHistories
                    .Where(d => studentIds.Contains(d.UserId) && d.CreatedAt >= monthStart)
                    .Select(d => d.UserId)
                    .Distinct()
                    .CountAsync();

                downloadsThisMonth = await _db.DownloadHistories
                    .CountAsync(d => studentIds.Contains(d.UserId) && d.CreatedAt >= monthStart);

                quizzesThisMonth = await _db.QuizAttempts
                    .CountAsync(a => studentIds.Contains(a.UserId) && a.CompletedAt >= monthStart);

                avgScore = await _db.QuizAttempts
                    .Where(a => studentIds.Contains(a.UserId) && a.CompletedAt >= monthStart)
                    .AverageAsync(a => (double?)a.Score) ?? 0;
            }
            else
            {
                // Groupes propres à cet utilisateur institution
                var groupIds = await _db.StudyGroups
                    .Where(g => g.OwnerId == userId && g.IsActive)
                    .Select(g => g.Id)
                    .ToListAsync();
                totalStudents = await _db.StudyGroupMembers
                    .CountAsync(m => groupIds.Contains(m.StudyGroupId) && m.UserId != userId);
            }

            var today = DateTime.UtcNow.Date;
            var activityByDay = new Dictionary<string, int>
            {
                ["Lun"] = 0, ["Mar"] = 0, ["Mer"] = 0,
                ["Jeu"] = 0, ["Ven"] = 0, ["Sam"] = 0, ["Dim"] = 0,
            };
            string[] dayNames = ["Dim", "Lun", "Mar", "Mer", "Jeu", "Ven", "Sam"];
            for (int i = 6; i >= 0; i--)
            {
                var d = today.AddDays(-i);
                var key = dayNames[(int)d.DayOfWeek];
                activityByDay[key] = downloadsThisMonth / 7;
            }

            return Ok(new
            {
                totalStudents,
                activeStudents,
                averageScore = Math.Round(avgScore, 1),
                downloadsThisMonth,
                quizzesThisMonth,
                activityByDay,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institution analytics");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── Élèves à risque ───────────────────────────────────────────────────────

    /// <summary>GET /api/institution/at-risk — élèves à risque de décrochage.</summary>
    [HttpGet("at-risk")]
    public async Task<IActionResult> GetAtRisk()
    {
        try
        {
            var userId = User.GetUserId();
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            var institutionId = user?.InstitutionId;

            List<int> studentIds;
            if (institutionId.HasValue)
            {
                studentIds = await _db.InstitutionStudents
                    .Where(s => s.InstitutionId == institutionId.Value && s.IsActive)
                    .Select(s => s.StudentId)
                    .ToListAsync();
            }
            else
            {
                var groupIds = await _db.StudyGroups
                    .Where(g => g.OwnerId == userId && g.IsActive)
                    .Select(g => g.Id)
                    .ToListAsync();
                studentIds = await _db.StudyGroupMembers
                    .Where(m => groupIds.Contains(m.StudyGroupId) && m.UserId != userId)
                    .Select(m => m.UserId)
                    .Distinct()
                    .ToListAsync();
            }

            if (!studentIds.Any()) return Ok(new List<object>());

            var threshold = DateTime.UtcNow.AddDays(-7);
            var activeIds = await _db.DownloadHistories
                .Where(d => studentIds.Contains(d.UserId) && d.CreatedAt >= threshold)
                .Select(d => d.UserId)
                .Distinct()
                .ToListAsync();

            var inactiveIds = studentIds.Except(activeIds).ToList();

            var students = await _db.Users
                .AsNoTracking()
                .Where(u => inactiveIds.Contains(u.Id))
                .Take(20)
                .ToListAsync();

            var result = students.Select(s => new
            {
                id = s.Id,
                firstName = s.FirstName ?? "",
                lastName = s.LastName ?? "",
                groupName = (string?)null,
                level = s.Level ?? "",
                riskScore = 0.7,
                riskReason = "Aucune activité depuis plus de 7 jours",
                weakSubject = "",
                weakScore = 0,
                globalScore = 0,
                inactiveDays = (int)(DateTime.UtcNow - (s.LastLoginAt ?? DateTime.UtcNow.AddDays(-14))).TotalDays,
            }).ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting at-risk students");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── Plan d'action ─────────────────────────────────────────────────────────

    /// <summary>GET /api/institution/action-plan — plan d'action IA pour l'institution.</summary>
    [HttpGet("action-plan")]
    public async Task<IActionResult> GetActionPlan(CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("FastApiClient");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/institution/action-plan");
            var auth = HttpContext.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(auth))
                req.Headers.TryAddWithoutValidation("Authorization", auth);

            var res = await client.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
                return Ok(new { plan = "Plan d'action non disponible pour le moment." });

            var content = await res.Content.ReadAsStringAsync(ct);
            return Content(content, "application/json");
        }
        catch
        {
            return Ok(new { plan = "Plan d'action non disponible pour le moment." });
        }
    }
}
