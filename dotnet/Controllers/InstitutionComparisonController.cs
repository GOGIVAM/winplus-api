using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Comparaison entre établissements (anonyme), sur les 90 derniers jours :
/// rang de l'établissement parmi ceux de sa région (repli : ville, puis pays),
/// moyenne comparée à la moyenne régionale et à la meilleure, et écart par matière.
/// Aucun autre établissement n'est nommé ; il faut au moins 3 établissements
/// comparables (avec des élèves actifs) pour afficher un résultat.
///
/// GET /api/institution/comparison
/// </summary>
[ApiController]
[Route("api/institution/comparison")]
[Authorize]
public class InstitutionComparisonController : ControllerBase
{
    private const int MinInstitutions = 3;
    private const int MinStudents = 5;
    private readonly ApplicationDbContext _db;

    public InstitutionComparisonController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (User.GetUserRole() != "institution") return Forbid();
        var me = User.GetUserId();
        var myId = await _db.Users.Where(u => u.Id == me).Select(u => u.InstitutionId).FirstOrDefaultAsync();
        if (myId == null) return Ok(new { data = new { available = false, reason = "Compte non rattaché à un établissement." }, success = true });
        var mine = await _db.Institutions.AsNoTracking().Where(i => i.Id == myId).Select(i => new { i.Id, i.Region, i.City, i.Country }).FirstAsync();

        // Périmètre : région, sinon ville, sinon pays.
        var all = await _db.Institutions.AsNoTracking().Where(i => !i.IsDeleted && i.IsActive)
            .Select(i => new { i.Id, i.Region, i.City, i.Country }).ToListAsync();
        var since = DateTime.UtcNow.AddDays(-90);
        var scopes = new List<(string label, List<int> ids)>();
        if (!string.IsNullOrWhiteSpace(mine.Region)) scopes.Add(($"région {mine.Region}", all.Where(i => i.Region == mine.Region).Select(i => i.Id).ToList()));
        if (!string.IsNullOrWhiteSpace(mine.City)) scopes.Add(($"ville de {mine.City}", all.Where(i => i.City == mine.City).Select(i => i.Id).ToList()));
        scopes.Add(("pays", all.Where(i => i.Country == mine.Country).Select(i => i.Id).ToList()));

        foreach (var (label, ids) in scopes)
        {
            var rows = await (from s in _db.InstitutionStudents.AsNoTracking()
                              join a in _db.QuizAttempts.AsNoTracking() on s.StudentId equals a.UserId
                              where ids.Contains(s.InstitutionId) && s.IsActive
                                    && s.ConfirmationStatus == InstitutionAttachmentStatuses.Accepted
                                    && a.IsCompleted && a.CompletedAt >= since
                              select new { s.InstitutionId, s.StudentId, a.Score, subject = a.Quiz != null ? a.Quiz.Subject : null })
                             .ToListAsync();

            // Établissements comparables : au moins MinStudents élèves actifs.
            var perInst = rows.GroupBy(r => r.InstitutionId)
                .Select(g => new { id = g.Key, students = g.Select(x => x.StudentId).Distinct().Count(), avg = g.Average(x => (double)x.Score), rows = g.ToList() })
                .Where(x => x.students >= MinStudents)
                .ToList();
            if (perInst.Count < MinInstitutions || perInst.All(x => x.id != mine.Id)) continue;

            var ordered = perInst.OrderByDescending(x => x.avg).ToList();
            var me2 = ordered.First(x => x.id == mine.Id);
            var rank = ordered.FindIndex(x => x.id == mine.Id) + 1;

            var subjects = me2.rows.Where(r => !string.IsNullOrWhiteSpace(r.subject))
                .GroupBy(r => r.subject!)
                .Where(g => g.Count() >= 5)
                .Select(g =>
                {
                    var others = perInst.Select(p => p.rows.Where(r => r.subject == g.Key).Select(r => (double)r.Score).ToList())
                        .Where(l => l.Count >= 5).Select(l => l.Average()).ToList();
                    var mineAvg = g.Average(r => (double)r.Score);
                    return new
                    {
                        subject = g.Key,
                        mine = Math.Round(mineAvg, 1),
                        region = others.Count == 0 ? (double?)null : Math.Round(others.Average(), 1),
                        best = others.Count == 0 ? (double?)null : Math.Round(others.Max(), 1),
                        quizzes = g.Count(),
                    };
                })
                .OrderByDescending(x => x.quizzes).Take(8).ToList();

            return Ok(new
            {
                data = new
                {
                    available = true,
                    scope = label,
                    institutions = ordered.Count,
                    rank,
                    percentile = (int)Math.Round((ordered.Count - rank) * 100.0 / Math.Max(1, ordered.Count - 1)),
                    mine = Math.Round(me2.avg, 1),
                    regionAverage = Math.Round(ordered.Average(x => x.avg), 1),
                    best = Math.Round(ordered[0].avg, 1),
                    students = me2.students,
                    subjects,
                },
                success = true,
            });
        }

        return Ok(new { data = new { available = false, reason = $"Pas encore assez d'établissements comparables (il en faut {MinInstitutions} avec au moins {MinStudents} élèves actifs sur 90 jours)." }, success = true });
    }
}
