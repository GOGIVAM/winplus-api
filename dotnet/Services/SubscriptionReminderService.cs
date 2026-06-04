using Microsoft.EntityFrameworkCore;
using Backend.Data;

namespace Backend.Services;

public class SubscriptionReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionReminderService> _logger;

    public SubscriptionReminderService(IServiceScopeFactory scopeFactory, ILogger<SubscriptionReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SubscriptionReminderService démarré (quotidien à 9h UTC)");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun();
            await Task.Delay(delay, stoppingToken);

            try
            {
                await SendExpiryRemindersAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'envoi des rappels d'expiration");
            }
        }
    }

    private async Task SendExpiryRemindersAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(3);

        var subscriptions = await db.Subscriptions
            .Include(s => s.User)
            .Where(s => s.Status == "active"
                     && s.EndDate != null
                     && s.EndDate > now
                     && s.EndDate <= cutoff)
            .ToListAsync(ct);

        if (subscriptions.Count == 0) return;

        foreach (var sub in subscriptions)
        {
            if (ct.IsCancellationRequested) break;
            if (sub.User == null) continue;

            var firstName = sub.User.FirstName ?? sub.User.Email;
            await emailService.SendSubscriptionExpiryReminderAsync(
                sub.User.Email,
                firstName ?? "",
                sub.EndDate!.Value);
        }

        _logger.LogInformation("{Count} rappel(s) d'expiration envoyé(s)", subscriptions.Count);
    }

    private static TimeSpan TimeUntilNextRun()
    {
        var now = DateTime.UtcNow;
        var next = now.Date.AddHours(9);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
