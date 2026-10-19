using System.Text.Json;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Espace institution, phase 2 (étape 4) : bulletin d'un élève.
///
/// GET    /api/institution/bulletins/preview?studentId=&amp;from=&amp;to=   bulletin calculé, non enregistré
/// POST   /api/institution/bulletins                                  enregistrer et partager (élève + parents)
/// GET    /api/institution/bulletins?studentId=                       bulletins partagés par l'établissement
/// GET    /api/institution/bulletins/mine                             bulletins reçus (élève ou parent)
/// GET    /api/institution/bulletins/{id}                             un bulletin (établissement, élève, parent)
/// DELETE /api/institution/bulletins/{id}                             retirer un bulletin partagé
///
/// Données : quiz terminés sur la période (moyenne par matière, comparée à la
/// moyenne de la classe), rang dans la classe, devoirs d'établissement de la
/// période (faits / donnés), régularité (jours actifs) et temps d'étude.
/// </summary>
[ApiController]
[Route("api/institution/bulletins")]
[Authorize]
public class InstitutionBulletinsController : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _db;
    private readonly ILogger<InstitutionBulletinsController> _logger;
    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpClientFactory _http;
    private readonly IServiceTokenProvider _serviceToken;

    public InstitutionBulletinsController(ApplicationDbContext db, ILogger<InstitutionBulletinsController> logger, IServiceScopeFactory scopes,
        IHttpClientFactory http, IServiceTokenProvider serviceToken)
    {
        _db = db;
        _logger = logger;
        _scopes = scopes;
        _http = http;
        _serviceToken = serviceToken;
    }

    public record ClassPreviewRequest(int GroupId, DateTime From, DateTime To);
    public record AppreciationRequest(int StudentId, DateTime From, DateTime To, string? PeriodLabel);

    /// <summary>
    /// Conseil de classe : les bulletins calculés de tous les élèves d'une classe
    /// (classés par moyenne), à relire avant de les partager.
    /// </summary>
    [HttpPost("class-preview")]
    public async Task<IActionResult> ClassPreview([FromBody] ClassPreviewRequest req)
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var institutionId = await MyInstitutionIdAsync(me);
            if (institutionId == null || !await _db.StudyGroups.AnyAsync(g => g.Id == req.GroupId && g.OwnerId == me && g.IsActive))
                return NotFound(new { error = "Classe introuvable." });
            var p = Period(req.From, req.To);
            if (p == null) return BadRequest(new { error = "Période invalide (400 jours au plus)." });

            var members = await _db.StudyGroupMembers.AsNoTracking()
                .Where(m => m.StudyGroupId == req.GroupId && m.Role != InstitutionTeachersController.TeacherRole && m.UserId != me)
                .Select(m => m.UserId).ToListAsync();
            var own = await _db.InstitutionStudents.AsNoTracking()
                .Where(s => s.InstitutionId == institutionId && members.Contains(s.StudentId) && s.IsActive)
                .Select(s => s.StudentId).ToListAsync();

            var list = new List<BulletinData>();
            foreach (var id in own.Take(80))
            {
                var d = await ComputeAsync(institutionId.Value, me, id, p.Value.from, p.Value.to);
                if (d != null) list.Add(d);
            }
            return Ok(new { data = list.OrderByDescending(x => x.Average ?? -1).ThenBy(x => x.StudentName), success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing class bulletins");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Appréciation proposée par WinAI pour un bulletin (2 à 3 phrases), à relire.
    /// Repli sans IA : une appréciation construite à partir des chiffres.
    /// </summary>
    [HttpPost("appreciation")]
    public async Task<IActionResult> Appreciation([FromBody] AppreciationRequest req, CancellationToken ct)
    {
        var me = User.GetUserId();
        if (User.GetUserRole() != "institution") return Forbid();
        var institutionId = await MyInstitutionIdAsync(me);
        if (institutionId == null || !await IsOwnStudentAsync(institutionId.Value, req.StudentId))
            return NotFound(new { error = "Élève introuvable dans votre établissement." });
        var p = Period(req.From, req.To);
        if (p == null) return BadRequest(new { error = "Période invalide." });
        var d = await ComputeAsync(institutionId.Value, me, req.StudentId, p.Value.from, p.Value.to);
        if (d == null) return NotFound(new { error = "Élève introuvable." });

        string? text = null;
        var source = "winai";
        try
        {
            var client = _http.CreateClient("FastApiClient");
            using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/institution/appreciation")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new
                {
                    student_name = d.StudentName, period_label = req.PeriodLabel, average = d.Average, class_average = d.ClassAverage,
                    rank = d.Rank, class_size = d.ClassSize,
                    subjects = d.Subjects.Select(s => new { subject = s.Subject, average = s.Average, quizzes = s.Quizzes, classAverage = s.ClassAverage }),
                    homework_given = d.HomeworkGiven, homework_done = d.HomeworkDone, active_days = d.ActiveDays, study_minutes = d.StudyMinutes,
                }),
            };
            msg.Headers.TryAddWithoutValidation("Authorization", _serviceToken.CreateAuthorizationHeader(ServiceScopes.InstitutionReport));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(25));
            var res = await client.SendAsync(msg, cts.Token);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: cts.Token);
                if (json.TryGetProperty("appreciation", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.String)
                    text = a.GetString();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Appréciation WinAI indisponible, repli sur l'appréciation calculée");
        }
        if (string.IsNullOrWhiteSpace(text)) { text = FallbackAppreciation(d); source = "calculee"; }
        return Ok(new { data = new { appreciation = text, source }, success = true });
    }

    /// <summary>Appréciation construite à partir des chiffres (sans IA).</summary>
    private static string FallbackAppreciation(BulletinData d)
    {
        if (d.Average == null) return "Pas assez de travail sur WinPlus pendant la période pour évaluer les acquis. Il faut s'y mettre régulièrement dès maintenant.";
        var avg = d.Average.Value / 5;
        var opening = avg >= 16 ? "Excellente période, des résultats solides et réguliers."
            : avg >= 14 ? "Très bonne période, le travail porte ses fruits."
            : avg >= 12 ? "Période satisfaisante dans l'ensemble."
            : avg >= 10 ? "Période juste : les bases sont là mais restent fragiles."
            : "Période difficile : les résultats sont en dessous des attentes.";
        var best = d.Subjects.Where(s => s.Quizzes >= 2).OrderByDescending(s => s.Average).FirstOrDefault();
        var weak = d.Subjects.Where(s => s.Quizzes >= 2).OrderBy(s => s.Average).FirstOrDefault();
        var parts = new List<string> { opening };
        if (best != null && weak != null && best.Subject != weak.Subject)
            parts.Add($"Point fort en {best.Subject} ; la {weak.Subject} est à consolider.");
        if (d.HomeworkGiven > 0 && d.HomeworkDone < d.HomeworkGiven)
            parts.Add($"{d.HomeworkGiven - d.HomeworkDone} devoir(s) non rendu(s) : il faut gagner en régularité.");
        else if (d.ActiveDays < 8)
            parts.Add("Il faut réviser plus régulièrement, un peu chaque jour.");
        else
            parts.Add("Continuez sur ce rythme.");
        return string.Join(" ", parts);
    }

    public record ShareBulletinRequest(int StudentId, DateTime From, DateTime To, string PeriodLabel, string? Appreciation);

    // ── Données d'un bulletin ────────────────────────────────────────────
    public record SubjectLine(string Subject, double Average, int Quizzes, double? ClassAverage);
    public record BulletinData(
        int StudentId, string StudentName, string? Email, string InstitutionName, List<string> Classes,
        DateTime From, DateTime To, double? Average, double? ClassAverage, int? Rank, int? ClassSize,
        List<SubjectLine> Subjects, int HomeworkGiven, int HomeworkDone, double? HomeworkAverage,
        int ActiveDays, int StudyMinutes, int QuizCount);

    private async Task<int?> MyInstitutionIdAsync(int userId) =>
        await _db.Users.Where(u => u.Id == userId).Select(u => u.InstitutionId).FirstOrDefaultAsync();

    private Task<bool> IsOwnStudentAsync(int institutionId, int studentId) =>
        _db.InstitutionStudents.AnyAsync(s => s.InstitutionId == institutionId && s.StudentId == studentId && s.IsActive);

    private async Task<BulletinData?> ComputeAsync(int institutionId, int ownerUserId, int studentId, DateTime from, DateTime to)
    {
        var student = await _db.Users.AsNoTracking().Where(u => u.Id == studentId)
            .Select(u => new { u.FirstName, u.LastName, u.Email }).FirstOrDefaultAsync();
        if (student == null) return null;
        var institutionName = await _db.Institutions.Where(i => i.Id == institutionId).Select(i => i.Name).FirstOrDefaultAsync() ?? "";

        // Classes de l'établissement dont l'élève est membre.
        var groups = await _db.StudyGroupMembers.AsNoTracking()
            .Where(m => m.UserId == studentId && m.Role != InstitutionTeachersController.TeacherRole
                        && m.StudyGroup != null && m.StudyGroup.OwnerId == ownerUserId && m.StudyGroup.IsActive)
            .Select(m => new { m.StudyGroupId, name = m.StudyGroup!.Name })
            .ToListAsync();
        var groupIds = groups.Select(g => g.StudyGroupId).ToList();
        var classmates = groupIds.Count == 0 ? new List<int> { studentId } : await _db.StudyGroupMembers.AsNoTracking()
            .Where(m => groupIds.Contains(m.StudyGroupId) && m.Role != InstitutionTeachersController.TeacherRole)
            .Select(m => m.UserId).Distinct().ToListAsync();
        if (!classmates.Contains(studentId)) classmates.Add(studentId);

        var attempts = await _db.QuizAttempts.AsNoTracking()
            .Where(a => classmates.Contains(a.UserId) && a.IsCompleted && a.CompletedAt >= from && a.CompletedAt <= to)
            .Select(a => new { a.UserId, a.QuizId, a.Score, a.CompletedAt, subject = a.Quiz != null ? a.Quiz.Subject : null })
            .ToListAsync();
        var mine = attempts.Where(a => a.UserId == studentId).ToList();

        var subjects = mine.GroupBy(a => string.IsNullOrWhiteSpace(a.subject) ? "Autres" : a.subject!)
            .Select(g =>
            {
                var classAvg = attempts.Where(a => (string.IsNullOrWhiteSpace(a.subject) ? "Autres" : a.subject!) == g.Key)
                    .Select(a => (double)a.Score).DefaultIfEmpty().Average();
                return new SubjectLine(g.Key, Math.Round(g.Average(a => (double)a.Score), 1), g.Count(),
                    groupIds.Count == 0 ? null : Math.Round(classAvg, 1));
            })
            .OrderByDescending(s => s.Quizzes).ToList();

        double? average = mine.Count == 0 ? null : Math.Round(mine.Average(a => (double)a.Score), 1);
        var byStudent = attempts.GroupBy(a => a.UserId).Select(g => new { id = g.Key, avg = g.Average(a => (double)a.Score) })
            .OrderByDescending(x => x.avg).ToList();
        int? rank = average == null || groupIds.Count == 0 ? null : byStudent.FindIndex(x => x.id == studentId) + 1;
        double? classAverage = byStudent.Count == 0 || groupIds.Count == 0 ? null : Math.Round(byStudent.Average(x => x.avg), 1);

        // Devoirs d'établissement donnés pendant la période.
        var homework = groupIds.Count == 0 ? new List<(int QuizId, DateTime CreatedAt)>() : (await _db.InstitutionHomework.AsNoTracking()
            .Where(h => !h.IsDeleted && groupIds.Contains(h.StudyGroupId) && h.CreatedAt >= from && h.CreatedAt <= to)
            .Select(h => new { h.QuizId, h.CreatedAt }).ToListAsync())
            .Select(h => (h.QuizId, h.CreatedAt)).ToList();
        var hwScores = homework
            .Select(h => mine.Where(a => a.QuizId == h.QuizId && a.CompletedAt >= h.CreatedAt).Select(a => (double?)a.Score).Max())
            .ToList();
        var hwDone = hwScores.Count(s => s != null);

        var sessions = await _db.StudySessions.AsNoTracking()
            .Where(s => s.UserId == studentId && s.CreatedAt >= from && s.CreatedAt <= to)
            .Select(s => new { s.CreatedAt, s.Duration }).ToListAsync();
        var activeDays = mine.Select(a => a.CompletedAt.Date).Concat(sessions.Select(s => s.CreatedAt.Date)).Distinct().Count();

        return new BulletinData(
            studentId,
            $"{student.FirstName} {student.LastName}".Trim(), student.Email, institutionName,
            groups.Select(g => g.name).ToList(), from, to, average, classAverage, rank,
            groupIds.Count == 0 ? null : classmates.Count,
            subjects, homework.Count, hwDone,
            hwDone == 0 ? null : Math.Round(hwScores.Where(s => s != null).Average(s => s!.Value), 1),
            activeDays, sessions.Sum(s => s.Duration), mine.Count);
    }

    private static (DateTime from, DateTime to)? Period(DateTime from, DateTime to)
    {
        var f = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
        var t = DateTime.SpecifyKind(to.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
        if (t <= f || (t - f).TotalDays > 400) return null;
        return (f, t);
    }

    [HttpGet("preview")]
    public async Task<IActionResult> Preview([FromQuery] int studentId, [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var institutionId = await MyInstitutionIdAsync(me);
            if (institutionId == null || !await IsOwnStudentAsync(institutionId.Value, studentId))
                return NotFound(new { error = "Élève introuvable dans votre établissement." });
            var p = Period(from, to);
            if (p == null) return BadRequest(new { error = "Période invalide (400 jours au plus)." });

            var data = await ComputeAsync(institutionId.Value, me, studentId, p.Value.from, p.Value.to);
            return data == null ? NotFound(new { error = "Élève introuvable." }) : Ok(new { data, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing bulletin preview");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Share([FromBody] ShareBulletinRequest req)
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var institutionId = await MyInstitutionIdAsync(me);
            if (institutionId == null || !await IsOwnStudentAsync(institutionId.Value, req.StudentId))
                return NotFound(new { error = "Élève introuvable dans votre établissement." });
            var p = Period(req.From, req.To);
            if (p == null) return BadRequest(new { error = "Période invalide (400 jours au plus)." });
            var label = (req.PeriodLabel ?? string.Empty).Trim();
            if (label.Length == 0) return BadRequest(new { error = "Donnez un nom à la période (ex. 1er trimestre)." });
            if (label.Length > 100) label = label[..100];

            var data = await ComputeAsync(institutionId.Value, me, req.StudentId, p.Value.from, p.Value.to);
            if (data == null) return NotFound(new { error = "Élève introuvable." });

            var bulletin = new InstitutionBulletin
            {
                InstitutionId = institutionId.Value,
                StudentId = req.StudentId,
                CreatedByUserId = me,
                PeriodLabel = label,
                PeriodStart = p.Value.from,
                PeriodEnd = p.Value.to,
                Appreciation = string.IsNullOrWhiteSpace(req.Appreciation) ? null : req.Appreciation.Trim(),
                DataJson = JsonSerializer.Serialize(data, Json),
            };
            _db.InstitutionBulletins.Add(bulletin);
            await _db.SaveChangesAsync();

            var parents = await _db.ParentStudentLinks.AsNoTracking()
                .Where(l => l.StudentId == req.StudentId && l.Status == "accepted").Select(l => l.ParentId).ToListAsync();
            Notify(data.InstitutionName, data.StudentName, label, bulletin.Id, req.StudentId, parents);

            return Ok(new { data = new { bulletin.Id, parents = parents.Count }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sharing bulletin");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    private void Notify(string institutionName, string studentName, string label, int bulletinId, int studentId, IReadOnlyList<int> parentIds)
    {
        var scopes = _scopes;
        var logger = _logger;
        _ = Task.Run(async () =>
        {
            using var scope = scopes.CreateScope();
            var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();
            var db = scope.ServiceProvider.GetService<ApplicationDbContext>();
            // Lot 10.6 : dans la langue de chaque destinataire (User.Locale).
            async Task Send(int userId, LocalizedText title, LocalizedText message)
            {
                try
                {
                    await ntfy.PublishLocalizedAsync(db, $"winplus-user-{userId}", title, message, userId: userId,
                        type: "InstitutionBulletin", relatedEntityType: "InstitutionBulletin", relatedEntityId: bulletinId);
                }
                catch (Exception ex) { logger.LogWarning(ex, "Bulletin {BulletinId} non notifié à {UserId}", bulletinId, userId); }
            }
            await Send(studentId, new($"Ton bulletin · {label}", $"Your report card · {label}"),
                new($"{institutionName} a partagé ton bulletin. Ouvre WinPlus pour le consulter.",
                    $"{institutionName} has shared your report card. Open WinPlus to view it."));
            foreach (var p in parentIds)
                await Send(p, new($"Bulletin de {studentName} · {label}", $"{studentName}'s report card · {label}"),
                    new($"{institutionName} a partagé le bulletin de {studentName}. Ouvrez votre espace parent pour le consulter.",
                        $"{institutionName} has shared {studentName}'s report card. Open your parent space to view it."));
        });
    }

    private static object Summary(InstitutionBulletin b, string? studentName) => new
    {
        b.Id, b.StudentId, studentName, b.PeriodLabel, b.PeriodStart, b.PeriodEnd, b.SharedAt,
        institutionName = b.Institution?.Name,
    };

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? studentId)
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var institutionId = await MyInstitutionIdAsync(me);
            var rows = await _db.InstitutionBulletins.AsNoTracking().Include(b => b.Institution)
                .Where(b => b.InstitutionId == institutionId && !b.IsDeleted && (studentId == null || b.StudentId == studentId))
                .OrderByDescending(b => b.SharedAt).Take(200).ToListAsync();
            var ids = rows.Select(r => r.StudentId).Distinct().ToList();
            var names = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());
            return Ok(new { data = rows.Select(b => Summary(b, names.GetValueOrDefault(b.StudentId))), success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing bulletins");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        try
        {
            var me = User.GetUserId();
            var role = User.GetUserRole();
            List<int> students = role == "student" ? new List<int> { me }
                : role == "parent" ? await _db.ParentStudentLinks.AsNoTracking()
                    .Where(l => l.ParentId == me && l.Status == "accepted").Select(l => l.StudentId).ToListAsync()
                : new List<int>();
            if (students.Count == 0) return Ok(new { data = Array.Empty<object>(), success = true });

            var rows = await _db.InstitutionBulletins.AsNoTracking().Include(b => b.Institution)
                .Where(b => !b.IsDeleted && students.Contains(b.StudentId))
                .OrderByDescending(b => b.SharedAt).Take(50).ToListAsync();
            var names = await _db.Users.AsNoTracking().Where(u => students.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());
            return Ok(new { data = rows.Select(b => Summary(b, names.GetValueOrDefault(b.StudentId))), success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing my bulletins");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get([FromRoute] int id)
    {
        try
        {
            var me = User.GetUserId();
            var role = User.GetUserRole();
            var b = await _db.InstitutionBulletins.AsNoTracking().Include(x => x.Institution).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (b == null) return NotFound(new { error = "Bulletin introuvable." });

            var allowed = role switch
            {
                "institution" => await MyInstitutionIdAsync(me) == b.InstitutionId,
                "student" => b.StudentId == me,
                "parent" => await _db.ParentStudentLinks.AnyAsync(l => l.ParentId == me && l.StudentId == b.StudentId && l.Status == "accepted"),
                _ => false,
            };
            if (!allowed) return NotFound(new { error = "Bulletin introuvable." });

            var data = JsonSerializer.Deserialize<BulletinData>(b.DataJson, Json);
            return Ok(new
            {
                data = new
                {
                    b.Id, b.PeriodLabel, b.PeriodStart, b.PeriodEnd, b.SharedAt, b.Appreciation,
                    institutionName = b.Institution?.Name, bulletin = data,
                },
                success = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting bulletin");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        var me = User.GetUserId();
        if (User.GetUserRole() != "institution") return Forbid();
        var institutionId = await MyInstitutionIdAsync(me);
        var b = await _db.InstitutionBulletins.FirstOrDefaultAsync(x => x.Id == id && x.InstitutionId == institutionId && !x.IsDeleted);
        if (b == null) return NotFound(new { error = "Bulletin introuvable." });
        b.IsDeleted = true;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}
