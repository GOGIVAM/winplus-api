using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Rendez-vous parent-professeur.
///
/// Enseignant :
///   GET    /api/meetings/slots/mine          ses créneaux à venir (et leur réservation)
///   POST   /api/meetings/slots               publier une série de créneaux consécutifs
///   DELETE /api/meetings/slots/{id}          retirer un créneau (le parent est prévenu s'il était réservé)
/// Parent :
///   GET    /api/meetings/teachers            enseignants des classes de ses enfants
///   GET    /api/meetings/teachers/{id}/slots créneaux libres d'un enseignant
///   POST   /api/meetings/book                réserver { slotId, childId, topic }
/// Les deux :
///   GET    /api/meetings/mine                rendez-vous à venir
///   POST   /api/meetings/{id}/cancel         annuler (l'autre partie est prévenue)
/// </summary>
[ApiController]
[Route("api/meetings")]
[Authorize]
public class ParentTeacherMeetingsController : ControllerBase
{
    private static readonly string[] Modes = { "presentiel", "visio", "telephone" };
    private readonly ApplicationDbContext _db;
    private readonly INtfyService _ntfy;
    private readonly ILogger<ParentTeacherMeetingsController> _logger;

    public ParentTeacherMeetingsController(ApplicationDbContext db, INtfyService ntfy, ILogger<ParentTeacherMeetingsController> logger)
    {
        _db = db;
        _ntfy = ntfy;
        _logger = logger;
    }

    public record CreateSlotsRequest(DateTime StartsAt, int Count, int DurationMinutes, string Mode, string? Location);
    public record BookRequest(int SlotId, int ChildId, string? Topic);

    private static string Fmt(DateTime utc) => (utc + TimeSpan.FromHours(1)).ToString("dddd d MMMM 'à' HH'h'mm", new System.Globalization.CultureInfo("fr-FR"));
    private static string Name(User? u) => u == null ? "" : $"{u.FirstName} {u.LastName}".Trim();

    private async Task Notify(int userId, string title, string message, int meetingId)
    {
        try { await _ntfy.PublishAsync($"winplus-user-{userId}", title, message, userId: userId, type: "ParentTeacherMeeting", relatedEntityType: "ParentTeacherMeeting", relatedEntityId: meetingId); }
        catch (Exception ex) { _logger.LogWarning(ex, "Rendez-vous {MeetingId} : notification non envoyée à {UserId}", meetingId, userId); }
    }

    /// <summary>Enfants du parent qui sont élèves d'une classe où l'enseignant est affecté.</summary>
    private async Task<List<int>> SharedChildrenAsync(int parentId, int teacherId)
    {
        var children = await _db.ParentStudentLinks.Where(l => l.ParentId == parentId && l.Status == "accepted").Select(l => l.StudentId).ToListAsync();
        var teacherGroups = await _db.StudyGroupMembers.Where(m => m.UserId == teacherId && m.Role == InstitutionTeachersController.TeacherRole).Select(m => m.StudyGroupId).ToListAsync();
        return await _db.StudyGroupMembers.Where(m => teacherGroups.Contains(m.StudyGroupId) && children.Contains(m.UserId)).Select(m => m.UserId).Distinct().ToListAsync();
    }

    // ── Enseignant ──────────────────────────────────────────────────────────
    [HttpGet("slots/mine")]
    public async Task<IActionResult> MySlots()
    {
        var me = User.GetUserId();
        var now = DateTime.UtcNow.AddHours(-1);
        var slots = await _db.TeacherMeetingSlots.AsNoTracking()
            .Where(s => s.TeacherUserId == me && !s.IsDeleted && s.StartsAt >= now)
            .OrderBy(s => s.StartsAt).Take(200).ToListAsync();
        var ids = slots.Select(s => s.Id).ToList();
        var booked = await _db.ParentTeacherMeetings.AsNoTracking().Include(m => m.Parent).Include(m => m.Child)
            .Where(m => ids.Contains(m.SlotId) && m.Status == "booked").ToListAsync();
        return Ok(new
        {
            data = slots.Select(s =>
            {
                var b = booked.FirstOrDefault(m => m.SlotId == s.Id);
                return new
                {
                    s.Id, s.StartsAt, s.DurationMinutes, s.Mode, s.Location,
                    meeting = b == null ? null : new { b.Id, parent = Name(b.Parent), child = Name(b.Child), b.Topic },
                };
            }),
            success = true,
        });
    }

    [HttpPost("slots")]
    public async Task<IActionResult> CreateSlots([FromBody] CreateSlotsRequest req)
    {
        if (User.GetUserRole() != "teacher") return Forbid();
        var me = User.GetUserId();
        if (req.Count is < 1 or > 24) return BadRequest(new { error = "Entre 1 et 24 créneaux à la fois." });
        if (req.DurationMinutes is < 5 or > 120) return BadRequest(new { error = "Durée entre 5 et 120 minutes." });
        if (!Modes.Contains(req.Mode)) return BadRequest(new { error = "Mode invalide." });
        if (req.StartsAt <= DateTime.UtcNow) return BadRequest(new { error = "Le premier créneau doit être dans le futur." });

        var end = req.StartsAt.AddMinutes(req.DurationMinutes * req.Count);
        var clash = await _db.TeacherMeetingSlots.AnyAsync(s => s.TeacherUserId == me && !s.IsDeleted
            && s.StartsAt < end && s.StartsAt.AddMinutes(s.DurationMinutes) > req.StartsAt);
        if (clash) return Conflict(new { error = "Ces créneaux chevauchent des créneaux déjà publiés." });

        for (var i = 0; i < req.Count; i++)
        {
            _db.TeacherMeetingSlots.Add(new TeacherMeetingSlot
            {
                TeacherUserId = me, StartsAt = req.StartsAt.AddMinutes(i * req.DurationMinutes), DurationMinutes = req.DurationMinutes,
                Mode = req.Mode, Location = string.IsNullOrWhiteSpace(req.Location) ? null : req.Location.Trim(),
            });
        }
        await _db.SaveChangesAsync();
        return Ok(new { data = new { created = req.Count }, success = true });
    }

    [HttpDelete("slots/{id:int}")]
    public async Task<IActionResult> DeleteSlot([FromRoute] int id)
    {
        var me = User.GetUserId();
        var slot = await _db.TeacherMeetingSlots.FirstOrDefaultAsync(s => s.Id == id && s.TeacherUserId == me && !s.IsDeleted);
        if (slot == null) return NotFound(new { error = "Créneau introuvable." });
        slot.IsDeleted = true;
        var meeting = await _db.ParentTeacherMeetings.Include(m => m.Teacher).FirstOrDefaultAsync(m => m.SlotId == id && m.Status == "booked");
        if (meeting != null) { meeting.Status = "cancelled"; meeting.CancelledBy = me; meeting.CancelledAt = DateTime.UtcNow; }
        await _db.SaveChangesAsync();
        if (meeting != null)
            await Notify(meeting.ParentUserId, "Rendez-vous annulé", $"{Name(meeting.Teacher)} a annulé le rendez-vous du {Fmt(slot.StartsAt)}. Vous pouvez choisir un autre créneau.", meeting.Id);
        return Ok(new { success = true });
    }

    // ── Parent ──────────────────────────────────────────────────────────────
    [HttpGet("teachers")]
    public async Task<IActionResult> Teachers()
    {
        var me = User.GetUserId();
        var children = await _db.ParentStudentLinks.AsNoTracking().Where(l => l.ParentId == me && l.Status == "accepted")
            .Select(l => new { l.StudentId, first = l.Student != null ? l.Student.FirstName : null }).ToListAsync();
        var childIds = children.Select(c => c.StudentId).ToList();
        var childGroups = await _db.StudyGroupMembers.AsNoTracking().Where(m => childIds.Contains(m.UserId) && m.Role != InstitutionTeachersController.TeacherRole)
            .Select(m => new { m.UserId, m.StudyGroupId, name = m.StudyGroup != null ? m.StudyGroup.Name : "" }).ToListAsync();
        var groupIds = childGroups.Select(g => g.StudyGroupId).Distinct().ToList();
        var teachers = await _db.StudyGroupMembers.AsNoTracking()
            .Where(m => groupIds.Contains(m.StudyGroupId) && m.Role == InstitutionTeachersController.TeacherRole)
            .Select(m => new { m.UserId, m.StudyGroupId, m.User!.FirstName, m.User.LastName, m.User.AvatarUrl }).ToListAsync();
        var now = DateTime.UtcNow;
        var teacherIds = teachers.Select(t => t.UserId).Distinct().ToList();
        var freeCounts = await _db.TeacherMeetingSlots.AsNoTracking()
            .Where(s => teacherIds.Contains(s.TeacherUserId) && !s.IsDeleted && s.StartsAt > now
                        && !_db.ParentTeacherMeetings.Any(m => m.SlotId == s.Id && m.Status == "booked"))
            .GroupBy(s => s.TeacherUserId).Select(g => new { id = g.Key, count = g.Count(), next = g.Min(s => s.StartsAt) }).ToListAsync();

        var list = teachers.GroupBy(t => t.UserId).Select(g =>
        {
            var t = g.First();
            var kids = childGroups.Where(c => g.Any(x => x.StudyGroupId == c.StudyGroupId))
                .Select(c => new { id = c.UserId, first = children.First(k => k.StudentId == c.UserId).first, className = c.name })
                .GroupBy(k => k.id).Select(k => k.First()).ToList();
            var free = freeCounts.FirstOrDefault(f => f.id == g.Key);
            return new { id = g.Key, name = $"{t.FirstName} {t.LastName}".Trim(), t.AvatarUrl, children = kids, freeSlots = free?.count ?? 0, nextSlot = free?.next };
        }).OrderByDescending(x => x.freeSlots).ThenBy(x => x.name).ToList();
        return Ok(new { data = list, success = true });
    }

    [HttpGet("teachers/{teacherId:int}/slots")]
    public async Task<IActionResult> FreeSlots([FromRoute] int teacherId)
    {
        var me = User.GetUserId();
        if ((await SharedChildrenAsync(me, teacherId)).Count == 0) return NotFound(new { error = "Enseignant introuvable." });
        var now = DateTime.UtcNow;
        var slots = await _db.TeacherMeetingSlots.AsNoTracking()
            .Where(s => s.TeacherUserId == teacherId && !s.IsDeleted && s.StartsAt > now
                        && !_db.ParentTeacherMeetings.Any(m => m.SlotId == s.Id && m.Status == "booked"))
            .OrderBy(s => s.StartsAt).Take(60)
            .Select(s => new { s.Id, s.StartsAt, s.DurationMinutes, s.Mode, s.Location }).ToListAsync();
        return Ok(new { data = slots, success = true });
    }

    [HttpPost("book")]
    public async Task<IActionResult> Book([FromBody] BookRequest req)
    {
        if (User.GetUserRole() != "parent") return Forbid();
        var me = User.GetUserId();
        var slot = await _db.TeacherMeetingSlots.Include(s => s.Teacher).FirstOrDefaultAsync(s => s.Id == req.SlotId && !s.IsDeleted && s.StartsAt > DateTime.UtcNow);
        if (slot == null) return NotFound(new { error = "Ce créneau n'est plus disponible." });
        if (!(await SharedChildrenAsync(me, slot.TeacherUserId)).Contains(req.ChildId)) return BadRequest(new { error = "Cet enseignant n'enseigne pas à cet enfant." });
        if (await _db.ParentTeacherMeetings.AnyAsync(m => m.SlotId == slot.Id && m.Status == "booked"))
            return Conflict(new { error = "Ce créneau vient d'être réservé. Choisissez-en un autre." });

        var meeting = new ParentTeacherMeeting
        {
            SlotId = slot.Id, TeacherUserId = slot.TeacherUserId, ParentUserId = me, ChildUserId = req.ChildId,
            Topic = string.IsNullOrWhiteSpace(req.Topic) ? null : req.Topic.Trim()[..Math.Min(500, req.Topic.Trim().Length)],
        };
        _db.ParentTeacherMeetings.Add(meeting);
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict(new { error = "Ce créneau vient d'être réservé. Choisissez-en un autre." }); }

        var parent = await _db.Users.FindAsync(me);
        var child = await _db.Users.FindAsync(req.ChildId);
        await Notify(slot.TeacherUserId, "Nouveau rendez-vous parent",
            $"{Name(parent)} (parent de {child?.FirstName}) a réservé le {Fmt(slot.StartsAt)}" + (meeting.Topic != null ? $" : « {meeting.Topic} »." : "."), meeting.Id);
        await Notify(me, "Rendez-vous confirmé", $"Rendez-vous avec {Name(slot.Teacher)} le {Fmt(slot.StartsAt)}.", meeting.Id);
        return Ok(new { data = new { meeting.Id }, success = true });
    }

    // ── Les deux ────────────────────────────────────────────────────────────
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        var me = User.GetUserId();
        var since = DateTime.UtcNow.AddHours(-2);
        var rows = await _db.ParentTeacherMeetings.AsNoTracking()
            .Include(m => m.Slot).Include(m => m.Parent).Include(m => m.Child).Include(m => m.Teacher)
            .Where(m => (m.ParentUserId == me || m.TeacherUserId == me) && m.Status == "booked" && m.Slot != null && m.Slot.StartsAt >= since)
            .OrderBy(m => m.Slot!.StartsAt).Take(50).ToListAsync();
        return Ok(new
        {
            data = rows.Select(m => new
            {
                m.Id, startsAt = m.Slot!.StartsAt, m.Slot.DurationMinutes, m.Slot.Mode, m.Slot.Location, m.Topic,
                teacher = Name(m.Teacher), parent = Name(m.Parent), child = Name(m.Child), asTeacher = m.TeacherUserId == me,
            }),
            success = true,
        });
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel([FromRoute] int id)
    {
        var me = User.GetUserId();
        var m = await _db.ParentTeacherMeetings.Include(x => x.Slot).Include(x => x.Parent).Include(x => x.Teacher)
            .FirstOrDefaultAsync(x => x.Id == id && x.Status == "booked" && (x.ParentUserId == me || x.TeacherUserId == me));
        if (m == null) return NotFound(new { error = "Rendez-vous introuvable." });
        m.Status = "cancelled"; m.CancelledBy = me; m.CancelledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        var other = m.ParentUserId == me ? m.TeacherUserId : m.ParentUserId;
        var who = m.ParentUserId == me ? Name(m.Parent) : Name(m.Teacher);
        await Notify(other, "Rendez-vous annulé", $"{who} a annulé le rendez-vous du {Fmt(m.Slot!.StartsAt)}.", m.Id);
        return Ok(new { success = true });
    }
}
