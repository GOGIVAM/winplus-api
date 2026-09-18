using Microsoft.EntityFrameworkCore;
using Backend.Data;

namespace Backend.Services;

/// <summary>
/// Désactivation automatique du mode veille d'examen (ExamCoachPlan.
/// ParentWatchModeActivatedAt) une fois la date d'examen dépassée  le parent
/// peut aussi désactiver manuellement avant (ExamCoachController.DeactivateWatchMode).
/// </summary>
public class ExamWatchModeExpirationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExamWatchModeExpirationService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public ExamWatchModeExpirationService(IServiceScopeFactory scopeFactory, ILogger<ExamWatchModeExpirationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExamWatchModeExpirationService démarré (intervalle: 1 heure)");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);

            try
            {
                await ExpireWatchModesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'expiration des veilles d'examen");
            }
        }
    }

    private async Task ExpireWatchModesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;
        var count = await db.ExamCoachPlans
            .Where(p => p.ParentWatchModeActivatedAt != null && p.ExamDate < now)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ParentWatchModeActivatedAt, (DateTime?)null), ct);

        if (count > 0)
            _logger.LogInformation("{Count} veille(s) d'examen désactivée(s) automatiquement (date d'examen dépassée)", count);
    }
}
