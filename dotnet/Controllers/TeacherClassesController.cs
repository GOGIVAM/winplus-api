using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;

namespace Backend.Controllers;

public class UpsertTeacherClassRequest
{
    public string? Name { get; set; }
    public string? Level { get; set; }
    public string? AcademicYear { get; set; }
    public string? Description { get; set; }
}

public class AddClassStudentRequest
{
    public int? StudentId { get; set; }
    public string? Email { get; set; }
}

/// <summary>
/// Gestion des classes d'un enseignant (S4-4).
/// POST   /api/teacher/classes
/// PATCH  /api/teacher/classes/{id}
/// DELETE /api/teacher/classes/{id}
/// GET    /api/teacher/classes/{id}/students
/// POST   /api/teacher/classes/{id}/students
/// DELETE /api/teacher/classes/{id}/students/{studentId}
/// </summary>
[ApiController]
[Route("api/teacher/classes")]
[Authorize]
public class TeacherClassesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<TeacherClassesController> _logger;

    public TeacherClassesController(ApplicationDbContext db, ILogger<TeacherClassesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertTeacherClassRequest request)
    {
        try
        {
            var name = (request.Name ?? "").Trim();
            if (name.Length is < 2 or > 150)
                return BadRequest(new { success = false, error = "Le nom de la classe doit contenir entre 2 et 150 caractères." });

            var teacherId = User.GetUserId();

            var duplicate = await _db.TeacherClasses
                .AnyAsync(c => c.TeacherId == teacherId && c.IsActive && c.Name == name);
            if (duplicate)
                return BadRequest(new { success = false, error = "Vous avez déjà une classe portant ce nom." });

            var klass = new TeacherClass
            {
                TeacherId    = teacherId,
                Name         = name,
                Level        = string.IsNullOrWhiteSpace(request.Level) ? null : request.Level!.Trim(),
                AcademicYear = string.IsNullOrWhiteSpace(request.AcademicYear) ? null : request.AcademicYear!.Trim(),
                Description  = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description!.Trim()
            };

            _db.TeacherClasses.Add(klass);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                data = new { klass.Id, klass.Name, klass.Level, klass.AcademicYear, klass.Description, klass.StudentCount, klass.CreatedAt },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating teacher class");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPatch("{id:int}")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] UpsertTeacherClassRequest request)
    {
        try
        {
            var teacherId = User.GetUserId();
            var klass = await _db.TeacherClasses.FirstOrDefaultAsync(c => c.Id == id && c.TeacherId == teacherId);
            if (klass == null) return NotFound(new { success = false, error = "Classe introuvable." });

            if (request.Name != null)
            {
                var name = request.Name.Trim();
                if (name.Length is < 2 or > 150)
                    return BadRequest(new { success = false, error = "Nom de classe invalide." });
                klass.Name = name;
            }
            if (request.Level != null) klass.Level = request.Level.Trim();
            if (request.AcademicYear != null) klass.AcademicYear = request.AcademicYear.Trim();
            if (request.Description != null) klass.Description = request.Description.Trim();

            klass.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new { data = new { klass.Id, klass.Name, klass.Level, klass.AcademicYear, klass.Description, klass.StudentCount }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating teacher class {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        try
        {
            var teacherId = User.GetUserId();
            var klass = await _db.TeacherClasses.FirstOrDefaultAsync(c => c.Id == id && c.TeacherId == teacherId);
            if (klass == null) return NoContent();

            klass.IsActive = false;
            klass.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting teacher class {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Élèves de la classe, avec leur score moyen réel.</summary>
    [HttpGet("{id:int}/students")]
    public async Task<IActionResult> GetStudents([FromRoute] int id)
    {
        try
        {
            var teacherId = User.GetUserId();
            var owns = await _db.TeacherClasses.AnyAsync(c => c.Id == id && c.TeacherId == teacherId);
            if (!owns) return StatusCode(403, new { success = false, error = "Classe non autorisée." });

            var students = await _db.TeacherClassStudents.AsNoTracking()
                .Where(cs => cs.TeacherClassId == id)
                .OrderBy(cs => cs.Student!.LastName)
                .Select(cs => new
                {
                    studentId = cs.StudentId,
                    firstName = cs.Student!.FirstName,
                    lastName  = cs.Student.LastName,
                    email     = cs.Student.Email,
                    level     = cs.Student.Level,
                    avatarUrl = cs.Student.AvatarUrl,
                    addedAt   = cs.AddedAt,
                    avgScore  = _db.QuizAttempts
                        .Where(a => a.UserId == cs.StudentId && a.IsCompleted)
                        .Average(a => (decimal?)a.Score)
                })
                .ToListAsync();

            var classAvg = students.Where(s => s.avgScore.HasValue).Select(s => (double)s.avgScore!.Value).ToList();

            return Ok(new
            {
                data = students,
                classAverage = classAvg.Count == 0 ? (int?)null : (int)Math.Round(classAvg.Average()),
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting class students {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPost("{id:int}/students")]
    public async Task<IActionResult> AddStudent([FromRoute] int id, [FromBody] AddClassStudentRequest request)
    {
        try
        {
            var teacherId = User.GetUserId();
            var klass = await _db.TeacherClasses.FirstOrDefaultAsync(c => c.Id == id && c.TeacherId == teacherId);
            if (klass == null) return StatusCode(403, new { success = false, error = "Classe non autorisée." });

            int? studentId = request.StudentId;
            if (studentId == null && !string.IsNullOrWhiteSpace(request.Email))
            {
                var email = request.Email.Trim().ToLowerInvariant();
                studentId = await _db.Users
                    .Where(u => u.Email.ToLower() == email && !u.IsDeleted)
                    .Select(u => (int?)u.Id)
                    .FirstOrDefaultAsync();
                if (studentId == null)
                    return NotFound(new { success = false, error = "Aucun compte WinPlus avec cet email." });
            }

            if (studentId == null)
                return BadRequest(new { success = false, error = "studentId ou email requis." });

            var already = await _db.TeacherClassStudents
                .AnyAsync(cs => cs.TeacherClassId == id && cs.StudentId == studentId);
            if (!already)
            {
                _db.TeacherClassStudents.Add(new TeacherClassStudent { TeacherClassId = id, StudentId = studentId.Value });
                await _db.SaveChangesAsync();
            }

            klass.StudentCount = await _db.TeacherClassStudents.CountAsync(cs => cs.TeacherClassId == id);
            klass.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new { data = new { studentId, klass.StudentCount, alreadyMember = already }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding student to class {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpDelete("{id:int}/students/{studentId:int}")]
    public async Task<IActionResult> RemoveStudent([FromRoute] int id, [FromRoute] int studentId)
    {
        try
        {
            var teacherId = User.GetUserId();
            var klass = await _db.TeacherClasses.FirstOrDefaultAsync(c => c.Id == id && c.TeacherId == teacherId);
            if (klass == null) return StatusCode(403, new { success = false, error = "Classe non autorisée." });

            var link = await _db.TeacherClassStudents
                .FirstOrDefaultAsync(cs => cs.TeacherClassId == id && cs.StudentId == studentId);
            if (link != null)
            {
                _db.TeacherClassStudents.Remove(link);
                await _db.SaveChangesAsync();
            }

            klass.StudentCount = await _db.TeacherClassStudents.CountAsync(cs => cs.TeacherClassId == id);
            klass.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing student from class {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
