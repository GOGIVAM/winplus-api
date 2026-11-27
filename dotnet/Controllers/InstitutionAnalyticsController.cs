using Backend.Data;
using Backend.Extensions;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Analyses de l'établissement, vue d'ensemble (27/11/2026, remarque G8 de Georges : l'onglet Analyses était
/// « trop moche et pas assez complet »).
///   GET /api/institution/analytics/overview?period=7d|30d|term|year&amp;groupId=
/// Compte établissement seulement. Scores en POURCENTAGE de 0 à 100. Un champ sans donnée est omis (pas 0).
/// *Prev = période précédente de même durée. Activité = quiz, leçon, révision ou téléchargement.
/// </summary>
[ApiController]
[Route("api/institution/analytics")]
[Authorize]
public class InstitutionAnalyticsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<InstitutionAnalyticsController> _logger;

    public InstitutionAnalyticsController(ApplicationDbContext db, ILogger<InstitutionAnalyticsController> logger)
    {
        _db = db; _logger = logger;
    }

    private static ObjectResult Refus(int status, string code, string message) =>
        new(new { success = false, code, error = message, message }) { StatusCode = status };

    private static double? Avg(IEnumerable<decimal> scores)
    {
        var list = scores.ToList();
        return list.Count == 0 ? null : Math.Round((double)list.Average(), 1);
    }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] string? period, [FromQuery] int? groupId, CancellationToken ct)
    {
        if (User.GetUserRole() != "institution" && !User.IsAdmin())
            return Refus(403, "role_institution", "Ces analyses sont réservées au compte établissement.");
        var now = DateTime.UtcNow;
        if (InstitutionAnalytics.ResolvePeriod(period, now) is not var (from, to))
            return Refus(400, "periode_invalide", "Période invalide : 7d, 30d, term ou year.");
        var prevFrom = from - (to - from);

        var me = User.GetUserId();
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == me, ct);
        var institutionId = user?.InstitutionId;

        // Classes : groupes créés par le compte établissement.
        var groups = await _db.StudyGroups.AsNoTracking().Where(g => g.OwnerId == me && g.IsActive)
            .Select(g => new { g.Id, g.Name }).ToListAsync(ct);
        var groupIds = groups.Select(g => g.Id).ToList();
        var memberships = await _db.StudyGroupMembers.AsNoTracking()
            .Where(m => groupIds.Contains(m.StudyGroupId) && m.Role != "teacher" && m.UserId != me)
            .Select(m => new { m.StudyGroupId, m.UserId }).ToListAsync(ct);

        // Élèves : ceux de l'établissement, sinon ceux des classes ; restreints à une classe si groupId.
        List<int> students = institutionId != null
            ? await _db.InstitutionStudents.AsNoTracking().Where(s => s.InstitutionId == institutionId && s.IsActive)
                .Select(s => s.StudentId).Distinct().ToListAsync(ct)
            : memberships.Select(m => m.UserId).Distinct().ToList();
        if (groupId != null)
        {
            if (!groupIds.Contains(groupId.Value)) return Refus(404, "classe_introuvable", "Classe introuvable.");
            var inGroup = memberships.Where(m => m.StudyGroupId == groupId).Select(m => m.UserId).ToHashSet();
            students = institutionId != null ? students.Where(inGroup.Contains).ToList() : inGroup.ToList();
        }

        var users = await _db.Users.AsNoTracking().Where(u => students.Contains(u.Id) && u.IsActive && !u.IsDeleted)
            .Select(u => new { u.Id, u.LastLoginAt }).ToListAsync(ct);
        var ids = users.Select(u => u.Id).ToList();

        // Activité de la période et de la précédente, en une lecture par source.
        var events = new List<(int UserId, DateTime At)>();
        events.AddRange((await _db.QuizAttempts.AsNoTracking().Where(a => ids.Contains(a.UserId) && a.CompletedAt >= prevFrom && a.CompletedAt <= to)
            .Select(a => new { a.UserId, a.CompletedAt }).ToListAsync(ct)).Select(x => (x.UserId, x.CompletedAt)));
        events.AddRange((await _db.LessonProgress.AsNoTracking().Where(a => ids.Contains(a.UserId) && a.UpdatedAt >= prevFrom && a.UpdatedAt <= to)
            .Select(a => new { a.UserId, a.UpdatedAt }).ToListAsync(ct)).Select(x => (x.UserId, x.UpdatedAt)));
        events.AddRange((await _db.StudySessions.AsNoTracking().Where(a => ids.Contains(a.UserId) && a.CreatedAt >= prevFrom && a.CreatedAt <= to)
            .Select(a => new { a.UserId, a.CreatedAt }).ToListAsync(ct)).Select(x => (x.UserId, x.CreatedAt)));
        var downloads = await _db.DownloadHistories.AsNoTracking().Where(a => ids.Contains(a.UserId) && a.CreatedAt >= prevFrom && a.CreatedAt <= to)
            .Select(a => new { a.UserId, a.CreatedAt }).ToListAsync(ct);
        events.AddRange(downloads.Select(x => (x.UserId, x.CreatedAt)));

        var attempts = await (from a in _db.QuizAttempts.AsNoTracking()
                              join q in _db.Quizzes.AsNoTracking() on a.QuizId equals q.Id
                              join sub in _db.Subjects.AsNoTracking() on q.SubjectId equals (int?)sub.Id into subs
                              from sub in subs.DefaultIfEmpty()
                              where ids.Contains(a.UserId) && a.CompletedAt >= prevFrom && a.CompletedAt <= to
                              select new { a.UserId, a.QuizId, a.Score, a.CompletedAt, Subject = sub != null ? sub.Title : null })
            .ToListAsync(ct);
        var cur = attempts.Where(a => a.CompletedAt >= from).ToList();
        var prev = attempts.Where(a => a.CompletedAt < from).ToList();
        var activeNow = events.Where(e => e.At >= from).Select(e => e.UserId).ToHashSet();
        var activePrev = events.Where(e => e.At < from).Select(e => e.UserId).ToHashSet();

        // Semaines (lundi) de la période.
        var weekly = new List<object>();
        var monday = from.Date.AddDays(-(((int)from.DayOfWeek + 6) % 7));
        for (var w = monday; w <= to; w = w.AddDays(7))
        {
            var end = w.AddDays(7);
            var wq = cur.Where(a => a.CompletedAt >= w && a.CompletedAt < end).ToList();
            weekly.Add(new
            {
                weekStart = DateOnly.FromDateTime(w),
                active = events.Where(e => e.At >= w && e.At < end && e.At >= from).Select(e => e.UserId).Distinct().Count(),
                quizzes = wq.Count(),
                avgScore = Avg(wq.Select(a => a.Score)),
            });
        }

        var groupRows = groups.Where(g => groupId == null || g.Id == groupId).Select(g =>
        {
            var members = memberships.Where(m => m.StudyGroupId == g.Id).Select(m => m.UserId).Where(ids.Contains).ToHashSet();
            return new
            {
                id = g.Id,
                name = g.Name,
                students = members.Count,
                active = members.Count(activeNow.Contains),
                avgScore = Avg(cur.Where(a => members.Contains(a.UserId)).Select(a => a.Score)),
            };
        }).OrderBy(g => g.name).ToList();

        var subjectRows = cur.GroupBy(a => string.IsNullOrWhiteSpace(a.Subject) ? "Autres" : a.Subject!)
            .Select(g => new { subject = g.Key, attempts = g.Count(), avgScore = Avg(g.Select(a => a.Score)) })
            .OrderByDescending(x => x.attempts).Take(12).ToList();

        // Devoirs et examens blancs de l'établissement (ou des classes du compte).
        var homework = await _db.InstitutionHomework.AsNoTracking()
            .Where(h => !h.IsDeleted && (institutionId != null ? h.InstitutionId == institutionId : groupIds.Contains(h.StudyGroupId))
                && (groupId == null || h.StudyGroupId == groupId) && h.CreatedAt >= from && h.CreatedAt <= to)
            .Select(h => new { h.QuizId, h.StudyGroupId, h.CreatedAt }).ToListAsync(ct);
        var homeworkSubmitted = homework.Sum(h =>
        {
            var members = memberships.Where(m => m.StudyGroupId == h.StudyGroupId).Select(m => m.UserId).ToHashSet();
            return cur.Where(a => a.QuizId == h.QuizId && a.CompletedAt >= h.CreatedAt && members.Contains(a.UserId))
                .Select(a => a.UserId).Distinct().Count();
        });
        var mockQuizIds = institutionId == null ? new List<int>() : await _db.InstitutionMockExams.AsNoTracking()
            .Where(m => m.InstitutionId == institutionId && !m.IsDeleted && m.OpensAt <= to && m.ClosesAt >= from)
            .Select(m => m.QuizId).ToListAsync(ct);
        var mockTaken = cur.Where(a => mockQuizIds.Contains(a.QuizId)).Select(a => (a.UserId, a.QuizId)).Distinct().Count();

        var risk = await InstitutionAnalytics.AtRiskAsync(_db, me, ids, 200, ct);

        var data = new
        {
            period = new { from, to },
            kpis = new
            {
                enrolled = ids.Count,
                neverLoggedIn = users.Count(u => u.LastLoginAt == null),
                active = activeNow.Count,
                activePrev = activePrev.Count,
                avgScore = Avg(cur.Select(a => a.Score)),
                avgScorePrev = Avg(prev.Select(a => a.Score)),
                quizzes = cur.Count(),
                quizzesPrev = prev.Count(),
            },
            weekly,
            groups = groupRows,
            subjects = subjectRows,
            content = new
            {
                downloads = downloads.Count(d => d.CreatedAt >= from),
                homeworkGiven = homework.Count,
                homeworkSubmitted,
                mockExamsTaken = mockTaken,
            },
            atRisk = new
            {
                critical = risk.Count(r => r.RiskScore >= InstitutionAnalytics.CriticalRisk),
                moderate = risk.Count(r => r.RiskScore < InstitutionAnalytics.CriticalRisk),
                top = risk.Take(5).Select(r => new
                {
                    studentId = r.Id,
                    name = $"{r.FirstName} {r.LastName}".Trim(),
                    groupName = r.GroupName,
                    riskReason = r.RiskReason,
                }),
            },
        };
        return Ok(new { success = true, data });
    }
}
