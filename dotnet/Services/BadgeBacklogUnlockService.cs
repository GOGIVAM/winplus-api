using Microsoft.EntityFrameworkCore;
using Backend.Data;

namespace Backend.Services;

/// <summary>
/// Module 24 (lot 8, rattrapage 16.2) : rattrapage périodique des badges
/// plateforme pour les utilisateurs déjà actifs en base avant la mise en
/// service du catalogue, et filet de sécurité si un hook synchrone
/// (QuizService, CourseCertificateService) a été manqué. Même patron que
/// CourseInactivityAlertService (BackgroundService + IServiceScopeFactory,
/// boucle avec délai), pas de nouveau mécanisme introduit.
///
/// Tourne toutes les 6 heures (plus fréquent que l'alerte d'inactivité
/// quotidienne : un déblocage de badge en retard est plus visible pour
/// l'utilisateur qu'une alerte professeur).
/// </summary>
public sealed class BadgeBacklogUnlockService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BadgeBacklogUnlockService> _logger;

    public BadgeBacklogUnlockService(IServiceScopeFactory scopeFactory, ILogger<BadgeBacklogUnlockService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BadgeBacklogUnlockService started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "BadgeBacklogUnlockService run failed."); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var badges = scope.ServiceProvider.GetRequiredService<IBadgeService>();

        // Univers borné : utilisateurs ayant au moins une tentative de quiz ou
        // un certificat (les seuls déclencheurs synchrones) + ceux actifs
        // récemment (pour le critère de série, qui n'a pas de hook synchrone).
        var recentCutoff = DateTime.UtcNow.AddDays(-35);
        var userIds = await db.QuizAttempts.AsNoTracking().Select(a => a.UserId)
            .Union(db.CourseCertificates.AsNoTracking().Select(c => c.UserId))
            .Union(db.StudySessions.AsNoTracking().Where(s => s.CreatedAt >= recentCutoff).Select(s => s.UserId))
            .Union(db.DownloadHistories.AsNoTracking().Where(d => d.CreatedAt >= recentCutoff).Select(d => d.UserId))
            .Distinct()
            .ToListAsync(ct);

        foreach (var userId in userIds)
        {
            if (ct.IsCancellationRequested) break;
            await badges.EvaluateForUserAsync(userId);
        }
    }
}
