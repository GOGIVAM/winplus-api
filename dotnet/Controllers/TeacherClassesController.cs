using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;

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

public class AssignClassContentRequest
{
    public int SubjectId { get; set; }
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
    private readonly ITeacherService _teacherService;
    private readonly INtfyService _ntfy;

    public TeacherClassesController(ApplicationDbContext db, ILogger<TeacherClassesController> logger, ITeacherService teacherService, INtfyService ntfy)
    {
        _db = db;
        _logger = logger;
        _teacherService = teacherService;
        _ntfy = ntfy;
    }

    /// <summary>
    /// Liste des classes du professeur. Absent jusqu'ici — le frontend
    /// (teacherExtraService.getClasses → GET /api/teacher/classes)
    /// l'appelait déjà, mais "Mes classes" ne pouvait jamais rien afficher
    /// (404 silencieux). Corrigé ici.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false)
    {
        try
        {
            var teacherId = User.GetUserId();
            var classes = await _db.TeacherClasses.AsNoTracking()
                .Where(c => c.TeacherId == teacherId && (c.IsActive || includeInactive))
                .OrderByDescending(c => c.IsActive).ThenByDescending(c => c.CreatedAt)
                .Select(c => new { c.Id, c.Name, c.Level, c.AcademicYear, c.Description, c.StudentCount, c.CreatedAt, c.IsActive })
                .ToListAsync();
            return Ok(classes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing teacher classes");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertTeacherClassRequest request)
    {
        try
        {
            var name = (request.Name ?? "").Trim();
            if (name.Length is < 2 or > 150)
                return BadRequest(new { success = false, error = "Le nom de la classe doit contenir entre 2 et 150 caractères." });
            if (string.IsNullOrWhiteSpace(request.Level))
                return BadRequest(new { success = false, error = "Le niveau est obligatoire." });
            if (string.IsNullOrWhiteSpace(request.AcademicYear))
                return BadRequest(new { success = false, error = "L'année académique est obligatoire." });
            if (request.Description != null && request.Description.Length > 300)
                return BadRequest(new { success = false, error = "La description ne peut pas dépasser 300 caractères." });

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
            if (request.Description != null)
            {
                if (request.Description.Length > 300)
                    return BadRequest(new { success = false, error = "La description ne peut pas dépasser 300 caractères." });
                klass.Description = request.Description.Trim();
            }

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

    /// <summary>Désactive (archive) une classe sans la supprimer — réversible, garde ses élèves et son historique.</summary>
    [HttpPatch("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate([FromRoute] int id, [FromQuery] bool reactivate = false)
    {
        try
        {
            var teacherId = User.GetUserId();
            var klass = await _db.TeacherClasses.FirstOrDefaultAsync(c => c.Id == id && c.TeacherId == teacherId);
            if (klass == null) return NotFound(new { success = false, error = "Classe introuvable." });

            klass.IsActive = reactivate;
            klass.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new { data = new { klass.Id, klass.IsActive }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating teacher class {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Supprime définitivement une classe — uniquement si elle ne contient aucun élève (US-CLA-01).</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        try
        {
            var teacherId = User.GetUserId();
            var klass = await _db.TeacherClasses.FirstOrDefaultAsync(c => c.Id == id && c.TeacherId == teacherId);
            if (klass == null) return NoContent();

            var hasStudents = await _db.TeacherClassStudents.AnyAsync(cs => cs.TeacherClassId == id);
            if (hasStudents)
                return BadRequest(new { success = false, error = "Cette classe contient des élèves — retirez-les d'abord, ou désactivez la classe au lieu de la supprimer." });

            _db.TeacherClasses.Remove(klass);
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

            var roster = await _db.TeacherClassStudents.AsNoTracking()
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
                })
                .ToListAsync();

            var studentIds = roster.Select(r => r.studentId).ToList();
            var attemptsByStudent = await _db.QuizAttempts.AsNoTracking()
                .Where(a => studentIds.Contains(a.UserId) && a.IsCompleted)
                .OrderBy(a => a.StartedAt)
                .Select(a => new { a.UserId, a.Score, a.StartedAt })
                .ToListAsync();
            var grouped = attemptsByStudent.GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.OrderBy(a => a.StartedAt).ToList());

            // Tendance (US-CLA-03) : moyenne de la 2e moitié des tentatives vs
            // la 1ère moitié — nécessite au moins 4 tentatives pour être
            // significatif, sinon "stable" par défaut (pas assez de données).
            static string ComputeTrend(List<decimal> scores)
            {
                if (scores.Count < 4) return "stable";
                var mid = scores.Count / 2;
                var older = scores.Take(mid).Average();
                var recent = scores.Skip(mid).Average();
                if (recent - older >= 5) return "up";
                if (older - recent >= 5) return "down";
                return "stable";
            }

            var students = roster.Select(r =>
            {
                grouped.TryGetValue(r.studentId, out var attempts);
                var scores = attempts?.Select(a => a.Score).ToList() ?? new List<decimal>();
                return new
                {
                    r.studentId, r.firstName, r.lastName, r.email, r.level, r.avatarUrl, r.addedAt,
                    avgScore = scores.Count > 0 ? (decimal?)Math.Round(scores.Average(), 1) : null,
                    attemptCount = scores.Count,
                    trend = ComputeTrend(scores),
                };
            }).ToList();

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

    /// <summary>Fiche détaillée d'un élève de la classe : historique de ses tentatives de quiz (US-CLA-03).</summary>
    [HttpGet("{id:int}/students/{studentId:int}/attempts")]
    public async Task<IActionResult> GetStudentAttempts([FromRoute] int id, [FromRoute] int studentId)
    {
        var teacherId = User.GetUserId();
        var isMember = await _db.TeacherClassStudents
            .AnyAsync(cs => cs.TeacherClassId == id && cs.StudentId == studentId
                && cs.TeacherClass!.TeacherId == teacherId);
        if (!isMember) return StatusCode(403, new { success = false, error = "Élève non trouvé dans cette classe." });

        var attempts = await _db.QuizAttempts.AsNoTracking()
            .Where(a => a.UserId == studentId && a.IsCompleted)
            .OrderByDescending(a => a.StartedAt)
            .Take(50)
            .Select(a => new { a.Id, a.QuizId, a.Score, a.CorrectAnswers, a.Passed, a.StartedAt })
            .ToListAsync();

        var quizIds = attempts.Select(a => a.QuizId).Distinct().ToList();
        var quizTitles = await _db.Quizzes.AsNoTracking()
            .Where(q => quizIds.Contains(q.Id)).ToDictionaryAsync(q => q.Id, q => q.Title);

        var result = attempts.Select(a => new
        {
            a.Id, a.QuizId,
            quizTitle = quizTitles.GetValueOrDefault(a.QuizId),
            a.Score, a.CorrectAnswers, a.Passed, a.StartedAt,
        });

        return Ok(new { data = result, success = true });
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

                var teacherName = await _db.Users.Where(u => u.Id == teacherId)
                    .Select(u => $"{u.FirstName} {u.LastName}".Trim()).FirstOrDefaultAsync();
                await _ntfy.PublishAsync($"winplus-user-{studentId.Value}", "Ajouté(e) à une classe",
                    $"{teacherName ?? "Ton professeur"} t'a ajouté(e) à la classe « {klass.Name} ».",
                    userId: studentId.Value, type: "TeacherClass");
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

    // ── Contenu assigné à la classe (Module 2, US-CAT-04) ─────────────────

    [HttpGet("{id:int}/content")]
    public async Task<IActionResult> GetAssignedContent([FromRoute] int id)
    {
        var teacherId = User.GetUserId();
        var klass = await _db.TeacherClasses.FirstOrDefaultAsync(c => c.Id == id && c.TeacherId == teacherId);
        if (klass == null) return StatusCode(403, new { success = false, error = "Classe non autorisée." });

        var items = await _db.TeacherClassContents
            .AsNoTracking()
            .Where(tcc => tcc.TeacherClassId == id)
            .OrderByDescending(tcc => tcc.AssignedAt)
            .Select(tcc => new
            {
                tcc.Id,
                tcc.SubjectId,
                subjectTitle = tcc.Subject != null ? tcc.Subject.Title : null,
                tcc.PriceChargedXaf,
                tcc.AssignedAt,
            })
            .ToListAsync();

        return Ok(new { data = items, success = true });
    }

    /// <summary>
    /// Assigne un contenu du catalogue à la classe : tous les élèves y
    /// accèdent ensuite sans payer individuellement (voir
    /// SubjectsController.ResolveAccessibleExamAsync, qui vérifie
    /// désormais aussi TeacherClassContent). Le montant est débité une
    /// seule fois au professeur si le contenu n'est pas déjà gratuit,
    /// déjà acheté, ou déjà publié par lui-même.
    ///
    /// ⚠ Pas encore de solde WinPlus dépensable (Module 6) : le montant est
    /// enregistré pour la comptabilité (Revenus &gt; Achats) mais rien ne
    /// bloque encore une assignation faute de solde suffisant.
    /// </summary>
    [HttpPost("{id:int}/content")]
    public async Task<IActionResult> AssignContent([FromRoute] int id, [FromBody] AssignClassContentRequest request)
    {
        try
        {
            var teacherId = User.GetUserId();
            var klass = await _db.TeacherClasses.FirstOrDefaultAsync(c => c.Id == id && c.TeacherId == teacherId);
            if (klass == null) return StatusCode(403, new { success = false, error = "Classe non autorisée." });

            var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.Id == request.SubjectId && !s.IsDeleted);
            if (subject == null) return NotFound(new { success = false, error = "Contenu introuvable." });

            var already = await _db.TeacherClassContents
                .AnyAsync(tcc => tcc.TeacherClassId == id && tcc.SubjectId == subject.Id);
            if (already)
                return Conflict(new { success = false, error = "Ce contenu est déjà assigné à cette classe." });

            var alreadyOwnedByTeacher = subject.Price <= 0
                || subject.AuthorUserId == teacherId
                || await _db.OrderItems.AnyAsync(oi => oi.SubjectId == subject.Id
                    && oi.Order.UserId == teacherId && oi.Order.Status == "completed");

            var priceCharged = alreadyOwnedByTeacher ? 0m : subject.Price;

            if (priceCharged > 0)
            {
                var balance = await _teacherService.GetSpendableBalanceAsync(teacherId);
                if (balance < priceCharged)
                    return StatusCode(402, new
                    {
                        success = false,
                        error = $"Solde insuffisant : {balance:0} XAF disponibles, {priceCharged:0} XAF requis.",
                        balanceXaf = balance,
                        requiredXaf = priceCharged,
                    });
            }

            var assignment = new TeacherClassContent
            {
                TeacherClassId = id,
                SubjectId = subject.Id,
                AssignedByUserId = teacherId,
                PriceChargedXaf = priceCharged,
            };
            _db.TeacherClassContents.Add(assignment);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Professeur {TeacherId} a assigné le contenu {SubjectId} à la classe {ClassId} ({Price} XAF)",
                teacherId, subject.Id, id, priceCharged);

            return Ok(new { data = new { assignment.Id, priceCharged }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning content to class {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpDelete("{id:int}/content/{contentId:int}")]
    public async Task<IActionResult> UnassignContent([FromRoute] int id, [FromRoute] int contentId)
    {
        var teacherId = User.GetUserId();
        var klass = await _db.TeacherClasses.FirstOrDefaultAsync(c => c.Id == id && c.TeacherId == teacherId);
        if (klass == null) return StatusCode(403, new { success = false, error = "Classe non autorisée." });

        var link = await _db.TeacherClassContents.FirstOrDefaultAsync(tcc => tcc.Id == contentId && tcc.TeacherClassId == id);
        if (link != null)
        {
            _db.TeacherClassContents.Remove(link);
            await _db.SaveChangesAsync();
        }
        return NoContent();
    }
}
