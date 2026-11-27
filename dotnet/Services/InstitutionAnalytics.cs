using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Calculs partagés de l'espace établissement (27/11/2026) : élèves à risque (GET /api/institution/at-risk)
/// et vue d'ensemble des analyses (GET /api/institution/analytics/overview). Scores en POURCENTAGE de 0 à 100.
/// « Activité » = quiz terminé, leçon suivie, séance de révision ou téléchargement.
/// </summary>
public static class InstitutionAnalytics
{
    public sealed record RiskRow(int Id, string FirstName, string LastName, string? GroupName, string Level,
        double RiskScore, string RiskReason, string WeakSubject, int WeakScore, int? GlobalScore, int InactiveDays);

    public const double CriticalRisk = 0.8;

    /// <summary>Dernière activité connue de chaque élève (toutes dates).</summary>
    public static async Task<Dictionary<int, DateTime>> LastActivityAsync(ApplicationDbContext db, List<int> ids, CancellationToken ct = default)
    {
        var quiz = await db.QuizAttempts.AsNoTracking().Where(a => ids.Contains(a.UserId))
            .GroupBy(a => a.UserId).Select(g => new { g.Key, Last = g.Max(a => a.CompletedAt) }).ToListAsync(ct);
        var study = await db.StudySessions.AsNoTracking().Where(x => ids.Contains(x.UserId))
            .GroupBy(x => x.UserId).Select(g => new { g.Key, Last = g.Max(x => x.CreatedAt) }).ToListAsync(ct);
        var download = await db.DownloadHistories.AsNoTracking().Where(x => ids.Contains(x.UserId))
            .GroupBy(x => x.UserId).Select(g => new { g.Key, Last = g.Max(x => x.CreatedAt) }).ToListAsync(ct);
        var lesson = await db.LessonProgress.AsNoTracking().Where(x => ids.Contains(x.UserId))
            .GroupBy(x => x.UserId).Select(g => new { g.Key, Last = g.Max(x => x.UpdatedAt) }).ToListAsync(ct);
        var last = new Dictionary<int, DateTime>();
        foreach (var r in quiz.Concat(study).Concat(download).Concat(lesson))
            if (!last.TryGetValue(r.Key, out var cur) || r.Last > cur) last[r.Key] = r.Last;
        return last;
    }

    /// <summary>Classe de chaque élève : groupe de l'établissement, sinon groupe créé par le compte établissement.</summary>
    public static async Task<Dictionary<int, string>> GroupNamesAsync(ApplicationDbContext db, int ownerUserId, List<int> ids, CancellationToken ct = default)
    {
        var fromInstitution = await db.InstitutionStudents.AsNoTracking()
            .Where(x => ids.Contains(x.StudentId) && x.IsActive && x.GroupName != null)
            .Select(x => new { x.StudentId, x.GroupName }).ToListAsync(ct);
        var map = fromInstitution.GroupBy(x => x.StudentId).ToDictionary(g => g.Key, g => g.First().GroupName!);
        var fromGroups = await (from m in db.StudyGroupMembers.AsNoTracking()
                                join g in db.StudyGroups.AsNoTracking() on m.StudyGroupId equals g.Id
                                where g.OwnerId == ownerUserId && g.IsActive && ids.Contains(m.UserId) && m.Role != "teacher"
                                select new { m.UserId, g.Name }).ToListAsync(ct);
        foreach (var x in fromGroups) map.TryAdd(x.UserId, x.Name);
        return map;
    }

    /// <summary>Élèves à risque : inactivité (0,5 à 7 jours, 1 à 21 jours et plus) ou moyenne des quiz sous 50 % sur 30 jours.</summary>
    public static async Task<List<RiskRow>> AtRiskAsync(ApplicationDbContext db, int ownerUserId, List<int> studentIds, int take, CancellationToken ct = default)
    {
        if (studentIds.Count == 0) return new();
        var now = DateTime.UtcNow;
        var since30 = now.AddDays(-30);
        var lastActivity = await LastActivityAsync(db, studentIds, ct);

        var attempts = await (from a in db.QuizAttempts.AsNoTracking()
                              join q in db.Quizzes.AsNoTracking() on a.QuizId equals q.Id
                              join sub in db.Subjects.AsNoTracking() on q.SubjectId equals (int?)sub.Id into subs
                              from sub in subs.DefaultIfEmpty()
                              where studentIds.Contains(a.UserId) && a.CompletedAt >= since30
                              select new { a.UserId, a.Score, Subject = sub != null ? sub.Title : null })
            .ToListAsync(ct);
        var scoresByUser = attempts.GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.ToList());
        var groups = await GroupNamesAsync(db, ownerUserId, studentIds, ct);

        var users = await db.Users.AsNoTracking()
            .Where(u => studentIds.Contains(u.Id) && u.IsActive && !u.IsDeleted)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Level, u.CreatedAt })
            .ToListAsync(ct);

        return users.Select(u =>
            {
                var last = lastActivity.TryGetValue(u.Id, out var l) ? l : (DateTime?)null;
                var inactiveDays = (int)Math.Max(0, (now - (last ?? u.CreatedAt)).TotalDays);
                var mine = scoresByUser.GetValueOrDefault(u.Id);
                int? global = mine is { Count: > 0 } ? (int)Math.Round(mine.Average(a => a.Score)) : null;
                var weak = mine?.Where(a => a.Subject != null).GroupBy(a => a.Subject!)
                    .Select(g => new { Subject = g.Key, Avg = (int)Math.Round(g.Average(a => a.Score)) })
                    .OrderBy(x => x.Avg).FirstOrDefault();
                var inactivityRisk = inactiveDays < 7 ? 0 : Math.Min(1.0, 0.5 + (inactiveDays - 7) / 28.0);
                var scoreRisk = global is int gs && gs < 50 ? Math.Min(1.0, (50 - gs) / 50.0 + 0.4) : 0;
                var reasons = new List<string>();
                if (inactiveDays >= 7) reasons.Add(last == null ? "Aucune activité enregistrée" : $"Aucune activité depuis {inactiveDays} jours");
                if (global is int g2 && g2 < 50) reasons.Add($"Moyenne des quiz à {g2} % sur 30 jours");
                return new RiskRow(u.Id, u.FirstName ?? "", u.LastName ?? "", groups.GetValueOrDefault(u.Id), u.Level ?? "",
                    Math.Round(Math.Max(inactivityRisk, scoreRisk), 2), string.Join(" ; ", reasons),
                    weak?.Subject ?? "", weak?.Avg ?? 0, global, inactiveDays);
            })
            .Where(r => r.RiskScore > 0)
            .OrderByDescending(r => r.RiskScore).ThenByDescending(r => r.InactiveDays)
            .Take(take)
            .ToList();
    }

    /// <summary>Période demandée : 7d, 30d, term (trimestre scolaire camerounais en cours : sept-déc, janv-mars,
    /// avr-juin ; à défaut les 90 derniers jours), year (année scolaire depuis le 1er septembre).</summary>
    public static (DateTime From, DateTime To)? ResolvePeriod(string? period, DateTime now)
    {
        var today = now.Date;
        switch ((period ?? "30d").Trim().ToLowerInvariant())
        {
            case "7d": return (today.AddDays(-6), now);
            case "30d": return (today.AddDays(-29), now);
            case "term":
                var m = today.Month;
                DateTime? start = m >= 9 ? new DateTime(today.Year, 9, 1) : m <= 3 ? new DateTime(today.Year, 1, 1)
                    : m <= 6 ? new DateTime(today.Year, 4, 1) : null;
                return (DateTime.SpecifyKind(start ?? today.AddDays(-89), DateTimeKind.Utc), now);
            case "year":
                var y = today.Month >= 9 ? today.Year : today.Year - 1;
                return (new DateTime(y, 9, 1, 0, 0, 0, DateTimeKind.Utc), now);
            default: return null;
        }
    }
}
