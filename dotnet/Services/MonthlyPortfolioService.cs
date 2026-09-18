using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.Entities;

namespace Backend.Services;

/// <summary>
/// Portefeuille de compétences : portrait stable de l'apprenant, recalculé
/// une fois par mois pour chaque enfant lié à au moins un parent (accepted).
/// Trois sections purement descriptives  jamais de score, de jauge chiffrée
/// ni de comparaison entre enfants (voir parent_decisions_session.md).
/// Stocké dans ParentReport (ReportType="Portefeuille"), un exemplaire par
/// parent lié, comme WeeklyParentReportService le fait pour la capsule.
/// </summary>
public sealed class MonthlyPortfolioService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MonthlyPortfolioService> _logger;

    public MonthlyPortfolioService(IServiceScopeFactory scopeFactory, ILogger<MonthlyPortfolioService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MonthlyPortfolioService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilNextMonthStart();
            _logger.LogInformation("Next monthly portfolio run in {Days:F1} day(s).", delay.TotalDays);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await RunAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MonthlyPortfolioService run failed.");
            }
        }
    }

    private static TimeSpan ComputeDelayUntilNextMonthStart()
    {
        var now = DateTime.UtcNow;
        var firstOfNextMonth = new DateTime(now.Year, now.Month, 1, 6, 0, 0, DateTimeKind.Utc).AddMonths(1);
        return firstOfNextMonth - now;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("Running MonthlyPortfolioService at {Time}", DateTime.UtcNow);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var links = await db.ParentStudentLinks.AsNoTracking()
            .Where(l => l.Status == "accepted")
            .Select(l => new { l.ParentId, l.StudentId })
            .ToListAsync(ct);

        var childIds = links.Select(l => l.StudentId).Distinct().ToList();
        if (childIds.Count == 0)
        {
            _logger.LogInformation("No linked children  nothing to compute.");
            return;
        }

        var cutoff30 = DateTime.UtcNow.AddDays(-30);
        var computed = 0;

        foreach (var childId in childIds)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var childName = await db.Users.AsNoTracking()
                    .Where(u => u.Id == childId)
                    .Select(u => u.FirstName)
                    .FirstOrDefaultAsync(ct) ?? "Votre enfant";

                var portrait = await BuildPortraitAsync(db, childId, childName, cutoff30, ct);
                var contentJson = JsonSerializer.Serialize(portrait);

                var parentIds = links.Where(l => l.StudentId == childId).Select(l => l.ParentId).Distinct();
                foreach (var parentId in parentIds)
                {
                    db.ParentReports.Add(new ParentReport
                    {
                        ParentId = parentId,
                        ChildId = childId,
                        ReportType = "Portefeuille",
                        Content = contentJson,
                        EmitterType = "System",
                    });
                }

                computed++;
            }
            catch (Exception ex)
            {
                // Un portrait en échec ne doit jamais bloquer les autres enfants.
                _logger.LogWarning(ex, "Failed to build portfolio for child {ChildId}", childId);
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Monthly portfolio computed for {Count}/{Total} children.", computed, childIds.Count);
    }

    private sealed record Portrait(string Regularite, string Autonomie, string Curiosite);

    private async Task<Portrait> BuildPortraitAsync(
        ApplicationDbContext db, int childId, string name, DateTime cutoff30, CancellationToken ct)
    {
        // ── Régularité : jours distincts avec une activité (étude, téléchargement
        // ou quiz) sur 30 jours. UserSession (device management) n'est pas fiable
        // pour reconstituer un historique jour par jour  une ligne par device y
        // est mise à jour en continu plutôt que créée à chaque connexion.
        var studyDays = await db.StudySessions.AsNoTracking()
            .Where(s => s.UserId == childId && s.CreatedAt >= cutoff30)
            .Select(s => s.CreatedAt.Date)
            .ToListAsync(ct);
        var downloadDays = await db.DownloadHistories.AsNoTracking()
            .Where(d => d.UserId == childId && d.CreatedAt >= cutoff30)
            .Select(d => d.CreatedAt.Date)
            .ToListAsync(ct);
        var quizDays = await db.QuizAttempts.AsNoTracking()
            .Where(a => a.UserId == childId && a.CompletedAt >= cutoff30)
            .Select(a => a.CompletedAt.Date)
            .ToListAsync(ct);

        var activeDaysCount = studyDays.Concat(downloadDays).Concat(quizDays).Distinct().Count();

        var regularite = activeDaysCount >= 12
            ? $"{name} se connecte régulièrement, plusieurs fois par semaine."
            : activeDaysCount >= 4
                ? $"{name} se connecte de temps en temps, sans rythme fixe."
                : activeDaysCount >= 1
                    ? $"{name} a eu une activité irrégulière ce mois-ci."
                    : $"{name} n'a pas eu d'activité enregistrée ce mois-ci.";

        // ── Autonomie : matières consultées (30j) qui viennent, ou non, d'un
        // achat du parent (ParentCreditLedger  seul lien fiable Order/enfant,
        // voir ParentCreditsController.GetPurchaseImpact).
        var purchasedSubjectIds = (await (
            from ledger in db.ParentCreditLedgers
            where ledger.ChildId == childId && ledger.EntryType == "consumption" && ledger.OrderId != null
            join order in db.Orders on ledger.OrderId equals order.Id
            join item in db.OrderItems on order.Id equals item.OrderId
            select item.SubjectId
        ).Distinct().ToListAsync(ct)).ToHashSet();

        var consultedFromDownloads = await db.DownloadHistories.AsNoTracking()
            .Where(d => d.UserId == childId && d.CreatedAt >= cutoff30)
            .Select(d => d.SubjectId)
            .ToListAsync(ct);
        var consultedFromStudy = await db.StudySessions.AsNoTracking()
            .Where(s => s.UserId == childId && s.CreatedAt >= cutoff30)
            .Select(s => s.SubjectId)
            .ToListAsync(ct);
        var consultedSet = consultedFromDownloads.Concat(consultedFromStudy).Distinct().ToHashSet();

        var autonomousCount = consultedSet.Count(id => !purchasedSubjectIds.Contains(id));
        var purchasedConsultedCount = consultedSet.Count - autonomousCount;

        var autonomie = consultedSet.Count == 0
            ? $"{name} n'a pas encore consulté de contenu ce mois-ci."
            : autonomousCount > purchasedConsultedCount
                ? $"{name} explore souvent la plateforme de lui-même."
                : $"{name} utilise principalement les contenus partagés par ses parents.";

        // ── Curiosité : matières consultées (30j) en dehors du périmètre habituel
        // (inscriptions + matières des quiz passés). Goal n'a pas de champ
        // matière exploitable (Type = academic/personal/skill, pas un lien vers
        // Subjects) : volontairement écarté plutôt que déduit d'un texte libre.
        var enrolledSubjectIds = await db.Enrollments.AsNoTracking()
            .Where(e => e.UserId == childId)
            .Select(e => e.SubjectId)
            .Distinct()
            .ToListAsync(ct);

        var quizSubjectTitles = await (
            from a in db.QuizAttempts
            join q in db.Quizzes on a.QuizId equals q.Id
            where a.UserId == childId
            select q.Subject
        ).Distinct().ToListAsync(ct);
        var quizSubjectIds = await db.Subjects.AsNoTracking()
            .Where(s => quizSubjectTitles.Contains(s.Title))
            .Select(s => s.Id)
            .ToListAsync(ct);

        var habitualSet = enrolledSubjectIds.Concat(quizSubjectIds).Distinct().ToHashSet();
        var outsideHabitualCount = consultedSet.Count(id => !habitualSet.Contains(id));

        var curiosite = consultedSet.Count == 0
            ? $"{name} n'a pas exploré de nouveau contenu ce mois-ci."
            : outsideHabitualCount > 0
                ? $"{name} montre de la curiosité pour des sujets variés."
                : $"{name} reste concentré sur ses matières habituelles.";

        return new Portrait(regularite, autonomie, curiosite);
    }
}
