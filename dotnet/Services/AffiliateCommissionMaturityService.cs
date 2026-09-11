using Microsoft.EntityFrameworkCore;
using Backend.Data;

namespace Backend.Services;

/// <summary>
/// Fait mûrir quotidiennement les commissions d'affiliation : "pending" →
/// "confirmed" une fois AffiliateSettings.HoldPeriodDays écoulés depuis la
/// commande (si celle-ci est toujours "completed"), ou → "reversed" si la
/// commande a entre-temps été remboursée/annulée. Même mécanique que
/// PaymentExpirationService (boucle à intervalle fixe, scope DI par run).
/// </summary>
public sealed class AffiliateCommissionMaturityService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AffiliateCommissionMaturityService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    public AffiliateCommissionMaturityService(IServiceScopeFactory scopeFactory, ILogger<AffiliateCommissionMaturityService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AffiliateCommissionMaturityService démarré (intervalle: 6 heures)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await MatureCommissionsAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Erreur lors de la maturation des commissions d'affiliation"); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task MatureCommissionsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var settings = await db.AffiliateSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);
        var holdDays = settings?.HoldPeriodDays ?? 14;
        var cutoff = DateTime.UtcNow.AddDays(-holdDays);

        var pending = await db.AffiliateCommissions
            .Include(c => c.Order).ThenInclude(o => o.Payments)
            .Where(c => c.Status == "pending" && c.CreatedAt <= cutoff)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        int confirmed = 0, reversed = 0;
        foreach (var commission in pending)
        {
            // Order.Status ne passe jamais à "refunded" dans ce codebase (seul
            // Payment.Status le fait, voir PaymentService.RefundPaymentAsync) —
            // il faut donc regarder les paiements liés, pas seulement la commande.
            var wasRefundedOrFailed = commission.Order.Payments.Any(p => p.Status is "refunded" or "failed");
            if (wasRefundedOrFailed || commission.Order.Status is "cancelled" or "failed")
            {
                commission.Status = "reversed";
                reversed++;
            }
            else
            {
                commission.Status = "confirmed";
                commission.ConfirmedAt = DateTime.UtcNow;
                confirmed++;
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Maturation des commissions d'affiliation : {Confirmed} confirmée(s), {Reversed} annulée(s)", confirmed, reversed);
    }
}
