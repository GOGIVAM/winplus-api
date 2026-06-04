using Microsoft.EntityFrameworkCore;
using Backend.Data;

namespace Backend.Services;

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
            await Task.Delay(Interval, stoppingToken);

            try
            {
                await ExpireSubscriptionsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'expiration des abonnements");
            }
        }
    }

    private async Task ExpireSubscriptionsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;
        var expired = await db.Subscriptions
            .Where(s => s.Status == "active" && s.EndDate != null && s.EndDate < now)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        foreach (var sub in expired)
        {
            sub.Status = "expired";
            sub.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("{Count} abonnement(s) expiré(s)", expired.Count);
    }
}
