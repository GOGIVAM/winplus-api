using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Filet de sécurité du journal de portefeuille (Module 1).
///
/// Les écritures sont posées en direct après chaque transition métier ; si
/// l'une d'elles est perdue (processus arrêté entre la validation de la
/// commande et l'écriture, exception avalée), ce service rejoue les
/// traductions idempotentes des événements récents. Il ne crée jamais de
/// débit : un débit n'existe que dans l'opération qui le contrôle.
/// Même mécanique que les autres services périodiques (scope DI par passage).
/// </summary>
public sealed class WalletReconciliationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RecentWindow = TimeSpan.FromDays(3);

    /// <summary>Une commission reste « pending » pendant la maturation (14 jours par défaut).</summary>
    private static readonly TimeSpan CommissionWindow = TimeSpan.FromDays(45);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WalletReconciliationService> _logger;

    public WalletReconciliationService(IServiceScopeFactory scopeFactory, ILogger<WalletReconciliationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ReconcileAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Erreur lors de la réconciliation du journal de portefeuille"); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var wallet = scope.ServiceProvider.GetRequiredService<IWalletService>();

        var since = DateTime.UtcNow - RecentWindow;

        var orderIds = await db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= since || (o.UpdatedAt != null && o.UpdatedAt >= since) || (o.CompletedDate != null && o.CompletedDate >= since))
            .Select(o => o.Id).ToListAsync(ct);
        foreach (var id in orderIds) await wallet.SyncOrderAsync(id);

        var bookingIds = await db.TutorBookings.AsNoTracking()
            .Where(b => b.UpdatedAt >= since || (b.EscrowReleasedAt != null && b.EscrowReleasedAt >= since))
            .Select(b => b.Id).ToListAsync(ct);
        foreach (var id in bookingIds) await wallet.SyncTutorBookingAsync(id);

        var commissionSince = DateTime.UtcNow - CommissionWindow;
        var commissionIds = await db.AffiliateCommissions.AsNoTracking()
            .Where(c => c.CreatedAt >= commissionSince || (c.ConfirmedAt != null && c.ConfirmedAt >= since))
            .Select(c => c.Id).ToListAsync(ct);
        foreach (var id in commissionIds) await wallet.SyncAffiliateCommissionAsync(id);
    }
}
