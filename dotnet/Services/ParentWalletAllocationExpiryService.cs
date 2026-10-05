using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.Entities;

namespace Backend.Services;

/// <summary>
/// Contre-passe le reliquat non consommé d'une dotation mensuelle de
/// portefeuille parent (Module 14) exactement à son expiration, par une
/// écriture <see cref="WalletEntryTypes.AllocationExpired"/> explicite,
/// idempotente (une seule par dotation). Sur le modèle de
/// <see cref="SubscriptionExpirationService"/>.
///
/// Ce service est la seule raison pour laquelle <see cref="IWalletService"/>
/// n'a pas besoin de filtrer les écritures par <c>ExpiresAt</c> à la lecture
/// du solde : l'expiration est matérialisée en une écriture réelle au bon
/// moment, pour exactement le reliquat encore disponible — jamais pour la
/// part déjà dépensée avant l'expiration.
/// </summary>
public class ParentWalletAllocationExpiryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ParentWalletAllocationExpiryService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    public ParentWalletAllocationExpiryService(IServiceScopeFactory scopeFactory, ILogger<ParentWalletAllocationExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ParentWalletAllocationExpiryService démarré (intervalle : 30 minutes)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireAllocationsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'expiration des dotations de portefeuille parent");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ExpireAllocationsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var wallet = scope.ServiceProvider.GetRequiredService<IWalletService>();

        var now = DateTime.UtcNow;

        // Dotations confirmées, dont l'échéance est dépassée, et qui n'ont pas
        // déjà reçu leur écriture d'expiration.
        var dueAllocations = await db.WalletTransactions.AsNoTracking()
            .Where(t => t.OwnerType == WalletOwnerTypes.Parent
                     && t.EntryType == WalletEntryTypes.MonthlyAllocation
                     && t.Status == WalletEntryStatus.Confirmed
                     && t.ExpiresAt != null && t.ExpiresAt <= now
                     && !db.WalletTransactions.Any(e => e.SourceType == "WalletTransaction" && e.SourceId == t.Id
                                                      && e.EntryType == WalletEntryTypes.AllocationExpired))
            .ToListAsync(ct);

        if (dueAllocations.Count == 0) return;

        var expiredCount = 0;
        foreach (var allocation in dueAllocations)
        {
            ct.ThrowIfCancellationRequested();

            var consumed = await db.WalletTransactions.AsNoTracking()
                .Where(t => t.SourceType == "WalletTransaction" && t.SourceId == allocation.Id
                         && t.EntryType == WalletEntryTypes.AllocationConsumption
                         && t.Status == WalletEntryStatus.Confirmed)
                .SumAsync(t => (decimal?)-t.Amount, ct) ?? 0m;

            var remaining = Math.Max(0, allocation.Amount - consumed);
            if (remaining <= 0)
            {
                // Rien à contre-passer : poser quand même un marqueur à montant
                // nul serait rejeté par PostAsync (montant non nul exigé pour
                // une écriture autre que WithdrawalProcessed). Un reliquat nul
                // n'a pas besoin d'être tracé : AllocationRemainingAsync
                // retombe déjà sur 0 sans marqueur.
                continue;
            }

            var ownerId = allocation.OwnerId!.Value;
            await wallet.PostAsync(new WalletEntry(ownerId, WalletEntryTypes.AllocationExpired, -remaining,
                WalletService.ParentAllocationExpiryKey(allocation.Id),
                "Dotation mensuelle expirée (reliquat non utilisé)",
                "WalletTransaction", checked((int)allocation.Id), OwnerType: WalletOwnerTypes.Parent));
            expiredCount++;
        }

        if (expiredCount > 0)
            _logger.LogInformation("{Count} dotation(s) de portefeuille parent expirée(s) (reliquat contre-passé)", expiredCount);
    }
}
