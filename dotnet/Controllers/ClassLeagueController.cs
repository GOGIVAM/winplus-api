using Backend.Data;
using Backend.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Ligue hebdomadaire des classes d'un établissement : un classement amical
/// fondé sur la RÉGULARITÉ, pas sur les notes.
///   points d'un élève = 10 par jour actif (au moins un quiz) + 2 par quiz terminé
///                     + 15 par devoir d'établissement fait dans la semaine ;
///   points d'une classe = moyenne des points de ses élèves (une petite classe
///   n'est pas désavantagée).
/// Semaine du lundi 00:00 au dimanche, heure du Cameroun (UTC+1).
///
/// GET /api/league/mine          élève : la ligue de son établissement, sa classe, sa contribution
/// GET /api/league/institution   établissement : la ligue complète et le vainqueur de la semaine passée
/// </summary>
[ApiController]
[Route("api/league")]
[Authorize]
public class ClassLeagueController : ControllerBase
{
    private static readonly TimeSpan Cameroon = TimeSpan.FromHours(1);
    private readonly ApplicationDbContext _db;
    public ClassLeagueController(ApplicationDbContext db) => _db = db;

    private static (DateTime start, DateTime end) Week(int offset = 0)
    {
        var local = DateTime.UtcNow + Cameroon;
        var monday = local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7)).AddDays(7 * offset);
        return (DateTime.SpecifyKind(monday - Cameroon, DateTimeKind.Utc), DateTime.SpecifyKind(monday.AddDays(7) - Cameroon, DateTimeKind.Utc));
    }

    private record ClassScore(int GroupId, string Name, int Students, int ActiveStudents, double Points, Dictionary<int, double> PerStudent);

    private async Task<List<ClassScore>> ComputeAsync(int ownerUserId, DateTime start, DateTime end)
    {
        var groups = await _db.StudyGroups.AsNoTracking().Where(g => g.OwnerId == ownerUserId && g.IsActive)
            .Select(g => new { g.Id, g.Name }).ToListAsync();
        var groupIds = groups.Select(g => g.Id).ToList();
        var members = await _db.StudyGroupMembers.AsNoTracking()
            .Where(m => groupIds.Contains(m.StudyGroupId) && m.Role != InstitutionTeachersController.TeacherRole && m.UserId != ownerUserId)
            .Select(m => new { m.StudyGroupId, m.UserId }).ToListAsync();
        var userIds = members.Select(m => m.UserId).Distinct().ToList();

        var attempts = await _db.QuizAttempts.AsNoTracking()
            .Where(a => userIds.Contains(a.UserId) && a.IsCompleted && a.CompletedAt >= start && a.CompletedAt < end)
            .Select(a => new { a.UserId, a.QuizId, a.CompletedAt }).ToListAsync();
        var homework = await _db.InstitutionHomework.AsNoTracking()
            .Where(h => groupIds.Contains(h.StudyGroupId) && !h.IsDeleted)
            .Select(h => new { h.StudyGroupId, h.QuizId, h.CreatedAt }).ToListAsync();

        double PointsOf(int userId, int groupId)
        {
            var mine = attempts.Where(a => a.UserId == userId).ToList();
            var days = mine.Select(a => (a.CompletedAt + Cameroon).Date).Distinct().Count();
            var hwDone = homework.Where(h => h.StudyGroupId == groupId)
                .Count(h => mine.Any(a => a.QuizId == h.QuizId && a.CompletedAt >= h.CreatedAt));
            return days * 10 + mine.Count * 2 + hwDone * 15;
        }

        return groups.Select(g =>
        {
            var ids = members.Where(m => m.StudyGroupId == g.Id).Select(m => m.UserId).Distinct().ToList();
            var per = ids.ToDictionary(id => id, id => PointsOf(id, g.Id));
            return new ClassScore(g.Id, g.Name, ids.Count, per.Count(p => p.Value > 0),
                ids.Count == 0 ? 0 : Math.Round(per.Values.Average(), 1), per);
        })
        .Where(c => c.Students > 0)
        .OrderByDescending(c => c.Points).ThenByDescending(c => c.ActiveStudents)
        .ToList();
    }

    private static object Table(List<ClassScore> list) => list.Select((c, i) => new
    {
        rank = i + 1, groupId = c.GroupId, name = c.Name, students = c.Students, activeStudents = c.ActiveStudents, points = c.Points,
    });

    [HttpGet("institution")]
    public async Task<IActionResult> Institution()
    {
        if (User.GetUserRole() != "institution") return Forbid();
        var me = User.GetUserId();
        var (start, end) = Week();
        var (pStart, pEnd) = Week(-1);
        var current = await ComputeAsync(me, start, end);
        var previous = await ComputeAsync(me, pStart, pEnd);
        return Ok(new
        {
            data = new
            {
                weekStart = start, weekEnd = end,
                table = Table(current),
                lastWinner = previous.FirstOrDefault(p => p.Points > 0) is { } w ? new { w.Name, w.Points } : null,
            },
            success = true,
        });
    }

    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        var me = User.GetUserId();
        // Classe d'établissement de l'élève (la première s'il en a plusieurs).
        var membership = await _db.StudyGroupMembers.AsNoTracking()
            .Where(m => m.UserId == me && m.Role != InstitutionTeachersController.TeacherRole && m.StudyGroup != null && m.StudyGroup.IsActive
                        && _db.Users.Any(u => u.Id == m.StudyGroup.OwnerId && u.Role == "institution"))
            .Select(m => new { m.StudyGroupId, ownerId = m.StudyGroup!.OwnerId }).FirstOrDefaultAsync();
        if (membership == null) return Ok(new { data = (object?)null, success = true });

        var (start, end) = Week();
        var table = await ComputeAsync(membership.ownerId, start, end);
        if (table.Count < 2) return Ok(new { data = (object?)null, success = true });
        var myClass = table.FirstOrDefault(c => c.GroupId == membership.StudyGroupId);
        if (myClass == null) return Ok(new { data = (object?)null, success = true });
        var rank = table.IndexOf(myClass) + 1;
        var myPoints = myClass.PerStudent.GetValueOrDefault(me);
        var ahead = rank > 1 ? table[rank - 2] : null;

        return Ok(new
        {
            data = new
            {
                weekEnd = end,
                myClass = new { myClass.Name, rank, myClass.Points, total = table.Count, myClass.ActiveStudents, myClass.Students },
                myPoints,
                gapToNext = ahead == null ? (double?)null : Math.Round(ahead.Points - myClass.Points, 1),
                nextName = ahead?.Name,
                top = Table(table.Take(5).ToList()),
            },
            success = true,
        });
    }
}
