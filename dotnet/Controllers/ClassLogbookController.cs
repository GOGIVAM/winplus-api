using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Cahier de textes numérique des classes d'établissement.
///
/// GET    /api/logbook/teacher/classes        classes où l'enseignant est affecté
/// GET    /api/logbook/classes/{groupId}      entrées d'une classe (enseignant, établissement, élève, parent)
/// POST   /api/logbook/classes/{groupId}      nouvelle entrée (enseignant affecté ou établissement)
/// PUT    /api/logbook/entries/{id}           modifier (auteur ou établissement)
/// DELETE /api/logbook/entries/{id}           supprimer (auteur ou établissement)
/// GET    /api/logbook/mine                   cahier de textes récent (élève : ses classes ; parent : celles de ses enfants)
/// </summary>
[ApiController]
[Route("api/logbook")]
[Authorize]
public class ClassLogbookController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ClassLogbookController> _logger;
    private readonly IServiceScopeFactory _scopes;

    public ClassLogbookController(ApplicationDbContext db, ILogger<ClassLogbookController> logger, IServiceScopeFactory scopes)
    {
        _db = db;
        _logger = logger;
        _scopes = scopes;
    }

    public record EntryRequest(string Subject, DateTime LessonDate, string Content, string? HomeworkText, DateTime? HomeworkDue);

    private enum Access { None, Read, Write, Owner }

    private async Task<Access> AccessAsync(int groupId, int userId)
    {
        var group = await _db.StudyGroups.AsNoTracking().Where(g => g.Id == groupId && g.IsActive)
            .Select(g => new { g.OwnerId }).FirstOrDefaultAsync();
        if (group == null) return Access.None;
        if (group.OwnerId == userId) return Access.Owner;
        var member = await _db.StudyGroupMembers.AsNoTracking()
            .Where(m => m.StudyGroupId == groupId && m.UserId == userId).Select(m => m.Role).FirstOrDefaultAsync();
        if (member == InstitutionTeachersController.TeacherRole) return Access.Write;
        if (member != null) return Access.Read;
        var childIds = await _db.ParentStudentLinks.AsNoTracking()
            .Where(l => l.ParentId == userId && l.Status == "accepted").Select(l => l.StudentId).ToListAsync();
        if (childIds.Count > 0 && await _db.StudyGroupMembers.AnyAsync(m => m.StudyGroupId == groupId && childIds.Contains(m.UserId)))
            return Access.Read;
        return Access.None;
    }

    private static object Map(ClassLogbookEntry e, int me) => new
    {
        e.Id, e.StudyGroupId, e.Subject, e.LessonDate, e.Content, e.HomeworkText, e.HomeworkDue, e.CreatedAt,
        groupName = e.StudyGroup?.Name,
        author = e.Author == null ? null : $"{e.Author.FirstName} {e.Author.LastName}".Trim(),
        mine = e.AuthorUserId == me,
    };

    private static string? Validate(EntryRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Subject) || req.Subject.Trim().Length > 100) return "Indiquez la matière.";
        if (string.IsNullOrWhiteSpace(req.Content)) return "Décrivez ce qui a été fait en cours.";
        if (req.Content.Length > 4000 || (req.HomeworkText?.Length ?? 0) > 2000) return "Texte trop long.";
        if (req.LessonDate.Date > DateTime.UtcNow.Date.AddDays(1)) return "La date du cours ne peut pas être dans le futur.";
        if (req.HomeworkDue != null && req.HomeworkDue.Value.Date < req.LessonDate.Date) return "La date du travail doit suivre le cours.";
        return null;
    }

    [HttpGet("teacher/classes")]
    public async Task<IActionResult> TeacherClasses()
    {
        var me = User.GetUserId();
        var rows = await _db.StudyGroupMembers.AsNoTracking()
            .Where(m => m.UserId == me && m.Role == InstitutionTeachersController.TeacherRole && m.StudyGroup != null && m.StudyGroup.IsActive)
            .Select(m => new
            {
                id = m.StudyGroupId, name = m.StudyGroup!.Name, level = m.StudyGroup.Subject, ownerId = m.StudyGroup.OwnerId,
                students = m.StudyGroup.Members.Count(x => x.Role != InstitutionTeachersController.TeacherRole && x.UserId != m.StudyGroup.OwnerId),
            })
            .ToListAsync();
        var ownerIds = rows.Select(r => r.ownerId).Distinct().ToList();
        var owners = await _db.Users.AsNoTracking().Where(u => ownerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.InstitutionId }).ToListAsync();
        var instIds = owners.Where(o => o.InstitutionId != null).Select(o => o.InstitutionId!.Value).Distinct().ToList();
        var instNames = await _db.Institutions.AsNoTracking().Where(i => instIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name);
        var classes = rows.Select(r =>
        {
            var instId = owners.FirstOrDefault(o => o.Id == r.ownerId)?.InstitutionId;
            return new { r.id, r.name, r.level, r.students, institutionName = instId != null ? instNames.GetValueOrDefault(instId.Value) : null };
        }).ToList();
        return Ok(new { data = classes, success = true });
    }

    [HttpGet("classes/{groupId:int}")]
    public async Task<IActionResult> List([FromRoute] int groupId, [FromQuery] int take = 60)
    {
        var me = User.GetUserId();
        if (await AccessAsync(groupId, me) == Access.None) return NotFound(new { error = "Classe introuvable." });
        take = Math.Clamp(take, 1, 200);
        var rows = await _db.ClassLogbookEntries.AsNoTracking().Include(e => e.Author).Include(e => e.StudyGroup)
            .Where(e => e.StudyGroupId == groupId && !e.IsDeleted)
            .OrderByDescending(e => e.LessonDate).ThenByDescending(e => e.CreatedAt).Take(take).ToListAsync();
        return Ok(new { data = rows.Select(e => Map(e, me)), success = true });
    }

    [HttpPost("classes/{groupId:int}")]
    public async Task<IActionResult> Create([FromRoute] int groupId, [FromBody] EntryRequest req)
    {
        try
        {
            var me = User.GetUserId();
            var access = await AccessAsync(groupId, me);
            if (access is not (Access.Write or Access.Owner)) return Forbid();
            var error = Validate(req);
            if (error != null) return BadRequest(new { error });

            var entry = new ClassLogbookEntry
            {
                StudyGroupId = groupId, AuthorUserId = me, Subject = req.Subject.Trim(),
                LessonDate = DateTime.SpecifyKind(req.LessonDate.Date, DateTimeKind.Utc),
                Content = req.Content.Trim(),
                HomeworkText = string.IsNullOrWhiteSpace(req.HomeworkText) ? null : req.HomeworkText.Trim(),
                HomeworkDue = req.HomeworkDue == null ? null : DateTime.SpecifyKind(req.HomeworkDue.Value.Date, DateTimeKind.Utc),
            };
            _db.ClassLogbookEntries.Add(entry);
            await _db.SaveChangesAsync();

            // Travail à faire : les élèves de la classe sont prévenus.
            if (entry.HomeworkText != null)
            {
                var groupName = await _db.StudyGroups.Where(g => g.Id == groupId).Select(g => g.Name).FirstOrDefaultAsync() ?? "ta classe";
                var students = await _db.StudyGroupMembers.AsNoTracking()
                    .Where(m => m.StudyGroupId == groupId && m.Role != InstitutionTeachersController.TeacherRole && m.UserId != me)
                    .Select(m => m.UserId).ToListAsync();
                NotifyHomework(students, groupName, entry);
            }
            await _db.Entry(entry).Reference(e => e.Author).LoadAsync();
            await _db.Entry(entry).Reference(e => e.StudyGroup).LoadAsync();
            return Ok(new { data = Map(entry, me), success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating logbook entry");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    private void NotifyHomework(IReadOnlyList<int> students, string groupName, ClassLogbookEntry e)
    {
        if (students.Count == 0) return;
        var scopes = _scopes;
        var logger = _logger;
        var due = e.HomeworkDue != null ? $" Pour le {e.HomeworkDue:dd/MM}." : string.Empty;
        var text = e.HomeworkText!.Length > 140 ? e.HomeworkText[..140] + "…" : e.HomeworkText;
        _ = Task.Run(async () =>
        {
            using var scope = scopes.CreateScope();
            var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();
            foreach (var id in students)
            {
                try
                {
                    await ntfy.PublishAsync($"winplus-user-{id}", $"Travail à faire · {e.Subject} ({groupName})", text + due,
                        userId: id, type: "ClassLogbook", relatedEntityType: "ClassLogbookEntry", relatedEntityId: e.Id);
                }
                catch (Exception ex) { logger.LogWarning(ex, "Cahier de textes : élève {UserId} non prévenu", id); }
            }
        });
    }

    private async Task<(ClassLogbookEntry? entry, bool allowed)> EditableAsync(int id, int me)
    {
        var e = await _db.ClassLogbookEntries.Include(x => x.StudyGroup).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (e == null) return (null, false);
        return (e, e.AuthorUserId == me || e.StudyGroup?.OwnerId == me);
    }

    [HttpPut("entries/{id:int}")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] EntryRequest req)
    {
        var me = User.GetUserId();
        var (e, allowed) = await EditableAsync(id, me);
        if (e == null) return NotFound(new { error = "Entrée introuvable." });
        if (!allowed) return Forbid();
        var error = Validate(req);
        if (error != null) return BadRequest(new { error });
        e.Subject = req.Subject.Trim();
        e.LessonDate = DateTime.SpecifyKind(req.LessonDate.Date, DateTimeKind.Utc);
        e.Content = req.Content.Trim();
        e.HomeworkText = string.IsNullOrWhiteSpace(req.HomeworkText) ? null : req.HomeworkText.Trim();
        e.HomeworkDue = req.HomeworkDue == null ? null : DateTime.SpecifyKind(req.HomeworkDue.Value.Date, DateTimeKind.Utc);
        e.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete("entries/{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        var me = User.GetUserId();
        var (e, allowed) = await EditableAsync(id, me);
        if (e == null) return NotFound(new { error = "Entrée introuvable." });
        if (!allowed) return Forbid();
        e.IsDeleted = true;
        e.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpGet("mine")]
    public async Task<IActionResult> Mine([FromQuery] int days = 14)
    {
        var me = User.GetUserId();
        var role = User.GetUserRole();
        var students = role == "parent"
            ? await _db.ParentStudentLinks.AsNoTracking().Where(l => l.ParentId == me && l.Status == "accepted").Select(l => l.StudentId).ToListAsync()
            : new List<int> { me };
        var groupIds = await _db.StudyGroupMembers.AsNoTracking()
            .Where(m => students.Contains(m.UserId) && m.Role != InstitutionTeachersController.TeacherRole)
            .Select(m => m.StudyGroupId).Distinct().ToListAsync();
        if (groupIds.Count == 0) return Ok(new { data = Array.Empty<object>(), success = true });

        var since = DateTime.UtcNow.Date.AddDays(-Math.Clamp(days, 1, 90));
        var rows = await _db.ClassLogbookEntries.AsNoTracking().Include(e => e.Author).Include(e => e.StudyGroup)
            .Where(e => groupIds.Contains(e.StudyGroupId) && !e.IsDeleted
                        && (e.LessonDate >= since || (e.HomeworkDue != null && e.HomeworkDue >= DateTime.UtcNow.Date)))
            .OrderByDescending(e => e.LessonDate).ThenByDescending(e => e.CreatedAt).Take(60).ToListAsync();
        return Ok(new { data = rows.Select(e => Map(e, me)), success = true });
    }
}
