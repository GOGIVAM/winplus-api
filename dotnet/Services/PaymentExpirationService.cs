using Backend.Repositories;

namespace Backend.Services;

public class PaymentExpirationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentExpirationService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan PendingThreshold = TimeSpan.FromHours(1);

    public PaymentExpirationService(IServiceScopeFactory scopeFactory, ILogger<PaymentExpirationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PaymentExpirationService démarré (intervalle: 1 heure)");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);

            try
            {
                await ExpireStalePaymentsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'expiration des paiements");
            }
        }
    }

    private async Task ExpireStalePaymentsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();

        var expiryThreshold = DateTime.UtcNow.Subtract(PendingThreshold);
        var stalePayments = await repository.GetExpiredPendingPaymentsAsync(expiryThreshold);

        if (stalePayments.Count == 0) return;

        foreach (var payment in stalePayments)
        {
            if (ct.IsCancellationRequested) break;

            payment.Status = "expired";
            payment.ErrorMessage = "Paiement expiré après 1 heure sans confirmation";
            await repository.UpdateAsync(payment);
        }

        _logger.LogInformation("{Count} paiement(s) expiré(s) après dépassement du délai d'1 heure", stalePayments.Count);
    }
}
