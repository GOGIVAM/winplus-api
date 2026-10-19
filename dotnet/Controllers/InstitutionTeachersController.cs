using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Phase 2 de l'espace institution : gestion des enseignants.
///
/// GET    /api/institution/teachers                               enseignants affiliés + demandes en attente
/// POST   /api/institution/teachers/groups/{groupId}              affecter un enseignant à une classe { teacherId }
/// DELETE /api/institution/teachers/groups/{groupId}/{teacherId}  retirer un enseignant d'une classe
///
/// L'affiliation elle-même (invitation, acceptation, révocation) reste servie
/// par InstitutionNetworkController (/api/institution-network/affiliation/…).
/// Une classe est un StudyGroup créé par le compte institution ; un enseignant
/// y est un StudyGroupMember de rôle « teacher » (pas de nouvelle table).
/// </summary>
[ApiController]
[Route("api/institution/teachers")]
[Authorize]
public class InstitutionTeachersController : ControllerBase
{
    public const string TeacherRole = "teacher";

    private readonly ApplicationDbContext _db;
    private readonly ILogger<InstitutionTeachersController> _logger;

    public InstitutionTeachersController(ApplicationDbContext db, ILogger<InstitutionTeachersController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private async Task<int?> MyInstitutionIdAsync(int userId) =>
        await _db.Users.Where(u => u.Id == userId).Select(u => u.InstitutionId).FirstOrDefaultAsync();

    /// <summary>Enseignants affiliés (avec leur activité et leurs classes) et demandes en attente.</summary>
    [HttpGet]
    public async Task<IActionResult> GetTeachers()
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var institutionId = await MyInstitutionIdAsync(me);
            if (institutionId == null)
                return Ok(new { data = new { teachers = Array.Empty<object>(), received = Array.Empty<object>(), sent = Array.Empty<object>() }, success = true });

            var links = await _db.InstitutionTeacherLinks.AsNoTracking()
                .Where(l => l.InstitutionId == institutionId)
                .Select(l => new
                {
                    l.Id, l.Status, l.InitiatedBy, l.CreatedAt, l.TeacherId,
                    firstName = l.Teacher != null ? l.Teacher.FirstName : null,
                    lastName = l.Teacher != null ? l.Teacher.LastName : null,
                    email = l.Teacher != null ? l.Teacher.Email : null,
                    avatarUrl = l.Teacher != null ? l.Teacher.AvatarUrl : null,
                    lastLoginAt = l.Teacher != null ? l.Teacher.LastLoginAt : null,
                })
                .ToListAsync();

            var accepted = links.Where(l => l.Status == "accepted").ToList();
            var teacherIds = accepted.Select(l => l.TeacherId).ToList();

            var contents = await _db.Subjects.AsNoTracking()
                .Where(s => s.AuthorUserId != null && teacherIds.Contains(s.AuthorUserId.Value))
                .GroupBy(s => s.AuthorUserId!.Value)
                .Select(g => new { id = g.Key, total = g.Count(), published = g.Count(s => s.IsPublished) })
                .ToDictionaryAsync(x => x.id);

            var quizzes = await _db.Quizzes.AsNoTracking()
                .Where(q => q.CreatedByUserId != null && teacherIds.Contains(q.CreatedByUserId.Value))
                .GroupBy(q => q.CreatedByUserId!.Value)
                .Select(g => new { id = g.Key, total = g.Count() })
                .ToDictionaryAsync(x => x.id, x => x.total);

            // Classes de l'établissement où chaque enseignant est affecté.
            var classes = await _db.StudyGroupMembers.AsNoTracking()
                .Where(m => m.Role == TeacherRole && teacherIds.Contains(m.UserId)
                            && m.StudyGroup != null && m.StudyGroup.OwnerId == me && m.StudyGroup.IsActive)
                .Select(m => new { m.UserId, id = m.StudyGroupId, name = m.StudyGroup!.Name })
                .ToListAsync();

            var teachers = accepted
                .OrderBy(l => l.lastName).ThenBy(l => l.firstName)
                .Select(l => new
                {
                    linkId = l.Id,
                    id = l.TeacherId,
                    l.firstName, l.lastName, l.email, l.avatarUrl, l.lastLoginAt,
                    since = l.CreatedAt,
                    contents = contents.TryGetValue(l.TeacherId, out var c) ? c.total : 0,
                    published = contents.TryGetValue(l.TeacherId, out var c2) ? c2.published : 0,
                    quizzes = quizzes.TryGetValue(l.TeacherId, out var q) ? q : 0,
                    classes = classes.Where(x => x.UserId == l.TeacherId).Select(x => new { x.id, x.name }).ToList(),
                })
                .ToList();

            var pending = links.Where(l => l.Status == "pending")
                .Select(l => new
                {
                    linkId = l.Id, id = l.TeacherId, l.firstName, l.lastName, l.email, l.avatarUrl,
                    createdAt = l.CreatedAt, fromMe = l.InitiatedBy == me,
                })
                .ToList();
            var received = pending.Where(p => !p.fromMe).ToList();
            var sent = pending.Where(p => p.fromMe).ToList();

            return Ok(new { data = new { teachers, received, sent }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institution teachers");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    public record AssignTeacherRequest(int TeacherId);

    /// <summary>Affecte un enseignant affilié à une classe de l'établissement.</summary>
    [HttpPost("groups/{groupId:int}")]
    public async Task<IActionResult> AssignToGroup([FromRoute] int groupId, [FromBody] AssignTeacherRequest req)
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var institutionId = await MyInstitutionIdAsync(me);

            var group = await _db.StudyGroups.FirstOrDefaultAsync(g => g.Id == groupId && g.OwnerId == me && g.IsActive);
            if (group == null) return NotFound(new { error = "Classe introuvable." });

            var affiliated = institutionId != null && await _db.InstitutionTeacherLinks
                .AnyAsync(l => l.InstitutionId == institutionId && l.TeacherId == req.TeacherId && l.Status == "accepted");
            if (!affiliated) return BadRequest(new { error = "Cet enseignant n'est pas affilié à votre établissement." });

            var existing = await _db.StudyGroupMembers.FirstOrDefaultAsync(m => m.StudyGroupId == groupId && m.UserId == req.TeacherId);
            if (existing != null)
            {
                if (existing.Role == TeacherRole) return Conflict(new { error = "Déjà enseignant de cette classe." });
                existing.Role = TeacherRole;
            }
            else
            {
                _db.StudyGroupMembers.Add(new StudyGroupMember { StudyGroupId = groupId, UserId = req.TeacherId, Role = TeacherRole });
            }
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning teacher to group");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Retire un enseignant d'une classe (son affiliation reste).</summary>
    [HttpDelete("groups/{groupId:int}/{teacherId:int}")]
    public async Task<IActionResult> RemoveFromGroup([FromRoute] int groupId, [FromRoute] int teacherId)
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var member = await _db.StudyGroupMembers
                .FirstOrDefaultAsync(m => m.StudyGroupId == groupId && m.UserId == teacherId && m.Role == TeacherRole
                                          && m.StudyGroup != null && m.StudyGroup.OwnerId == me);
            if (member == null) return NotFound(new { error = "Affectation introuvable." });
            _db.StudyGroupMembers.Remove(member);
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing teacher from group");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
