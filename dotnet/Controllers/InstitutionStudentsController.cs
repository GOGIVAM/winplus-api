using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;

namespace Backend.Controllers;

public class ImportStudentRow
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Level { get; set; }
    public string? Group { get; set; }
    public string? Matricule { get; set; }
}

public class ImportStudentsRequest
{
    public List<ImportStudentRow> Rows { get; set; } = new();
}

/// <summary>
/// Annuaire des élèves d'une institution (S5-2), import en masse et KPIs
/// consolidés (S5-1). Tous les chiffres proviennent des tables réelles.
///
/// GET  /api/institution/me
/// GET  /api/institution/{institutionId}/students
/// POST /api/institution/{institutionId}/students/import
/// GET  /api/institution/{institutionId}/kpis
/// GET  /api/institution/{institutionId}/subject-stats
/// POST /api/institution/{institutionId}/reports
/// </summary>
[ApiController]
[Route("api/institution")]
[Authorize]
public class InstitutionStudentsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<InstitutionStudentsController> _logger;

    public InstitutionStudentsController(ApplicationDbContext db, ILogger<InstitutionStudentsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Institution rattachée au compte connecté (User.InstitutionId).</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMine()
    {
        try
        {
            var userId = User.GetUserId();
            var institutionId = await _db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.InstitutionId)
                .FirstOrDefaultAsync();

            if (institutionId == null)
                return NoContent();   // Compte non rattaché : le frontend affiche son état vide.

            var institution = await _db.Institutions.AsNoTracking()
                .Where(i => i.Id == institutionId && !i.IsDeleted)
                .Select(i => new { i.Id, i.Name, i.Code, i.City, i.Country, i.Type })
                .FirstOrDefaultAsync();

            if (institution == null) return NoContent();

            return Ok(new { data = institution, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting own institution");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    private async Task<bool> CanAccessAsync(int institutionId)
    {
        var userId = User.GetUserId();
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Role, u.InstitutionId })
            .FirstOrDefaultAsync();

        if (user == null) return false;
        if (string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase)) return true;
        return user.InstitutionId == institutionId;
    }

    /// <summary>Annuaire filtrable et paginé.</summary>
    [HttpGet("{institutionId:int}/students")]
    public async Task<IActionResult> GetStudents(
        [FromRoute] int institutionId,
        [FromQuery] string? search,
        [FromQuery] string? level,
        [FromQuery] string? group,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        try
        {
            if (!await CanAccessAsync(institutionId))
                return StatusCode(403, new { success = false, error = "Accès refusé à cette institution." });

            if (page < 1) page = 1;
            if (pageSize is < 1 or > 100) pageSize = 25;

            var query = _db.InstitutionStudents.AsNoTracking()
                .Where(s => s.InstitutionId == institutionId && s.Student != null && !s.Student.IsDeleted);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(s =>
                    (s.Student!.FirstName != null && s.Student.FirstName.ToLower().Contains(term)) ||
                    (s.Student!.LastName  != null && s.Student.LastName.ToLower().Contains(term)) ||
                    s.Student!.Email.ToLower().Contains(term) ||
                    (s.MatriculeNumber != null && s.MatriculeNumber.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(level) && level != "Tous")
                query = query.Where(s => s.Level == level);

            if (!string.IsNullOrWhiteSpace(group) && group != "Tous")
                query = query.Where(s => s.GroupName == group);

            if (string.Equals(status, "actifs", StringComparison.OrdinalIgnoreCase))
                query = query.Where(s => s.IsActive);
            else if (string.Equals(status, "inactifs", StringComparison.OrdinalIgnoreCase))
                query = query.Where(s => !s.IsActive);

            var total = await query.CountAsync();

            var items = await query
                .OrderBy(s => s.Student!.LastName).ThenBy(s => s.Student!.FirstName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new
                {
                    s.Id,
                    studentId = s.StudentId,
                    firstName = s.Student!.FirstName,
                    lastName  = s.Student.LastName,
                    email     = s.Student.Email,
                    avatarUrl = s.Student.AvatarUrl,
                    level     = s.Level ?? s.Student.Level,
                    group     = s.GroupName,
                    matricule = s.MatriculeNumber,
                    isActive  = s.IsActive,
                    lastLoginAt = s.Student.LastLoginAt,
                    avgScore  = _db.QuizAttempts
                        .Where(a => a.UserId == s.StudentId && a.IsCompleted)
                        .Average(a => (double?)a.Score)
                })
                .ToListAsync();

            var levels = await _db.InstitutionStudents.AsNoTracking()
                .Where(s => s.InstitutionId == institutionId && s.Level != null)
                .Select(s => s.Level!).Distinct().OrderBy(l => l).ToListAsync();

            var groups = await _db.InstitutionStudents.AsNoTracking()
                .Where(s => s.InstitutionId == institutionId && s.GroupName != null)
                .Select(s => s.GroupName!).Distinct().OrderBy(g => g).ToListAsync();

            return Ok(new
            {
                data = items,
                total, page, pageSize,
                totalPages = (int)Math.Ceiling(total / (double)pageSize),
                filters = new { levels, groups },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institution students");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Import en masse. Le frontend parse le CSV et envoie les lignes ;
    /// seuls les emails correspondant à un compte existant sont rattachés,
    /// les autres sont renvoyés en erreur ligne à ligne.
    /// </summary>
    [HttpPost("{institutionId:int}/students/import")]
    public async Task<IActionResult> Import([FromRoute] int institutionId, [FromBody] ImportStudentsRequest request)
    {
        try
        {
            if (!await CanAccessAsync(institutionId))
                return StatusCode(403, new { success = false, error = "Accès refusé à cette institution." });

            if (request.Rows.Count == 0)
                return BadRequest(new { success = false, error = "Fichier vide." });
            if (request.Rows.Count > 5000)
                return BadRequest(new { success = false, error = "5 000 lignes maximum par import." });

            var imported = 0;
            var updated = 0;
            var errors = new List<object>();

            for (var i = 0; i < request.Rows.Count; i++)
            {
                var row = request.Rows[i];
                var email = (row.Email ?? "").Trim().ToLowerInvariant();

                if (email.Length == 0 || !email.Contains('@'))
                {
                    errors.Add(new { line = i + 1, email = row.Email, error = "Email absent ou invalide" });
                    continue;
                }

                var studentId = await _db.Users
                    .Where(u => u.Email.ToLower() == email && !u.IsDeleted)
                    .Select(u => (int?)u.Id)
                    .FirstOrDefaultAsync();

                if (studentId == null)
                {
                    errors.Add(new { line = i + 1, email, error = "Aucun compte WinPlus avec cet email" });
                    continue;
                }

                var link = await _db.InstitutionStudents
                    .FirstOrDefaultAsync(s => s.InstitutionId == institutionId && s.StudentId == studentId);

                if (link == null)
                {
                    _db.InstitutionStudents.Add(new InstitutionStudent
                    {
                        InstitutionId   = institutionId,
                        StudentId       = studentId.Value,
                        Level           = string.IsNullOrWhiteSpace(row.Level) ? null : row.Level!.Trim(),
                        GroupName       = string.IsNullOrWhiteSpace(row.Group) ? null : row.Group!.Trim(),
                        MatriculeNumber = string.IsNullOrWhiteSpace(row.Matricule) ? null : row.Matricule!.Trim()
                    });
                    imported++;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(row.Level))     link.Level = row.Level!.Trim();
                    if (!string.IsNullOrWhiteSpace(row.Group))     link.GroupName = row.Group!.Trim();
                    if (!string.IsNullOrWhiteSpace(row.Matricule)) link.MatriculeNumber = row.Matricule!.Trim();
                    link.IsActive = true;
                    updated++;
                }
            }

            await _db.SaveChangesAsync();

            return Ok(new { data = new { imported, updated, rejected = errors.Count, errors }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing institution students");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>KPIs calculés : élèves actifs, licences, quiz de la semaine, réussite.</summary>
    [HttpGet("{institutionId:int}/kpis")]
    public async Task<IActionResult> GetKpis([FromRoute] int institutionId)
    {
        try
        {
            if (!await CanAccessAsync(institutionId))
                return StatusCode(403, new { success = false, error = "Accès refusé à cette institution." });

            var studentIds = await _db.InstitutionStudents.AsNoTracking()
                .Where(s => s.InstitutionId == institutionId)
                .Select(s => s.StudentId)
                .ToListAsync();

            var licensesUsed = await _db.InstitutionStudents
                .CountAsync(s => s.InstitutionId == institutionId && s.IsActive);

            var since24h = DateTime.UtcNow.AddDays(-1);
            var activeToday = await _db.Users
                .CountAsync(u => studentIds.Contains(u.Id) && u.LastLoginAt >= since24h);

            var weekStart = DateTime.UtcNow.Date.AddDays(-7);
            var quizThisWeek = await _db.QuizAttempts
                .CountAsync(a => studentIds.Contains(a.UserId) && a.IsCompleted && a.CompletedAt >= weekStart);

            var scores = await _db.QuizAttempts.AsNoTracking()
                .Where(a => studentIds.Contains(a.UserId) && a.IsCompleted)
                .Select(a => (double)a.Score)
                .ToListAsync();

            return Ok(new
            {
                data = new
                {
                    studentsTotal   = studentIds.Count,
                    licensesUsed,
                    activeToday,
                    quizThisWeek,
                    avgSuccessRate  = scores.Count == 0 ? (int?)null : (int)Math.Round(scores.Count(s => s >= 50) * 100d / scores.Count),
                    avgScore        = scores.Count == 0 ? (int?)null : (int)Math.Round(scores.Average())
                },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institution KPIs");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Matières les plus travaillées, mesurées sur les sessions d'étude.</summary>
    [HttpGet("{institutionId:int}/subject-stats")]
    public async Task<IActionResult> GetSubjectStats([FromRoute] int institutionId, [FromQuery] int top = 6)
    {
        try
        {
            if (!await CanAccessAsync(institutionId))
                return StatusCode(403, new { success = false, error = "Accès refusé à cette institution." });

            if (top is < 1 or > 20) top = 6;

            var studentIds = await _db.InstitutionStudents.AsNoTracking()
                .Where(s => s.InstitutionId == institutionId)
                .Select(s => s.StudentId)
                .ToListAsync();

            var stats = await _db.StudySessions.AsNoTracking()
                .Where(s => studentIds.Contains(s.UserId) && s.Subject != null)
                .GroupBy(s => s.Subject!.Title)
                .Select(g => new { subject = g.Key, sessions = g.Count(), minutes = g.Sum(x => x.Duration) })
                .OrderByDescending(x => x.sessions)
                .Take(top)
                .ToListAsync();

            return Ok(new { data = stats, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institution subject stats");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Génère les données d'un rapport (global, par groupe, individuel, à risque).
    /// Le rendu PDF est fait côté client à partir de ces données réelles.
    /// </summary>
    [HttpPost("{institutionId:int}/reports")]
    public async Task<IActionResult> GenerateReport(
        [FromRoute] int institutionId,
        [FromQuery] string type = "global",
        [FromQuery] int period = 30,
        [FromQuery] int? studentId = null)
    {
        try
        {
            if (!await CanAccessAsync(institutionId))
                return StatusCode(403, new { success = false, error = "Accès refusé à cette institution." });

            var days = period is 7 or 30 or 90 or 365 ? period : 30;
            var since = DateTime.UtcNow.Date.AddDays(-days);

            var links = await _db.InstitutionStudents.AsNoTracking()
                .Where(s => s.InstitutionId == institutionId)
                .Select(s => new { s.StudentId, s.GroupName, s.Level })
                .ToListAsync();

            var studentIds = links.Select(l => l.StudentId).ToList();

            var attempts = await _db.QuizAttempts.AsNoTracking()
                .Where(a => studentIds.Contains(a.UserId) && a.IsCompleted && a.CompletedAt >= since)
                .Select(a => new { a.UserId, score = (double)a.Score })
                .ToListAsync();

            object payload;

            switch (type)
            {
                case "group":
                    payload = links
                        .GroupBy(l => l.GroupName ?? "Sans groupe")
                        .Select(g =>
                        {
                            var ids = g.Select(x => x.StudentId).ToHashSet();
                            var slice = attempts.Where(a => ids.Contains(a.UserId)).ToList();
                            return new
                            {
                                group    = g.Key,
                                students = g.Count(),
                                quizzes  = slice.Count,
                                avgScore = slice.Count == 0 ? (int?)null : (int)Math.Round(slice.Average(x => x.score))
                            };
                        })
                        .OrderBy(x => x.group)
                        .ToList();
                    break;

                case "individual":
                    if (studentId == null)
                        return BadRequest(new { success = false, error = "studentId requis pour un rapport individuel." });
                    if (!studentIds.Contains(studentId.Value))
                        return NotFound(new { success = false, error = "Élève non rattaché à cette institution." });

                    var slice2 = attempts.Where(a => a.UserId == studentId.Value).ToList();
                    var student = await _db.Users.AsNoTracking()
                        .Where(u => u.Id == studentId)
                        .Select(u => new { u.FirstName, u.LastName, u.Email, u.Level })
                        .FirstOrDefaultAsync();

                    payload = new
                    {
                        student,
                        quizzes  = slice2.Count,
                        avgScore = slice2.Count == 0 ? (int?)null : (int)Math.Round(slice2.Average(x => x.score)),
                        studyMinutes = await _db.StudySessions
                            .Where(s => s.UserId == studentId && s.CreatedAt >= since)
                            .SumAsync(s => (int?)s.Duration) ?? 0
                    };
                    break;

                case "at-risk":
                    payload = attempts
                        .GroupBy(a => a.UserId)
                        .Select(g => new { userId = g.Key, avg = g.Average(x => x.score), quizzes = g.Count() })
                        .Where(x => x.avg < 55)
                        .OrderBy(x => x.avg)
                        .Join(_db.Users.AsNoTracking(), x => x.userId, u => u.Id, (x, u) => new
                        {
                            studentId = u.Id,
                            firstName = u.FirstName,
                            lastName  = u.LastName,
                            level     = u.Level,
                            avgScore  = (int)Math.Round(x.avg),
                            quizzes   = x.quizzes
                        })
                        .ToList();
                    break;

                default:
                    payload = new
                    {
                        studentsTotal = studentIds.Count,
                        quizzes       = attempts.Count,
                        avgScore      = attempts.Count == 0 ? (int?)null : (int)Math.Round(attempts.Average(a => a.score)),
                        successRate   = attempts.Count == 0 ? (int?)null : (int)Math.Round(attempts.Count(a => a.score >= 50) * 100d / attempts.Count)
                    };
                    break;
            }

            return Ok(new
            {
                data = new
                {
                    type,
                    periodDays  = days,
                    generatedAt = DateTime.UtcNow,
                    report      = payload
                },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating institution report");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
