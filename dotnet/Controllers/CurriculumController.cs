using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;

namespace Backend.Controllers;

// ── DTOs ───────────────────────────────────────────────────────────
public class CurriculumSelectionRequest
{
    public string? Level { get; set; }
    public string? Series { get; set; }
}

public class CurriculumChapterRequest
{
    public string? Key { get; set; }
    /// <summary>« todo », « doing » ou « done ».</summary>
    public string? Status { get; set; }
}

public class CurriculumStateResponse
{
    public string? Level { get; set; }
    public string? Series { get; set; }
    /// <summary>Clé du chapitre → « doing » ou « done ».</summary>
    public Dictionary<string, string> Chapters { get; set; } = new();
    public DateTime? UpdatedAt { get; set; }
}

public class CurriculumStudentSummary
{
    public int StudentId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Level { get; set; }
    public string? Series { get; set; }
    public int Done { get; set; }
    public int Doing { get; set; }
    public DateTime? LastUpdate { get; set; }
}

/// <summary>
/// « Mon programme » : avancement dans le programme officiel du secondaire.
/// Le référentiel (matières, modules, chapitres) vit côté site ; le serveur
/// garde la classe suivie et l'état de chaque chapitre.
///
/// GET  /api/curriculum/me                      → mon avancement
/// PUT  /api/curriculum/me/selection            → ma classe et ma série
/// PUT  /api/curriculum/me/chapters             → l'état d'un chapitre
/// GET  /api/curriculum/students/{id}           → celui d'un élève (parent lié,
///                                                professeur de sa classe, son établissement)
/// GET  /api/curriculum/classes/{classId}/summary → résumé par élève d'une classe (professeur)
/// GET  /api/curriculum/institution/summary     → résumé par élève de l'établissement
/// </summary>
[ApiController]
[Route("api/curriculum")]
[Authorize]
public class CurriculumController : ControllerBase
{
    // Général francophone, technique francophone (tc*), anglophone (Form 1-5, Lower/Upper Sixth).
    private static readonly HashSet<string> Levels = new()
    {
        "6e", "5e", "4e", "3e", "2nde", "1ere", "tle",
        "tc1", "tc2", "tc3", "tc4", "tc2nde", "tc1ere", "tctle",
        "f1", "f2", "f3", "f4", "f5", "ls", "us",
    };
    private static readonly HashSet<string> SeriesAllowed = new()
    {
        "A", "C", "D", "E", "TI",
        "F1", "F2", "F3", "F4", "F5", "F7", "MA", "IH", "ESF", "ACA", "ACC", "CG",
        "ART", "SCI", "COM", "TECH",
    };
    private static readonly Regex KeyPattern = new(@"^(6e|5e|4e|3e|2nde|1ere|tle|tc1|tc2|tc3|tc4|tc2nde|tc1ere|tctle|f1|f2|f3|f4|f5|ls|us)\|[a-z0-9-]{1,40}\|\d{1,2}\|\d{1,3}$", RegexOptions.Compiled);
    /// <summary>Garde-fou : un programme complet compte quelques centaines de chapitres.</summary>
    private const int MaxChaptersPerUser = 3000;

    private readonly ApplicationDbContext _db;
    private readonly ILogger<CurriculumController> _logger;

    public CurriculumController(ApplicationDbContext db, ILogger<CurriculumController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private async Task<CurriculumStateResponse> BuildStateAsync(int userId)
    {
        var selection = await _db.CurriculumSelections.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId);
        var rows = await _db.CurriculumProgress.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.ChapterKey, p.Status, p.UpdatedAt })
            .ToListAsync();
        return new CurriculumStateResponse
        {
            Level = selection?.Level,
            Series = selection?.Series,
            Chapters = rows.ToDictionary(r => r.ChapterKey, r => r.Status),
            UpdatedAt = rows.Count > 0 ? rows.Max(r => r.UpdatedAt) : selection?.UpdatedAt,
        };
    }

    /// <summary>
    /// Qui peut lire l'avancement d'un élève : lui-même, un parent dont le lien
    /// est accepté, un professeur qui l'a dans une de ses classes, le compte de
    /// son établissement, un administrateur.
    /// </summary>
    private async Task<bool> CanReadStudentAsync(int callerId, int studentId)
    {
        if (callerId == studentId) return true;
        var role = User.GetUserRole();
        if (role == "admin" || role == "super_admin") return true;

        if (await _db.ParentStudentLinks.AnyAsync(l => l.ParentId == callerId && l.StudentId == studentId && l.Status == "accepted")) return true;

        if (await _db.TeacherClassStudents.AnyAsync(cs => cs.StudentId == studentId && cs.TeacherClass != null && cs.TeacherClass.TeacherId == callerId)) return true;

        var institutionId = await _db.Users.Where(u => u.Id == callerId).Select(u => u.InstitutionId).FirstOrDefaultAsync();
        if (institutionId != null && role == "institution"
            && await _db.InstitutionStudents.AnyAsync(s => s.InstitutionId == institutionId && s.StudentId == studentId)) return true;

        return false;
    }

    private async Task<List<CurriculumStudentSummary>> SummariesAsync(List<int> studentIds)
    {
        if (studentIds.Count == 0) return new List<CurriculumStudentSummary>();
        var users = await _db.Users.AsNoTracking().Where(u => studentIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName }).ToListAsync();
        var selections = await _db.CurriculumSelections.AsNoTracking().Where(s => studentIds.Contains(s.UserId)).ToListAsync();
        var counts = await _db.CurriculumProgress.AsNoTracking().Where(p => studentIds.Contains(p.UserId))
            .GroupBy(p => new { p.UserId, p.Status })
            .Select(g => new { g.Key.UserId, g.Key.Status, Count = g.Count(), Last = g.Max(p => p.UpdatedAt) })
            .ToListAsync();

        return users.Select(u =>
        {
            var sel = selections.FirstOrDefault(s => s.UserId == u.Id);
            var mine = counts.Where(c => c.UserId == u.Id).ToList();
            return new CurriculumStudentSummary
            {
                StudentId = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Level = sel?.Level,
                Series = sel?.Series,
                Done = mine.Where(c => c.Status == "done").Sum(c => c.Count),
                Doing = mine.Where(c => c.Status == "doing").Sum(c => c.Count),
                LastUpdate = mine.Count > 0 ? mine.Max(c => c.Last) : sel?.UpdatedAt,
            };
        }).OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToList();
    }

    // ── Mon avancement ─────────────────────────────────────────────

    [HttpGet("me")]
    public async Task<IActionResult> GetMine()
    {
        try
        {
            return Ok(await BuildStateAsync(User.GetUserId()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting curriculum state");
            return StatusCode(500, new { message = "Erreur lors du chargement du programme." });
        }
    }

    [HttpPut("me/selection")]
    public async Task<IActionResult> SetSelection([FromBody] CurriculumSelectionRequest req)
    {
        try
        {
            var level = (req.Level ?? string.Empty).Trim();
            if (!Levels.Contains(level)) return BadRequest(new { message = "Classe inconnue." });
            var series = string.IsNullOrWhiteSpace(req.Series) ? null : req.Series.Trim().ToUpperInvariant();
            if (series != null && !SeriesAllowed.Contains(series)) return BadRequest(new { message = "Série inconnue." });

            var userId = User.GetUserId();
            var selection = await _db.CurriculumSelections.FirstOrDefaultAsync(s => s.UserId == userId);
            if (selection == null)
            {
                selection = new CurriculumSelection { UserId = userId };
                _db.CurriculumSelections.Add(selection);
            }
            selection.Level = level;
            selection.Series = series;
            selection.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(await BuildStateAsync(userId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving curriculum selection");
            return StatusCode(500, new { message = "Erreur lors de l'enregistrement de la classe." });
        }
    }

    [HttpPut("me/chapters")]
    public async Task<IActionResult> SetChapter([FromBody] CurriculumChapterRequest req)
    {
        try
        {
            var key = (req.Key ?? string.Empty).Trim();
            var status = (req.Status ?? string.Empty).Trim().ToLowerInvariant();
            if (!KeyPattern.IsMatch(key)) return BadRequest(new { message = "Chapitre inconnu." });
            if (status != "todo" && status != "doing" && status != "done") return BadRequest(new { message = "État inconnu." });

            var userId = User.GetUserId();
            var row = await _db.CurriculumProgress.FirstOrDefaultAsync(p => p.UserId == userId && p.ChapterKey == key);

            if (status == "todo")
            {
                if (row != null) _db.CurriculumProgress.Remove(row);
            }
            else if (row == null)
            {
                if (await _db.CurriculumProgress.CountAsync(p => p.UserId == userId) >= MaxChaptersPerUser)
                    return BadRequest(new { message = "Trop de chapitres enregistrés." });
                _db.CurriculumProgress.Add(new CurriculumProgress { UserId = userId, ChapterKey = key, Status = status, UpdatedAt = DateTime.UtcNow });
            }
            else
            {
                row.Status = status;
                row.UpdatedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();

            return Ok(new { key, status });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving curriculum chapter");
            return StatusCode(500, new { message = "Erreur lors de l'enregistrement du chapitre." });
        }
    }

    // ── Avancement d'un élève, vu par un parent, un professeur ou un établissement ──

    [HttpGet("students/{studentId:int}")]
    public async Task<IActionResult> GetStudent([FromRoute] int studentId)
    {
        try
        {
            if (!await CanReadStudentAsync(User.GetUserId(), studentId)) return Forbid();
            return Ok(await BuildStateAsync(studentId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting student curriculum state");
            return StatusCode(500, new { message = "Erreur lors du chargement du programme de l'élève." });
        }
    }

    [HttpGet("classes/{classId:int}/summary")]
    public async Task<IActionResult> GetClassSummary([FromRoute] int classId)
    {
        try
        {
            var me = User.GetUserId();
            var owned = await _db.TeacherClasses.AsNoTracking().AnyAsync(c => c.Id == classId && c.TeacherId == me);
            if (!owned) return Forbid();

            var ids = await _db.TeacherClassStudents.AsNoTracking()
                .Where(cs => cs.TeacherClassId == classId).Select(cs => cs.StudentId).Distinct().ToListAsync();
            return Ok(await SummariesAsync(ids));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting class curriculum summary");
            return StatusCode(500, new { message = "Erreur lors du chargement de la classe." });
        }
    }

    [HttpGet("institution/summary")]
    public async Task<IActionResult> GetInstitutionSummary()
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var institutionId = await _db.Users.Where(u => u.Id == me).Select(u => u.InstitutionId).FirstOrDefaultAsync();
            if (institutionId == null) return Ok(new List<CurriculumStudentSummary>());

            var ids = await _db.InstitutionStudents.AsNoTracking()
                .Where(s => s.InstitutionId == institutionId).Select(s => s.StudentId).Distinct().ToListAsync();
            return Ok(await SummariesAsync(ids));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institution curriculum summary");
            return StatusCode(500, new { message = "Erreur lors du chargement de l'établissement." });
        }
    }
}
