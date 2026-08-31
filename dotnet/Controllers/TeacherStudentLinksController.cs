using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;

namespace Backend.Controllers;

public record InviteByUserIdRequest(int TargetUserId);

[ApiController]
[Route("api/teacher-links")]
[Authorize]
public class TeacherStudentLinksController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<TeacherStudentLinksController> _logger;

    public TeacherStudentLinksController(ApplicationDbContext db, ILogger<TeacherStudentLinksController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Recherche un utilisateur par email, nom ou téléphone.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return BadRequest(new { error = "Requête trop courte" });

        var me = User.GetUserId();
        var lq = q.ToLower();

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id != me &&
                        (u.Role == "student" || u.Role == "teacher") &&
                        (u.Email.ToLower().Contains(lq) ||
                         (u.FirstName + " " + u.LastName).ToLower().Contains(lq) ||
                         (u.Phone != null && u.Phone.Contains(q))))
            .Select(u => new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                u.Role,
                u.AvatarUrl,
                u.Email,
            })
            .Take(10)
            .ToListAsync();

        return Ok(users);
    }

    /// <summary>Envoie une invitation de liaison prof-élève.</summary>
    [HttpPost("invite")]
    public async Task<IActionResult> Invite([FromBody] InviteByUserIdRequest req)
    {
        var me = User.GetUserId();
        var myRole = User.GetUserRole();

        if (me == req.TargetUserId)
            return BadRequest(new { error = "Impossible de se lier à soi-même" });

        var target = await _db.Users.FindAsync(req.TargetUserId);
        if (target == null)
            return NotFound(new { error = "Utilisateur introuvable" });

        // L'un doit être prof, l'autre élève
        var targetRole = target.Role;
        bool valid = (myRole == "teacher" && targetRole == "student") ||
                     (myRole == "student" && targetRole == "teacher");
        if (!valid)
            return BadRequest(new { error = "La liaison doit être entre un professeur et un élève" });

        var teacherId = myRole == "teacher" ? me : req.TargetUserId;
        var studentId = myRole == "student" ? me : req.TargetUserId;

        // Vérifier si liaison existante
        var existing = await _db.TeacherStudentLinks
            .FirstOrDefaultAsync(l => l.TeacherId == teacherId && l.StudentId == studentId);

        if (existing != null)
        {
            if (existing.Status == "accepted")
                return Conflict(new { error = "Liaison déjà active" });
            if (existing.Status == "pending")
                return Conflict(new { error = "Invitation déjà envoyée" });
            // Si rejected, on réinitialise
            existing.Status = "pending";
            existing.InitiatedBy = me;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.TeacherStudentLinks.Add(new TeacherStudentLink
            {
                TeacherId = teacherId,
                StudentId = studentId,
                Status = "pending",
                InitiatedBy = me,
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    /// <summary>Liste les invitations reçues et en attente.</summary>
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        var me = User.GetUserId();
        var myRole = User.GetUserRole();

        IQueryable<TeacherStudentLink> query = _db.TeacherStudentLinks
            .AsNoTracking()
            .Where(l => l.Status == "pending" && l.InitiatedBy != me);

        if (myRole == "teacher")
            query = query.Where(l => l.TeacherId == me);
        else
            query = query.Where(l => l.StudentId == me);

        var pending = await query
            .Include(l => l.Initiator)
            .Select(l => new
            {
                l.Id,
                l.Status,
                l.CreatedAt,
                Initiator = new
                {
                    l.Initiator!.Id,
                    l.Initiator.FirstName,
                    l.Initiator.LastName,
                    l.Initiator.Role,
                    l.Initiator.AvatarUrl,
                },
            })
            .ToListAsync();

        return Ok(pending);
    }

    /// <summary>Accepte une invitation.</summary>
    [HttpPut("{id:int}/accept")]
    public async Task<IActionResult> Accept(int id)
    {
        var me = User.GetUserId();
        var link = await _db.TeacherStudentLinks.FindAsync(id);

        if (link == null) return NotFound();
        if (link.InitiatedBy == me)
            return BadRequest(new { error = "Vous ne pouvez pas accepter votre propre invitation" });
        if (link.TeacherId != me && link.StudentId != me)
            return Forbid();

        link.Status = "accepted";
        link.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    /// <summary>Rejette une invitation.</summary>
    [HttpPut("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id)
    {
        var me = User.GetUserId();
        var link = await _db.TeacherStudentLinks.FindAsync(id);

        if (link == null) return NotFound();
        if (link.TeacherId != me && link.StudentId != me)
            return Forbid();

        link.Status = "rejected";
        link.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    /// <summary>Liste les liaisons actives de l'utilisateur.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine()
    {
        var me = User.GetUserId();
        var myRole = User.GetUserRole();

        List<object> result;

        if (myRole == "teacher")
        {
            var links = await _db.TeacherStudentLinks
                .AsNoTracking()
                .Where(l => l.TeacherId == me && l.Status == "accepted")
                .Include(l => l.Student)
                .Select(l => new
                {
                    l.Id,
                    l.CreatedAt,
                    Other = new { l.Student!.Id, l.Student.FirstName, l.Student.LastName, l.Student.AvatarUrl, Role = "student" },
                })
                .ToListAsync();
            result = links.Cast<object>().ToList();
        }
        else
        {
            var links = await _db.TeacherStudentLinks
                .AsNoTracking()
                .Where(l => l.StudentId == me && l.Status == "accepted")
                .Include(l => l.Teacher)
                .Select(l => new
                {
                    l.Id,
                    l.CreatedAt,
                    Other = new { l.Teacher!.Id, l.Teacher.FirstName, l.Teacher.LastName, l.Teacher.AvatarUrl, Role = "teacher" },
                })
                .ToListAsync();
            result = links.Cast<object>().ToList();
        }

        return Ok(result);
    }

    /// <summary>Supprime une liaison.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var me = User.GetUserId();
        var link = await _db.TeacherStudentLinks.FindAsync(id);

        if (link == null) return NotFound();
        if (link.TeacherId != me && link.StudentId != me)
            return Forbid();

        _db.TeacherStudentLinks.Remove(link);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}
