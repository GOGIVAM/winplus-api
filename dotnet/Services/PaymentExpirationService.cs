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
        var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();

        var expiryThreshold = DateTime.UtcNow.Subtract(PendingThreshold);
        var stalePayments = await repository.GetExpiredPendingPaymentsAsync(expiryThreshold);

        if (stalePayments.Count == 0) return;

        foreach (var payment in stalePayments)
        {
            if (ct.IsCancellationRequested) break;

            payment.Status = "expired";
            payment.ErrorMessage = "Paiement expiré après 1 heure sans confirmation";
            await repository.UpdateAsync(payment);

            // Module 19 : ce service laissait la commande en attente à vie.
            // Une commande dont le paiement a expiré passe à l'état final
            // « échoué » ; la relance du paiement la ramènera en attente si
            // l'utilisateur reprend son achat (PaymentService.RetryPaymentAsync).
            try
            {
                // Décision 10.9 : le paiement reste marqué expiré ci-dessus,
                // mais une commande protégée (demande de remboursement, déjà
                // réglée) ne doit pas repasser en « échoué », état annulable.
                var order = await orderService.GetOrderByIdAsync(payment.OrderId);
                if (order != null && OrderStatusRules.BlocksAutomaticTransition(order.Status, "failed"))
                {
                    _logger.LogWarning(
                        "Commande {OrderId} en statut protégé {CurrentStatus} : passage en échoué après expiration " +
                        "du paiement {PaymentId} ignoré (décision 10.9).",
                        payment.OrderId, order.Status, payment.Id);
                    continue;
                }

                await orderService.UpdateOrderStatusAsync(payment.OrderId, "failed");

                // Lot 2 : complément Mobile Money expiré d'un paiement combiné,
                // ou recharge expirée : part solde restituée, recharge close.
                await scope.ServiceProvider.GetRequiredService<IWalletService>().SyncOrderAsync(payment.OrderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Impossible de marquer la commande {OrderId} comme échouée après expiration du paiement {PaymentId}",
                    payment.OrderId, payment.Id);
            }
        }

        _logger.LogInformation("{Count} paiement(s) expiré(s) après dépassement du délai d'1 heure", stalePayments.Count);
    }
}
