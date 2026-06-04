using Backend.Data;
using Backend.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public class PaymentReconciliationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentReconciliationService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    public PaymentReconciliationService(IServiceScopeFactory scopeFactory, ILogger<PaymentReconciliationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PaymentReconciliationService démarré (intervalle: 15 min)");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);

            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la réconciliation des paiements");
            }
        }
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var notchPay = scope.ServiceProvider.GetRequiredService<INotchPayService>();

        var pendingPayments = await repository.GetPendingPaymentsAsync();
        var reconciled = 0;

        foreach (var payment in pendingPayments)
        {
            if (ct.IsCancellationRequested) break;
            if (string.IsNullOrEmpty(payment.NotchpayReference)) continue;

            try
            {
                var tx = await notchPay.GetTransactionStatusAsync(payment.NotchpayReference);
                var newStatus = tx.Status switch
                {
                    "complete" or "completed" or "success" => "completed",
                    "failed" or "failure" or "canceled" => "failed",
                    "expired" => "expired",
                    _ => null
                };

                if (newStatus != null && newStatus != payment.Status)
                {
                    payment.Status = newStatus;
                    payment.Operator = tx.Operator ?? payment.Operator;
                    if (newStatus == "completed") payment.CompletedAt = DateTime.UtcNow;
                    if (newStatus == "failed")
                    {
                        payment.ErrorCode = tx.FailureCode;
                        payment.ErrorMessage = tx.FailureMessage;
                    }
                    await repository.UpdateAsync(payment);
                    reconciled++;
                    _logger.LogInformation("Paiement {Id} réconcilié: {OldStatus} → {NewStatus}", payment.Id, "pending", newStatus);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Impossible de réconcilier le paiement {Id} (ref: {Ref})", payment.Id, payment.NotchpayReference);
            }
        }

        if (reconciled > 0)
            _logger.LogInformation("Réconciliation terminée: {Count} paiement(s) mis à jour", reconciled);
    }
}
