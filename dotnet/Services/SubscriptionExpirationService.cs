using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Services.Entitlements;

namespace Backend.Services;

/// <summary>
/// Passe horaire du cycle d'abonnement. Lot 9.7b : elle ne se contente plus
/// d'expirer à l'échéance ; elle délègue à
/// <see cref="SubscriptionLifecycleService.RenewDueAsync"/> le renouvellement
/// automatique par le solde (18.15), la grâce de 3 jours, la coupure, la fin
/// des essais et des abonnements résiliés, avec notification à chaque étape.
/// </summary>
public class SubscriptionExpirationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionExpirationService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public SubscriptionExpirationService(IServiceScopeFactory scopeFactory, ILogger<SubscriptionExpirationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SubscriptionExpirationService démarré (intervalle: 1 heure)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { break; }
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var lifecycle = scope.ServiceProvider.GetRequiredService<SubscriptionLifecycleService>();
                var report = await lifecycle.RenewDueAsync(DateTime.UtcNow, stoppingToken);
                if (report.Renewed + report.InGrace + report.Expired > 0)
                    _logger.LogInformation("Abonnements : {Renewed} renouvelé(s), {Grace} en grâce, {Expired} terminé(s)",
                        report.Renewed, report.InGrace, report.Expired);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du cycle des abonnements (renouvellement, grâce, expiration)");
            }
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
