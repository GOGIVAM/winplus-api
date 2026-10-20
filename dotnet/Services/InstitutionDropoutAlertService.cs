using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Alertes de décrochage envoyées aux parents, en complément des seuils WinAI
/// (ParentAdvisorThresholdService : baisse de moyenne, devoir de professeur en
/// retard, examen imminent sans révision). Une fois par jour :
///  - Inactivité : aucun quiz ni session d'étude depuis 7 jours, pour un élève
///    qui avait travaillé avant (pas les comptes tout juste créés) ;
///  - Devoir d'établissement non fait, date limite passée ;
///  - Examen blanc manqué (fermé sans copie valide).
/// Même table (ParentAlerts, déduplication par DedupKey) et même canal
/// (NtfyService, préférences respectées) que les alertes WinAI : le parent les
/// retrouve au même endroit. L'élève reçoit aussi un rappel d'inactivité.
/// </summary>
public sealed class InstitutionDropoutAlertService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(10);
    private const int InactiveDays = 7;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InstitutionDropoutAlertService> _logger;

    public InstitutionDropoutAlertService(IServiceScopeFactory scopeFactory, ILogger<InstitutionDropoutAlertService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "InstitutionDropoutAlertService run failed."); }
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    // Lot 10.6 : Title et Content restent le texte français enregistré dans
    // ParentAlerts ; TitleEn et ContentEn servent à la notification d'un parent anglophone.
    private sealed record Candidate(int ChildId, string Type, string Severity, string Title, string Content, string DedupKey,
        string TitleEn, string ContentEn);

    public async Task RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();
        var now = DateTime.UtcNow;

        var links = await db.ParentStudentLinks.AsNoTracking()
            .Where(l => l.Status == "accepted")
            .Select(l => new { l.ParentId, l.StudentId })
            .ToListAsync(ct);
        if (links.Count == 0) return;
        var childIds = links.Select(l => l.StudentId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => childIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new { u.FirstName, u.CreatedAt }, ct);

        var candidates = new List<Candidate>();

        // ── 1. Inactivité prolongée ─────────────────────────────────────────
        var since = now.AddDays(-InactiveDays);
        var lastQuiz = await db.QuizAttempts.AsNoTracking().Where(a => childIds.Contains(a.UserId) && a.IsCompleted)
            .GroupBy(a => a.UserId).Select(g => new { id = g.Key, last = g.Max(a => a.CompletedAt) }).ToListAsync(ct);
        var lastStudy = await db.StudySessions.AsNoTracking().Where(s => childIds.Contains(s.UserId))
            .GroupBy(s => s.UserId).Select(g => new { id = g.Key, last = g.Max(s => s.CreatedAt) }).ToListAsync(ct);
        var last = lastQuiz.Concat(lastStudy).GroupBy(x => x.id).ToDictionary(g => g.Key, g => g.Max(x => x.last));
        foreach (var child in childIds)
        {
            if (!last.TryGetValue(child, out var lastAt)) continue; // n'a jamais travaillé : pas un décrochage
            if (lastAt >= since) continue;
            var days = (int)(now - lastAt).TotalDays;
            var first = names.TryGetValue(child, out var n) ? n.FirstName ?? "Votre enfant" : "Votre enfant";
            var firstEn = names.TryGetValue(child, out var nEn) ? nEn.FirstName ?? "Your child" : "Your child";
            candidates.Add(new Candidate(child, "InactiviteProlongee", days >= 14 ? "High" : "Medium",
                "Plus d'activité depuis une semaine",
                $"{first} n'a fait aucun quiz ni session de révision depuis {days} jours. Un petit encouragement peut suffire à relancer le rythme.",
                // Une alerte par période d'inactivité (date de la dernière activité).
                $"inactive:{lastAt:yyyy-MM-dd}",
                "No activity for a week",
                $"{firstEn} has done no quiz or revision session for {days} days. A little encouragement may be enough to get going again."));
        }

        // ── 2. Devoirs d'établissement non faits ─────────────────────────────
        var homework = await db.InstitutionHomework.AsNoTracking()
            .Where(h => !h.IsDeleted && h.DueDate != null && h.DueDate < now && h.DueDate > now.AddDays(-14))
            .Select(h => new { h.Id, h.Title, h.QuizId, h.CreatedAt, h.StudyGroupId, h.DueDate })
            .ToListAsync(ct);
        foreach (var h in homework)
        {
            var members = await db.StudyGroupMembers.AsNoTracking()
                .Where(m => m.StudyGroupId == h.StudyGroupId && m.Role != "teacher" && childIds.Contains(m.UserId))
                .Select(m => m.UserId).ToListAsync(ct);
            if (members.Count == 0) continue;
            var done = await db.QuizAttempts.AsNoTracking()
                .Where(a => a.QuizId == h.QuizId && a.IsCompleted && a.CompletedAt >= h.CreatedAt && members.Contains(a.UserId))
                .Select(a => a.UserId).Distinct().ToListAsync(ct);
            foreach (var child in members.Except(done))
            {
                var first = names.TryGetValue(child, out var n) ? n.FirstName ?? "Votre enfant" : "Votre enfant";
                var firstEn = names.TryGetValue(child, out var nEn) ? nEn.FirstName ?? "Your child" : "Your child";
                candidates.Add(new Candidate(child, "DevoirEtablissementNonFait", "Medium",
                    "Devoir non rendu",
                    $"{first} n'a pas fait le devoir « {h.Title} » donné par son établissement (date limite : {h.DueDate:dd/MM}).",
                    $"insthw:{h.Id}",
                    "Homework not handed in",
                    $"{firstEn} has not done the homework \"{h.Title}\" set by their school (deadline: {h.DueDate:dd/MM})."));
            }
        }

        // ── 3. Examens blancs manqués ────────────────────────────────────────
        var exams = await db.InstitutionMockExams.AsNoTracking()
            .Where(e => !e.IsDeleted && e.ClosesAt < now && e.ClosesAt > now.AddDays(-7))
            .ToListAsync(ct);
        foreach (var e in exams)
        {
            var groups = e.GroupIdList();
            var members = await db.StudyGroupMembers.AsNoTracking()
                .Where(m => groups.Contains(m.StudyGroupId) && m.Role != "teacher" && childIds.Contains(m.UserId))
                .Select(m => m.UserId).Distinct().ToListAsync(ct);
            if (members.Count == 0) continue;
            var took = await db.QuizAttempts.AsNoTracking()
                .Where(a => a.QuizId == e.QuizId && a.IsCompleted && a.StartedAt >= e.OpensAt && a.CompletedAt <= e.ClosesAt && members.Contains(a.UserId))
                .Select(a => a.UserId).Distinct().ToListAsync(ct);
            foreach (var child in members.Except(took))
            {
                var first = names.TryGetValue(child, out var n) ? n.FirstName ?? "Votre enfant" : "Votre enfant";
                var firstEn = names.TryGetValue(child, out var nEn) ? nEn.FirstName ?? "Your child" : "Your child";
                candidates.Add(new Candidate(child, "ExamenBlancManque", "High",
                    "Examen blanc manqué",
                    $"{first} n'a pas passé l'examen blanc « {e.Title} » (fermé le {e.ClosesAt:dd/MM}).",
                    $"mock:{e.Id}",
                    "Mock exam missed",
                    $"{firstEn} did not sit the mock exam \"{e.Title}\" (closed on {e.ClosesAt:dd/MM})."));
            }
        }

        if (candidates.Count == 0) return;

        var types = candidates.Select(c => c.Type).Distinct().ToList();
        var existing = (await db.ParentAlerts.AsNoTracking()
                .Where(a => a.DedupKey != null && types.Contains(a.Type))
                .Select(a => new { a.ParentId, a.ChildId, a.Type, a.DedupKey })
                .ToListAsync(ct))
            .Select(a => (a.ParentId, a.ChildId, a.Type, a.DedupKey!)).ToHashSet();
        var nudgedStudents = new HashSet<int>();
        var count = 0;

        foreach (var c in candidates)
        {
            foreach (var parentId in links.Where(l => l.StudentId == c.ChildId).Select(l => l.ParentId).Distinct())
            {
                if (ct.IsCancellationRequested) return;
                if (existing.Contains((parentId, c.ChildId, c.Type, c.DedupKey))) continue;
                db.ParentAlerts.Add(new ParentAlert
                {
                    ParentId = parentId, ChildId = c.ChildId, Type = c.Type, Severity = c.Severity,
                    Content = c.Content, DedupKey = c.DedupKey, DetectedAt = now,
                });
                await db.SaveChangesAsync(ct); // avant la notification : jamais deux fois
                existing.Add((parentId, c.ChildId, c.Type, c.DedupKey));
                count++;
                try
                {
                    // Lot 10.6 : notification dans la langue du parent (User.Locale).
                    await ntfy.PublishLocalizedAsync(db, $"winplus-user-{parentId}", new(c.Title, c.TitleEn), new(c.Content, c.ContentEn),
                        priority: c.Severity == "High" ? "high" : "default",
                        userId: parentId, type: "ParentAdvisor", relatedEntityType: "Child", relatedEntityId: c.ChildId);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Alerte {Type} non notifiée au parent {ParentId}", c.Type, parentId); }

                // L'élève inactif reçoit lui aussi un rappel (une fois par période).
                if (c.Type == "InactiviteProlongee" && nudgedStudents.Add(c.ChildId))
                {
                    try
                    {
                        await ntfy.PublishLocalizedAsync(db, $"winplus-user-{c.ChildId}", new("On reprend ?", "Shall we get back to it?"),
                            new("Cela fait une semaine sans révision. Un quiz de 10 minutes suffit pour relancer ta série.",
                                "It has been a week without revision. A 10-minute quiz is enough to restart your streak."),
                            userId: c.ChildId, type: "StudyReminder");
                    }
                    catch (Exception ex) { _logger.LogWarning(ex, "Rappel d'inactivité non envoyé à {StudentId}", c.ChildId); }
                }
            }
        }
        if (count > 0) _logger.LogInformation("InstitutionDropoutAlertService : {Count} alerte(s) parent créée(s).", count);
    }
}
