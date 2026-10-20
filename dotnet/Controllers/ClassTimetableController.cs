using System.Text.RegularExpressions;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Emploi du temps des classes d'établissement.
///
/// GET    /api/timetable/classes/{groupId}         créneaux d'une classe (établissement, enseignant, élève, parent)
/// POST   /api/timetable/classes/{groupId}/slots   ajouter un créneau (établissement)
/// PUT    /api/timetable/slots/{id}                modifier un créneau (établissement)
/// DELETE /api/timetable/slots/{id}                supprimer un créneau (établissement)
/// GET    /api/timetable/mine                      semaine de l'utilisateur (élève, parent, enseignant)
/// </summary>
[ApiController]
[Route("api/timetable")]
[Authorize]
public class ClassTimetableController : ControllerBase
{
    private static readonly Regex Hhmm = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);
    private readonly ApplicationDbContext _db;

    public ClassTimetableController(ApplicationDbContext db) => _db = db;

    public record SlotRequest(int DayOfWeek, string StartTime, string EndTime, string Subject, int? TeacherUserId, string? Room);

    private async Task<bool> OwnsAsync(int groupId, int me) =>
        await _db.StudyGroups.AnyAsync(g => g.Id == groupId && g.OwnerId == me && g.IsActive);

    private async Task<bool> CanReadAsync(int groupId, int me)
    {
        if (await OwnsAsync(groupId, me)) return true;
        if (await _db.StudyGroupMembers.AnyAsync(m => m.StudyGroupId == groupId && m.UserId == me)) return true;
        var children = await _db.ParentStudentLinks.Where(l => l.ParentId == me && l.Status == "accepted").Select(l => l.StudentId).ToListAsync();
        return children.Count > 0 && await _db.StudyGroupMembers.AnyAsync(m => m.StudyGroupId == groupId && children.Contains(m.UserId));
    }

    private async Task<string?> ValidateAsync(int groupId, SlotRequest req, int? exceptId)
    {
        if (req.DayOfWeek is < 1 or > 7) return "Jour invalide.";
        if (!Hhmm.IsMatch(req.StartTime ?? "") || !Hhmm.IsMatch(req.EndTime ?? "")) return "Heures au format HH:mm.";
        if (string.CompareOrdinal(req.EndTime, req.StartTime) <= 0) return "La fin doit suivre le début.";
        if (string.IsNullOrWhiteSpace(req.Subject) || req.Subject.Trim().Length > 100) return "Indiquez la matière.";
        if ((req.Room?.Length ?? 0) > 50) return "Nom de salle trop long.";
        if (req.TeacherUserId != null && !await _db.StudyGroupMembers.AnyAsync(m => m.StudyGroupId == groupId && m.UserId == req.TeacherUserId && m.Role == InstitutionTeachersController.TeacherRole))
            return "Cet enseignant n'est pas affecté à la classe.";
        // Chevauchement dans la même classe
        var overlap = await _db.ClassTimetableSlots.AnyAsync(s => s.StudyGroupId == groupId && s.DayOfWeek == req.DayOfWeek && s.Id != (exceptId ?? 0)
            && string.Compare(s.StartTime, req.EndTime) < 0 && string.Compare(req.StartTime, s.EndTime) < 0);
        return overlap ? "Ce créneau chevauche un autre cours de la classe." : null;
    }

    private static object Map(ClassTimetableSlot s) => new
    {
        s.Id, s.StudyGroupId, s.DayOfWeek, s.StartTime, s.EndTime, s.Subject, s.Room, s.TeacherUserId,
        teacher = s.Teacher == null ? null : $"{s.Teacher.FirstName} {s.Teacher.LastName}".Trim(),
        groupName = s.StudyGroup?.Name,
    };

    [HttpGet("classes/{groupId:int}")]
    public async Task<IActionResult> List([FromRoute] int groupId)
    {
        if (!await CanReadAsync(groupId, User.GetUserId())) return NotFound(new { error = "Classe introuvable." });
        var rows = await _db.ClassTimetableSlots.AsNoTracking().Include(s => s.Teacher).Include(s => s.StudyGroup)
            .Where(s => s.StudyGroupId == groupId).OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime).ToListAsync();
        return Ok(new { data = rows.Select(Map), success = true });
    }

    [HttpPost("classes/{groupId:int}/slots")]
    public async Task<IActionResult> Create([FromRoute] int groupId, [FromBody] SlotRequest req)
    {
        if (!await OwnsAsync(groupId, User.GetUserId())) return Forbid();
        var error = await ValidateAsync(groupId, req, null);
        if (error != null) return BadRequest(new { error });
        var slot = new ClassTimetableSlot
        {
            StudyGroupId = groupId, DayOfWeek = req.DayOfWeek, StartTime = req.StartTime, EndTime = req.EndTime,
            Subject = req.Subject.Trim(), TeacherUserId = req.TeacherUserId, Room = string.IsNullOrWhiteSpace(req.Room) ? null : req.Room.Trim(),
        };
        _db.ClassTimetableSlots.Add(slot);
        await _db.SaveChangesAsync();
        return Ok(new { data = new { slot.Id }, success = true });
    }

    [HttpPut("slots/{id:int}")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] SlotRequest req)
    {
        var slot = await _db.ClassTimetableSlots.FirstOrDefaultAsync(s => s.Id == id);
        if (slot == null) return NotFound(new { error = "Créneau introuvable." });
        if (!await OwnsAsync(slot.StudyGroupId, User.GetUserId())) return Forbid();
        var error = await ValidateAsync(slot.StudyGroupId, req, id);
        if (error != null) return BadRequest(new { error });
        slot.DayOfWeek = req.DayOfWeek; slot.StartTime = req.StartTime; slot.EndTime = req.EndTime;
        slot.Subject = req.Subject.Trim(); slot.TeacherUserId = req.TeacherUserId;
        slot.Room = string.IsNullOrWhiteSpace(req.Room) ? null : req.Room.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete("slots/{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        var slot = await _db.ClassTimetableSlots.FirstOrDefaultAsync(s => s.Id == id);
        if (slot == null) return NotFound(new { error = "Créneau introuvable." });
        if (!await OwnsAsync(slot.StudyGroupId, User.GetUserId())) return Forbid();
        _db.ClassTimetableSlots.Remove(slot);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        var me = User.GetUserId();
        var role = User.GetUserRole();
        IQueryable<ClassTimetableSlot> q = _db.ClassTimetableSlots.AsNoTracking().Include(s => s.Teacher).Include(s => s.StudyGroup);
        if (role == "teacher")
        {
            var groups = await _db.StudyGroupMembers.Where(m => m.UserId == me && m.Role == InstitutionTeachersController.TeacherRole)
                .Select(m => m.StudyGroupId).ToListAsync();
            q = q.Where(s => s.TeacherUserId == me || (s.TeacherUserId == null && groups.Contains(s.StudyGroupId)));
        }
        else
        {
            var students = role == "parent"
                ? await _db.ParentStudentLinks.Where(l => l.ParentId == me && l.Status == "accepted").Select(l => l.StudentId).ToListAsync()
                : new List<int> { me };
            var groups = await _db.StudyGroupMembers.Where(m => students.Contains(m.UserId) && m.Role != InstitutionTeachersController.TeacherRole)
                .Select(m => m.StudyGroupId).Distinct().ToListAsync();
            q = q.Where(s => groups.Contains(s.StudyGroupId));
        }
        var rows = await q.OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime).Take(300).ToListAsync();
        return Ok(new { data = rows.Select(Map), success = true });
    }
}
