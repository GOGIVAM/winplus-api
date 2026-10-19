using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Espace institution, phase 2 (étape 6) : examens blancs et classement interne.
///
/// GET    /api/institution/mock-exams                 examens blancs de l'établissement
/// POST   /api/institution/mock-exams                 programmer un examen blanc
/// GET    /api/institution/mock-exams/{id}/ranking    classement, moyennes par classe, répartition
/// POST   /api/institution/mock-exams/{id}/publish    publier les résultats (élèves notifiés)
/// DELETE /api/institution/mock-exams/{id}            supprimer
/// GET    /api/institution/mock-exams/mine            examens blancs de l'élève (statut, résultat publié)
///
/// Copie retenue : la PREMIÈRE tentative terminée dans la fenêtre d'ouverture,
/// et seulement si elle a été rendue dans la durée (+ 2 min de tolérance réseau).
/// </summary>
[ApiController]
[Route("api/institution/mock-exams")]
[Authorize]
public class InstitutionMockExamsController : ControllerBase
{
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    private readonly ApplicationDbContext _db;
    private readonly IQuizService _quizzes;
    private readonly ILogger<InstitutionMockExamsController> _logger;
    private readonly IServiceScopeFactory _scopes;

    public InstitutionMockExamsController(ApplicationDbContext db, IQuizService quizzes,
        ILogger<InstitutionMockExamsController> logger, IServiceScopeFactory scopes)
    {
        _db = db;
        _quizzes = quizzes;
        _logger = logger;
        _scopes = scopes;
    }

    public record CreateMockExamRequest(List<int> GroupIds, int? QuizId, int? ExamId, string? Title,
        DateTime OpensAt, DateTime ClosesAt, int DurationMinutes);

    private record Copy(int UserId, double Score, DateTime CompletedAt, int Minutes);

    private async Task<int?> MyInstitutionIdAsync(int userId) =>
        await _db.Users.Where(u => u.Id == userId).Select(u => u.InstitutionId).FirstOrDefaultAsync();

    /// <summary>Élèves des classes de l'examen : (élève, classe).</summary>
    private async Task<List<(int UserId, int GroupId, string GroupName)>> ParticipantsAsync(List<int> groupIds)
    {
        var rows = await _db.StudyGroupMembers.AsNoTracking()
            .Where(m => groupIds.Contains(m.StudyGroupId) && m.Role != InstitutionTeachersController.TeacherRole)
            .Select(m => new { m.UserId, m.StudyGroupId, name = m.StudyGroup != null ? m.StudyGroup.Name : "" })
            .ToListAsync();
        // Un élève dans plusieurs classes de l'examen n'est compté qu'une fois.
        return rows.GroupBy(r => r.UserId).Select(g => g.First()).Select(r => (r.UserId, r.StudyGroupId, r.name)).ToList();
    }

    /// <summary>Copie retenue de chaque élève (première tentative valide).</summary>
    private async Task<Dictionary<int, Copy>> CopiesAsync(InstitutionMockExam e, List<int> userIds)
    {
        var attempts = await _db.QuizAttempts.AsNoTracking()
            .Where(a => a.QuizId == e.QuizId && a.IsCompleted && userIds.Contains(a.UserId)
                        && a.StartedAt >= e.OpensAt && a.CompletedAt <= e.ClosesAt)
            .Select(a => new { a.UserId, a.Score, a.StartedAt, a.CompletedAt })
            .ToListAsync();
        var limit = TimeSpan.FromMinutes(e.DurationMinutes) + Grace;
        return attempts.GroupBy(a => a.UserId)
            .Select(g => g.OrderBy(a => a.CompletedAt).First())
            .Where(a => a.CompletedAt - a.StartedAt <= limit)
            .ToDictionary(a => a.UserId, a => new Copy(a.UserId, Math.Round((double)a.Score, 1), a.CompletedAt,
                (int)Math.Ceiling((a.CompletedAt - a.StartedAt).TotalMinutes)));
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var institutionId = await MyInstitutionIdAsync(me);
            if (institutionId == null) return Ok(new { data = Array.Empty<object>(), success = true });

            var exams = await _db.InstitutionMockExams.AsNoTracking().Include(x => x.Quiz)
                .Where(x => x.InstitutionId == institutionId && !x.IsDeleted)
                .OrderByDescending(x => x.OpensAt).Take(50).ToListAsync();
            var groupNames = await _db.StudyGroups.AsNoTracking().Where(g => g.OwnerId == me)
                .ToDictionaryAsync(g => g.Id, g => g.Name);

            var result = new List<object>();
            foreach (var e in exams)
            {
                var groups = e.GroupIdList();
                var people = await ParticipantsAsync(groups);
                var copies = await CopiesAsync(e, people.Select(p => p.UserId).ToList());
                result.Add(new
                {
                    e.Id, e.Title, e.OpensAt, e.ClosesAt, e.DurationMinutes, e.ResultsPublishedAt, e.QuizId,
                    subject = e.Quiz?.Subject,
                    groups = groups.Select(id => new { id, name = groupNames.GetValueOrDefault(id) ?? "Classe" }),
                    expected = people.Count,
                    submitted = copies.Count,
                    average = copies.Count == 0 ? (double?)null : Math.Round(copies.Values.Average(c => c.Score), 1),
                });
            }
            return Ok(new { data = result, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing mock exams");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMockExamRequest req)
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var institutionId = await MyInstitutionIdAsync(me);
            if (institutionId == null) return BadRequest(new { error = "Compte non rattaché à un établissement." });

            var groupIds = (req.GroupIds ?? new List<int>()).Distinct().ToList();
            if (groupIds.Count == 0) return BadRequest(new { error = "Choisissez au moins une classe." });
            var owned = await _db.StudyGroups.CountAsync(g => groupIds.Contains(g.Id) && g.OwnerId == me && g.IsActive);
            if (owned != groupIds.Count) return NotFound(new { error = "Classe introuvable." });
            if ((req.QuizId == null) == (req.ExamId == null)) return BadRequest(new { error = "Choisissez un quiz ou une épreuve." });
            if (req.ClosesAt <= req.OpensAt) return BadRequest(new { error = "La fermeture doit suivre l'ouverture." });
            if (req.ClosesAt <= DateTime.UtcNow) return BadRequest(new { error = "La fermeture doit être dans le futur." });
            if (req.DurationMinutes is < 5 or > 480) return BadRequest(new { error = "Durée entre 5 minutes et 8 heures." });

            int quizId; string defaultTitle;
            if (req.QuizId != null)
            {
                var quiz = await _db.Quizzes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == req.QuizId && !q.IsDeleted);
                if (quiz == null) return NotFound(new { error = "Quiz introuvable." });
                quizId = quiz.Id; defaultTitle = quiz.Title;
            }
            else
            {
                try { var q = await _quizzes.GetOrCreateExamQuizAsync(req.ExamId!.Value); quizId = q.Id; defaultTitle = q.Title; }
                catch (KeyNotFoundException) { return NotFound(new { error = "Épreuve introuvable." }); }
                catch (InvalidOperationException e) { return BadRequest(new { error = e.Message }); }
            }

            var title = string.IsNullOrWhiteSpace(req.Title) ? $"Examen blanc · {defaultTitle}" : req.Title.Trim();
            if (title.Length > 200) title = title[..200];
            var exam = new InstitutionMockExam
            {
                InstitutionId = institutionId.Value, CreatedByUserId = me, QuizId = quizId, ExamId = req.ExamId,
                Title = title, GroupIds = string.Join(',', groupIds),
                OpensAt = req.OpensAt, ClosesAt = req.ClosesAt, DurationMinutes = req.DurationMinutes,
            };
            _db.InstitutionMockExams.Add(exam);
            await _db.SaveChangesAsync();

            var people = await ParticipantsAsync(groupIds);
            Notify(people.Select(p => p.UserId).ToList(), $"Examen blanc · {title}",
                $"Ouvert du {exam.OpensAt.ToLocalTime():dd/MM à HH\\hmm} au {exam.ClosesAt.ToLocalTime():dd/MM à HH\\hmm}, durée {exam.DurationMinutes} min. Retrouve-le dans Mes devoirs.",
                exam.Id);
            return Ok(new { data = new { exam.Id, students = people.Count }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating mock exam");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("{id:int}/ranking")]
    public async Task<IActionResult> Ranking([FromRoute] int id)
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var institutionId = await MyInstitutionIdAsync(me);
            var e = await _db.InstitutionMockExams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.InstitutionId == institutionId && !x.IsDeleted);
            if (e == null) return NotFound(new { error = "Examen blanc introuvable." });

            var people = await ParticipantsAsync(e.GroupIdList());
            var copies = await CopiesAsync(e, people.Select(p => p.UserId).ToList());
            var names = await _db.Users.AsNoTracking().Where(u => people.Select(p => p.UserId).Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());

            var ordered = copies.Values.OrderByDescending(c => c.Score).ThenBy(c => c.Minutes).ToList();
            var ranking = ordered.Select((c, i) =>
            {
                var p = people.First(x => x.UserId == c.UserId);
                // Ex æquo : même rang pour une même note.
                var rank = ordered.FindIndex(o => o.Score == c.Score) + 1;
                return new { rank, studentId = c.UserId, name = names.GetValueOrDefault(c.UserId) ?? "Élève", groupName = p.GroupName, c.Score, c.Minutes, c.CompletedAt };
            }).ToList();
            var absent = people.Where(p => !copies.ContainsKey(p.UserId))
                .Select(p => new { studentId = p.UserId, name = names.GetValueOrDefault(p.UserId) ?? "Élève", groupName = p.GroupName })
                .OrderBy(a => a.name).ToList();
            var byClass = people.GroupBy(p => new { p.GroupId, p.GroupName }).Select(g =>
            {
                var cs = g.Where(p => copies.ContainsKey(p.UserId)).Select(p => copies[p.UserId].Score).ToList();
                return new { groupId = g.Key.GroupId, groupName = g.Key.GroupName, students = g.Count(), submitted = cs.Count,
                    average = cs.Count == 0 ? (double?)null : Math.Round(cs.Average(), 1), best = cs.Count == 0 ? (double?)null : cs.Max() };
            }).OrderByDescending(x => x.average ?? -1).ToList();
            var buckets = new[] { 0, 25, 50, 75 }.Select(b => new
            {
                from = b, to = b + 25,
                count = ordered.Count(c => c.Score >= b && (b == 75 ? c.Score <= 100 : c.Score < b + 25)),
            }).ToList();

            return Ok(new
            {
                data = new
                {
                    e.Id, e.Title, e.OpensAt, e.ClosesAt, e.DurationMinutes, e.ResultsPublishedAt,
                    expected = people.Count, submitted = copies.Count,
                    average = copies.Count == 0 ? (double?)null : Math.Round(copies.Values.Average(c => c.Score), 1),
                    ranking, absent, byClass, buckets,
                },
                success = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting mock exam ranking");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPost("{id:int}/publish")]
    public async Task<IActionResult> Publish([FromRoute] int id)
    {
        var me = User.GetUserId();
        if (User.GetUserRole() != "institution") return Forbid();
        var institutionId = await MyInstitutionIdAsync(me);
        var e = await _db.InstitutionMockExams.FirstOrDefaultAsync(x => x.Id == id && x.InstitutionId == institutionId && !x.IsDeleted);
        if (e == null) return NotFound(new { error = "Examen blanc introuvable." });
        if (e.ResultsPublishedAt != null) return Conflict(new { error = "Résultats déjà publiés." });
        e.ResultsPublishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var people = await ParticipantsAsync(e.GroupIdList());
        Notify(people.Select(p => p.UserId).ToList(), $"Résultats · {e.Title}",
            "Les résultats de l'examen blanc sont disponibles : ta note et ton classement t'attendent dans Mes devoirs.", e.Id);
        return Ok(new { success = true });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        var me = User.GetUserId();
        if (User.GetUserRole() != "institution") return Forbid();
        var institutionId = await MyInstitutionIdAsync(me);
        var e = await _db.InstitutionMockExams.FirstOrDefaultAsync(x => x.Id == id && x.InstitutionId == institutionId && !x.IsDeleted);
        if (e == null) return NotFound(new { error = "Examen blanc introuvable." });
        e.IsDeleted = true;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    /// <summary>Examens blancs de l'élève : à venir, ouvert, rendu ; note et rang une fois publiés.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        try
        {
            var me = User.GetUserId();
            var myGroups = await _db.StudyGroupMembers.AsNoTracking()
                .Where(m => m.UserId == me && m.Role != InstitutionTeachersController.TeacherRole)
                .Select(m => m.StudyGroupId).ToListAsync();
            if (myGroups.Count == 0) return Ok(new { data = Array.Empty<object>(), success = true });

            var candidates = await _db.InstitutionMockExams.AsNoTracking().Include(x => x.Quiz)
                .Where(x => !x.IsDeleted).OrderByDescending(x => x.OpensAt).Take(200).ToListAsync();
            var mineExams = candidates.Where(x => x.GroupIdList().Intersect(myGroups).Any()).Take(30).ToList();

            var now = DateTime.UtcNow;
            var result = new List<object>();
            foreach (var e in mineExams)
            {
                var myCopy = (await CopiesAsync(e, new List<int> { me })).GetValueOrDefault(me);
                var anyAttempt = await _db.QuizAttempts.AnyAsync(a => a.QuizId == e.QuizId && a.UserId == me && a.StartedAt >= e.OpensAt && a.CompletedAt <= e.ClosesAt && a.IsCompleted);
                var status = now < e.OpensAt ? "upcoming"
                    : myCopy != null || anyAttempt ? "submitted"
                    : now <= e.ClosesAt ? "open" : "missed";

                int? rank = null, total = null; double? classAverage = null, score = null;
                if (e.ResultsPublishedAt != null)
                {
                    var people = await ParticipantsAsync(e.GroupIdList());
                    var copies = await CopiesAsync(e, people.Select(p => p.UserId).ToList());
                    total = copies.Count;
                    classAverage = copies.Count == 0 ? null : Math.Round(copies.Values.Average(c => c.Score), 1);
                    if (myCopy != null)
                    {
                        score = myCopy.Score;
                        rank = copies.Values.Count(c => c.Score > myCopy.Score) + 1;
                    }
                }
                result.Add(new
                {
                    e.Id, e.Title, e.OpensAt, e.ClosesAt, e.DurationMinutes, e.QuizId, subject = e.Quiz?.Subject,
                    status, published = e.ResultsPublishedAt != null, score, rank, total, average = classAverage,
                });
            }
            return Ok(new { data = result, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting my mock exams");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    private void Notify(IReadOnlyList<int> userIds, string title, string message, int examId)
    {
        if (userIds.Count == 0) return;
        var scopes = _scopes;
        var logger = _logger;
        _ = Task.Run(async () =>
        {
            using var scope = scopes.CreateScope();
            var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();
            foreach (var id in userIds)
            {
                try
                {
                    await ntfy.PublishAsync($"winplus-user-{id}", title, message, userId: id,
                        type: "InstitutionMockExam", relatedEntityType: "InstitutionMockExam", relatedEntityId: examId);
                }
                catch (Exception ex) { logger.LogWarning(ex, "Examen blanc {ExamId} non notifié à {UserId}", examId, id); }
            }
        });
    }
}
