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

        // Premier passage dès le démarrage pour les commandes déjà bloquées.
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); await CloseStaleOrdersAsync(stoppingToken); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { _logger.LogError(ex, "Erreur lors de la clôture des commandes en attente"); }

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);

            try
            {
                await ExpireStalePaymentsAsync(stoppingToken);
                await CloseStaleOrdersAsync(stoppingToken);
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

    /// <summary>Délai après lequel une commande sans aucun paiement est considérée comme abandonnée.</summary>
    private static readonly TimeSpan AbandonedOrderThreshold = TimeSpan.FromHours(24);

    /// <summary>
    /// Commandes restées « en attente » sans paiement en cours : elles gonflaient
    /// sans fin le compteur « commandes en attente » de l'admin (28 commandes
    /// mortes relevées le 2026-10-21, dont des paniers abandonnés depuis février).
    ///   * dernier paiement échoué ou expiré : la commande passe en « échoué »
    ///     (même règle que l'expiration ci-dessus ; une relance la remet en attente) ;
    ///   * aucun paiement depuis 24 h : panier abandonné, la commande passe en « annulé ».
    /// Une commande protégée (remboursement, déjà réglée) n'est jamais touchée.
    /// </summary>
    private async Task CloseStaleOrdersAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Backend.Data.ApplicationDbContext>();
        var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
        var wallet = scope.ServiceProvider.GetRequiredService<IWalletService>();
        var abandonedBefore = DateTime.UtcNow.Subtract(AbandonedOrderThreshold);

        var candidates = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.Orders
                .Where(o => !o.IsDeleted && o.Status.ToLower() == "pending")
                .Select(o => new
                {
                    o.Id,
                    o.CreatedAt,
                    LastPaymentStatus = o.Payments.OrderByDescending(p => p.InitiatedAt).Select(p => p.Status).FirstOrDefault()
                }), ct);

        var closed = 0;
        foreach (var o in candidates)
        {
            if (ct.IsCancellationRequested) break;
            var last = o.LastPaymentStatus?.ToLowerInvariant();
            string? target = last is "failed" or "expired" or "cancelled" or "canceled"
                ? "failed"
                : last == null && o.CreatedAt < abandonedBefore ? "cancelled" : null;
            if (target == null) continue;

            try
            {
                var order = await orderService.GetOrderByIdAsync(o.Id);
                if (order == null || OrderStatusRules.BlocksAutomaticTransition(order.Status, target)) continue;
                await orderService.UpdateOrderStatusAsync(o.Id, target);
                if (target == "failed") await wallet.SyncOrderAsync(o.Id);
                closed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Impossible de clore la commande en attente {OrderId}", o.Id);
            }
        }

        if (closed > 0)
            _logger.LogInformation("{Count} commande(s) en attente sans paiement en cours close(s)", closed);
    }
}