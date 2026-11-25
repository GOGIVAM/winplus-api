using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Comparaison anonyme d'un élève avec ses pairs (GET /api/student/peer-comparison), 25/11/2026.
/// Portées : class (classe d'établissement, sinon classe d'un professeur), level (même niveau),
/// platform (tous les élèves, comportement historique). Fenêtre : 30 derniers jours.
/// Scores généraux : DailyScores (comme avant). Détail par matière : tentatives de quiz terminées
/// (DailyScores ne porte pas la matière). Aucune comparaison sous <see cref="MinSample"/> autres élèves :
/// le bloc vaut null et la raison est donnée, avec une phrase prête à afficher.
/// </summary>
public interface IPeerComparisonService
{
    Task<PeerComparisonResult> ComputeAsync(int userId, string? scope, int? subjectId, CancellationToken ct = default);
}

public sealed class PeerComparisonException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record PeerGroup(string Label, int Size);
public sealed record PeerScope(string Scope, string Label, bool Available);
public sealed record PeerSubject(int SubjectId, string? Name, double UserAvg, int UserQuizCount,
    double? GroupAvg, double? GroupMedian, int? Percentile, int Size, string? Reason, string? ReasonMessage);

public sealed record PeerComparisonResult
{
    public bool HasData { get; init; }
    public string Scope { get; init; } = "platform";
    public int PeriodDays { get; init; } = PeerComparisonService.PeriodDays;
    public int MinSample { get; init; } = PeerComparisonService.MinSample;
    public int? SubjectId { get; init; }
    public string? SubjectName { get; init; }
    public PeerGroup? Group { get; init; }
    public double? UserAvg { get; init; }
    /// <summary>Ancien nom de groupAvg, gardé pour les clients existants.</summary>
    public double? PeerAvg { get; init; }
    public double? GroupAvg { get; init; }
    public double? GroupMedian { get; init; }
    public int? Percentile { get; init; }
    public double? TopPerformersAvg { get; init; }
    public int SampleSize { get; init; }
    public string? Reason { get; init; }
    public string? ReasonMessage { get; init; }
    public IReadOnlyList<PeerSubject> Subjects { get; init; } = Array.Empty<PeerSubject>();
    public IReadOnlyList<PeerScope> AvailableScopes { get; init; } = Array.Empty<PeerScope>();
}

public class PeerComparisonService : IPeerComparisonService
{
    public const int MinSample = 5;
    public const int PeriodDays = 30;
    /// <summary>La moyenne des meilleurs n'est donnée que si elle porte sur au moins 3 élèves.</summary>
    private const int MinTopGroup = 3;

    private readonly ApplicationDbContext _db;
    public PeerComparisonService(ApplicationDbContext db) => _db = db;

    private sealed record ResolvedGroup(string Scope, string Label, IQueryable<int>? Members);

    public async Task<PeerComparisonResult> ComputeAsync(int userId, string? scope, int? subjectId, CancellationToken ct = default)
    {
        scope = string.IsNullOrWhiteSpace(scope) ? "platform" : scope.Trim().ToLowerInvariant();
        if (scope is not ("class" or "level" or "platform"))
            throw new PeerComparisonException("invalid_scope", "Portée inconnue : class, level ou platform.");

        var classe = await ResolveClassAsync(userId, ct);
        var level = (await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Level).FirstOrDefaultAsync(ct))?.Trim();
        var scopes = new List<PeerScope>
        {
            new("class", classe?.Label ?? "Ma classe", classe != null),
            new("level", string.IsNullOrEmpty(level) ? "Mon niveau" : level, !string.IsNullOrEmpty(level)),
            new("platform", "Tous les élèves", true),
        };

        string? subjectName = null;
        if (subjectId is int sid)
            subjectName = await _db.Subjects.AsNoTracking().Where(s => s.Id == sid).Select(s => s.Title).FirstOrDefaultAsync(ct);

        var baseResult = new PeerComparisonResult { Scope = scope, SubjectId = subjectId, SubjectName = subjectName, AvailableScopes = scopes };

        ResolvedGroup? group = scope switch
        {
            "class" => classe,
            "level" => string.IsNullOrEmpty(level) ? null
                : new ResolvedGroup("level", level, _db.Users.Where(u => u.Role == "student" && u.Level != null && u.Level.Trim().ToLower() == level.ToLower()).Select(u => u.Id)),
            _ => new ResolvedGroup("platform", "Tous les élèves", null),
        };
        if (group == null)
        {
            return scope == "class"
                ? baseResult with { Reason = "no_class", ReasonMessage = "Aucune classe n'est rattachée à ton compte. Ton établissement ou ton professeur peut t'ajouter à ta classe." }
                : baseResult with { Reason = "no_level", ReasonMessage = "Ton niveau n'est pas renseigné : ajoute-le dans ton profil pour te comparer aux élèves de ton niveau." };
        }

        var cutoffDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-PeriodDays));
        var cutoff = DateTime.UtcNow.AddDays(-PeriodDays);

        // Moyenne de l'élève et des autres membres du groupe (générale ou dans une matière).
        double? userAvg;
        List<double> peers;
        if (subjectId is int s1)
        {
            var attempts = SubjectAttempts(cutoff).Where(a => a.SubjectId == s1);
            userAvg = await attempts.Where(a => a.UserId == userId).Select(a => (double?)a.Score).AverageAsync(ct);
            var others = attempts.Where(a => a.UserId != userId);
            if (group.Members != null) others = others.Where(a => group.Members.Contains(a.UserId));
            peers = await others.GroupBy(a => a.UserId).Select(g => g.Average(a => a.Score)).ToListAsync(ct);
        }
        else
        {
            userAvg = await _db.DailyScores.AsNoTracking().Where(d => d.UserId == userId && d.Date >= cutoffDate)
                .Select(d => (double?)d.AverageScore).AverageAsync(ct);
            var others = _db.DailyScores.AsNoTracking().Where(d => d.Date >= cutoffDate && d.UserId != userId);
            if (group.Members != null) others = others.Where(d => group.Members.Contains(d.UserId));
            peers = await others.GroupBy(d => d.UserId).Select(g => g.Average(d => (double)d.AverageScore)).ToListAsync(ct);
        }

        var result = baseResult with { Group = new PeerGroup(group.Label, peers.Count), SampleSize = peers.Count };
        if (userAvg == null)
        {
            return result with
            {
                Reason = "no_scores",
                ReasonMessage = subjectId != null
                    ? "Fais au moins un quiz dans cette matière pour te comparer aux autres élèves."
                    : "Fais au moins un quiz pour te comparer aux autres élèves.",
            };
        }
        result = result with { UserAvg = Math.Round(userAvg.Value, 1) };

        if (subjectId == null)
            result = result with { Subjects = await SubjectRowsAsync(userId, group, cutoff, ct) };

        if (peers.Count < MinSample)
            return result with { Reason = "group_too_small", ReasonMessage = TooSmall(group.Scope, subjectId != null) };

        var groupAvg = Math.Round(peers.Average(), 1);
        var topCount = Math.Max(1, peers.Count / 5);
        return result with
        {
            HasData = true,
            GroupAvg = groupAvg,
            PeerAvg = groupAvg,
            GroupMedian = Math.Round(Median(peers), 1),
            Percentile = Percentile(peers, userAvg.Value),
            TopPerformersAvg = topCount >= MinTopGroup ? Math.Round(peers.OrderByDescending(x => x).Take(topCount).Average(), 1) : null,
        };
    }

    /// <summary>Une ligne par matière travaillée par l'élève sur la période.</summary>
    private async Task<IReadOnlyList<PeerSubject>> SubjectRowsAsync(int userId, ResolvedGroup group, DateTime cutoff, CancellationToken ct)
    {
        var mine = await SubjectAttempts(cutoff).Where(a => a.UserId == userId)
            .GroupBy(a => a.SubjectId)
            .Select(g => new { SubjectId = g.Key, Avg = g.Average(a => a.Score), Count = g.Count() })
            .ToListAsync(ct);
        if (mine.Count == 0) return Array.Empty<PeerSubject>();

        var ids = mine.Select(m => m.SubjectId).ToList();
        var others = SubjectAttempts(cutoff).Where(a => a.UserId != userId && ids.Contains(a.SubjectId));
        if (group.Members != null) others = others.Where(a => group.Members.Contains(a.UserId));
        var peerAvgs = await others.GroupBy(a => new { a.SubjectId, a.UserId })
            .Select(g => new { g.Key.SubjectId, Avg = g.Average(a => a.Score) })
            .ToListAsync(ct);
        var names = await _db.Subjects.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Title, ct);

        return mine.Select(m =>
        {
            var p = peerAvgs.Where(x => x.SubjectId == m.SubjectId).Select(x => x.Avg).ToList();
            var ok = p.Count >= MinSample;
            return new PeerSubject(m.SubjectId, names.GetValueOrDefault(m.SubjectId), Math.Round(m.Avg, 1), m.Count,
                ok ? Math.Round(p.Average(), 1) : null,
                ok ? Math.Round(Median(p), 1) : null,
                ok ? Percentile(p, m.Avg) : null,
                p.Count,
                ok ? null : "group_too_small",
                ok ? null : TooSmall(group.Scope, true));
        }).OrderBy(r => r.Name).ToList();
    }

    // Initialiseur de membres (pas de constructeur) : EF doit pouvoir filtrer et regrouper après la projection.
    private sealed class SubjectAttempt
    {
        public int UserId { get; init; }
        public int SubjectId { get; init; }
        public double Score { get; init; }
    }

    /// <summary>Tentatives terminées d'un quiz rattaché à une matière, sur la période.</summary>
    private IQueryable<SubjectAttempt> SubjectAttempts(DateTime cutoff) =>
        from a in _db.QuizAttempts.AsNoTracking()
        join q in _db.Quizzes.AsNoTracking() on a.QuizId equals q.Id
        where a.IsCompleted && a.CompletedAt >= cutoff && q.SubjectId != null
        select new SubjectAttempt { UserId = a.UserId, SubjectId = q.SubjectId!.Value, Score = (double)a.Score };

    /// <summary>
    /// Classe de l'élève : d'abord son groupe dans un établissement (rattachement actif, non refusé),
    /// puis une classe créée par un compte établissement, sinon la classe active d'un professeur où il a
    /// été ajouté le plus récemment.
    /// </summary>
    private async Task<ResolvedGroup?> ResolveClassAsync(int userId, CancellationToken ct)
    {
        var inst = await _db.InstitutionStudents.AsNoTracking()
            .Where(s => s.StudentId == userId && s.IsActive && s.ConfirmationStatus != InstitutionAttachmentStatuses.Refused
                        && s.GroupName != null && s.GroupName.Trim() != "")
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.InstitutionId, s.GroupName })
            .FirstOrDefaultAsync(ct);
        if (inst != null)
        {
            var name = inst.GroupName!.Trim();
            var lower = name.ToLower();
            return new ResolvedGroup("class", name, _db.InstitutionStudents
                .Where(s => s.InstitutionId == inst.InstitutionId && s.IsActive
                            && s.ConfirmationStatus != InstitutionAttachmentStatuses.Refused
                            && s.GroupName != null && s.GroupName.Trim().ToLower() == lower)
                .Select(s => s.StudentId));
        }

        // Classe créée par le compte établissement (groupes de l'espace établissement, mobile compris).
        var sg = await (from m in _db.StudyGroupMembers.AsNoTracking()
                        join g in _db.StudyGroups.AsNoTracking() on m.StudyGroupId equals g.Id
                        join o in _db.Users.AsNoTracking() on g.OwnerId equals o.Id
                        where m.UserId == userId && m.Role != "teacher" && g.IsActive && o.Role == "institution"
                        orderby m.JoinedAt descending
                        select new { g.Id, g.Name }).FirstOrDefaultAsync(ct);
        if (sg != null)
            return new ResolvedGroup("class", sg.Name, _db.StudyGroupMembers
                .Where(m => m.StudyGroupId == sg.Id && m.Role != "teacher").Select(m => m.UserId));

        var tc = await _db.TeacherClassStudents.AsNoTracking()
            .Where(m => m.StudentId == userId && m.TeacherClass != null && m.TeacherClass.IsActive)
            .OrderByDescending(m => m.AddedAt)
            .Select(m => new { m.TeacherClassId, m.TeacherClass!.Name })
            .FirstOrDefaultAsync(ct);
        if (tc != null)
            return new ResolvedGroup("class", tc.Name, _db.TeacherClassStudents.Where(m => m.TeacherClassId == tc.TeacherClassId).Select(m => m.StudentId));

        return null;
    }

    private static string TooSmall(string scope, bool subject)
    {
        var qui = scope switch { "class" => "élèves de ta classe", "level" => "élèves de ton niveau", _ => "élèves" };
        var ou = subject ? " dans cette matière" : "";
        return $"Trop peu d'{qui} ont fait des quiz{ou} ces {PeriodDays} derniers jours pour comparer (il en faut au moins {MinSample}).";
    }

    private static double Median(List<double> values)
    {
        var s = values.OrderBy(x => x).ToList();
        var mid = s.Count / 2;
        return s.Count % 2 == 1 ? s[mid] : (s[mid - 1] + s[mid]) / 2;
    }

    /// <summary>Part des pairs en dessous de l'élève, en pourcentage (même règle qu'avant).</summary>
    private static int Percentile(List<double> peers, double userAvg) =>
        (int)Math.Round((double)peers.Count(x => x < userAvg) / peers.Count * 100);
}
