using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public sealed record WalletBalanceMismatch(int UserId, decimal LegacyXaf, decimal LedgerXaf);

public sealed record WalletBackfillReport(
    bool DryRun,
    int OrdersReplayed,
    int BalancePurchasesReplayed,
    int BookingsReplayed,
    int CommissionsReplayed,
    int ClassAssignmentsReplayed,
    int WithdrawalsReplayed,
    int UsersCompared,
    List<WalletBalanceMismatch> Mismatches)
{
    /// <summary>Vrai si, pour chaque utilisateur concerné, le solde du journal égale strictement l'ancien solde.</summary>
    public bool Equal => Mismatches.Count == 0;
}

/// <summary>
/// Reprise de l'historique dans le journal (Module 1, voie « écritures de
/// reprise » : chaque événement passé est rejoué en écriture, avec la même clé
/// d'idempotence que l'écriture en direct, donc sans doublon possible si la
/// reprise est relancée ou si un événement a déjà été écrit en direct).
///
/// Après rejeu, le solde de chaque utilisateur concerné est comparé à l'ancien
/// calcul. <b>L'ancien calcul n'existe plus qu'ici</b>, uniquement pour cette
/// preuve d'égalité : aucun solde de l'application n'est plus lu de cette
/// façon.
/// </summary>
public interface IWalletBackfillService
{
    /// <summary>Rejoue l'historique en écritures (idempotent) puis compare.</summary>
    Task<WalletBackfillReport> RunAsync(CancellationToken ct = default);

    /// <summary>Compare seulement, sans écrire.</summary>
    Task<WalletBackfillReport> CompareAsync(CancellationToken ct = default);
}

public class WalletBackfillService : IWalletBackfillService
{
    private readonly ApplicationDbContext _db;
    private readonly IWalletService _wallet;
    private readonly ILogger<WalletBackfillService> _logger;

    public WalletBackfillService(ApplicationDbContext db, IWalletService wallet, ILogger<WalletBackfillService> logger)
    {
        _db = db;
        _wallet = wallet;
        _logger = logger;
    }

    public async Task<WalletBackfillReport> RunAsync(CancellationToken ct = default)
    {
        // 1. Commandes payées portant un contenu d'auteur : ventes catalogue.
        var saleOrderIds = await _db.OrderItems.AsNoTracking()
            .Where(oi => oi.Subject != null && oi.Subject.AuthorUserId != null
                      && PaidOrderStatus.All.Contains(oi.Order.Status.ToLower()))
            .Select(oi => oi.OrderId).Distinct().ToListAsync(ct);
        foreach (var id in saleOrderIds) await _wallet.SyncOrderAsync(id);

        // 2. Achats réglés par solde : débit historique, sans contrôle de solde
        //    (il a déjà eu lieu), même clé que le paiement en direct.
        var balanceOrders = await _db.Orders.AsNoTracking()
            .Where(o => o.UserId != null && o.PaymentMethod == "balance" && PaidOrderStatus.All.Contains(o.Status.ToLower()))
            .Select(o => new { o.Id, UserId = o.UserId!.Value, o.TotalAmount, o.OrderNumber, o.CreatedAt })
            .ToListAsync(ct);
        foreach (var o in balanceOrders)
        {
            if (RevenueSplit.Xaf(o.TotalAmount) <= 0) continue;
            await _wallet.PostAsync(new WalletEntry(o.UserId, WalletEntryTypes.BalancePurchase, -o.TotalAmount,
                WalletService.BalancePurchaseKey(o.Id), $"Achat panier {o.OrderNumber}", "Order", o.Id,
                OccurredAt: o.CreatedAt));
        }

        // 3. Réservations de tutorat payées ou libérées.
        var bookingIds = await _db.TutorBookings.AsNoTracking()
            .Where(b => b.EscrowReleasedAt != null || b.PaymentStatus == "completed")
            .Select(b => b.Id).ToListAsync(ct);
        foreach (var id in bookingIds) await _wallet.SyncTutorBookingAsync(id);

        // 4. Commissions d'affiliation (toutes : en attente, confirmées, annulées).
        var commissionIds = await _db.AffiliateCommissions.AsNoTracking().Select(c => c.Id).ToListAsync(ct);
        foreach (var id in commissionIds) await _wallet.SyncAffiliateCommissionAsync(id);

        // 5. Contenus assignés à une classe et facturés sur le solde.
        var assignments = await _db.TeacherClassContents.AsNoTracking()
            .Where(t => t.PriceChargedXaf > 0)
            .Select(t => new { t.Id, t.AssignedByUserId, t.PriceChargedXaf, t.AssignedAt, Title = t.Subject != null ? t.Subject.Title : "Contenu" })
            .ToListAsync(ct);
        foreach (var a in assignments)
        {
            await _wallet.PostAsync(new WalletEntry(a.AssignedByUserId, WalletEntryTypes.ClassAssignment, -a.PriceChargedXaf,
                WalletService.ClassAssignmentKey(a.Id), $"Assignation classe : {a.Title}", "TeacherClassContent", a.Id,
                OccurredAt: a.AssignedAt));
        }

        // 6. Retraits manuels déjà demandés (en attente) ou versés.
        var withdrawals = await _db.Withdrawals.AsNoTracking()
            .Where(w => w.Status == "pending" || w.Status == "completed")
            .ToListAsync(ct);
        foreach (var w in withdrawals)
        {
            await _wallet.PostAsync(new WalletEntry(w.UserId, WalletEntryTypes.WithdrawalRequested, -w.AmountXaf,
                WalletService.WithdrawalRequestedKey(w.Id), WithdrawalLabels.Requested(w), "Withdrawal", w.Id,
                OccurredAt: w.RequestedAt));
            if (w.Status == "completed")
                await _wallet.PostAsync(new WalletEntry(w.UserId, WalletEntryTypes.WithdrawalProcessed, 0m,
                    WalletService.WithdrawalProcessedKey(w.Id), WithdrawalLabels.Processed(w), "Withdrawal", w.Id,
                    OccurredAt: w.ProcessedAt ?? w.RequestedAt));
        }

        var comparison = await CompareAsync(ct);
        var report = comparison with
        {
            DryRun = false,
            OrdersReplayed = saleOrderIds.Count,
            BalancePurchasesReplayed = balanceOrders.Count,
            BookingsReplayed = bookingIds.Count,
            CommissionsReplayed = commissionIds.Count,
            ClassAssignmentsReplayed = assignments.Count,
            WithdrawalsReplayed = withdrawals.Count,
        };

        if (report.Equal)
            _logger.LogInformation("Reprise du journal : {Users} soldes identiques à l'ancien calcul", report.UsersCompared);
        else
            _logger.LogError("Reprise du journal : {Count} solde(s) différent(s) de l'ancien calcul, voir le rapport", report.Mismatches.Count);

        return report;
    }

    public async Task<WalletBackfillReport> CompareAsync(CancellationToken ct = default)
    {
        var legacy = await LegacyBalancesAsync(ct);
        var ledgerOwners = await _db.WalletTransactions.AsNoTracking()
            .Where(t => t.OwnerId != null && t.OwnerType != WalletOwnerTypes.Platform)
            .Select(t => t.OwnerId!.Value).Distinct().ToListAsync(ct);

        var users = legacy.Keys.Union(ledgerOwners).OrderBy(id => id).ToList();
        var mismatches = new List<WalletBalanceMismatch>();
        foreach (var userId in users)
        {
            var ledger = await _wallet.GetAvailableAsync(userId);
            var old = legacy.TryGetValue(userId, out var value) ? value : 0m;
            if (ledger != old) mismatches.Add(new WalletBalanceMismatch(userId, old, ledger));
        }

        return new WalletBackfillReport(true, 0, 0, 0, 0, 0, 0, users.Count, mismatches);
    }

    /// <summary>
    /// Ancien calcul de TeacherService.GetSpendableBalanceAsync (avant le lot 2),
    /// conservé à l'identique pour la seule preuve d'égalité de la reprise, sans
    /// le plancher à zéro (un écart négatif doit apparaître, pas être masqué).
    /// </summary>
    private async Task<Dictionary<int, decimal>> LegacyBalancesAsync(CancellationToken ct)
    {
        var totals = new Dictionary<int, decimal>();
        void Add(int userId, decimal amount) => totals[userId] = (totals.TryGetValue(userId, out var v) ? v : 0m) + amount;

        var sales = await _db.OrderItems.AsNoTracking()
            .Where(oi => PaidOrderStatus.All.Contains(oi.Order.Status.ToLower()) && oi.Subject != null && oi.Subject.AuthorUserId != null)
            .Select(oi => new { AuthorId = oi.Subject!.AuthorUserId!.Value, oi.PriceAtPurchase })
            .ToListAsync(ct);
        foreach (var s in sales) Add(s.AuthorId, s.PriceAtPurchase);

        var assignments = await _db.TeacherClassContents.AsNoTracking()
            .Select(t => new { t.AssignedByUserId, t.PriceChargedXaf }).ToListAsync(ct);
        foreach (var a in assignments) Add(a.AssignedByUserId, -a.PriceChargedXaf);

        var balanceOrders = await _db.Orders.AsNoTracking()
            .Where(o => o.UserId != null && PaidOrderStatus.All.Contains(o.Status.ToLower()) && o.PaymentMethod == "balance")
            .Select(o => new { UserId = o.UserId!.Value, o.TotalAmount }).ToListAsync(ct);
        foreach (var o in balanceOrders) Add(o.UserId, -o.TotalAmount);

        var bookings = await _db.TutorBookings.AsNoTracking()
            .Where(b => b.EscrowReleasedAt != null && b.TutorProfile != null)
            .Select(b => new { b.TutorProfile!.UserId, b.PriceXaf }).ToListAsync(ct);
        var shares = new Dictionary<int, decimal>();
        foreach (var b in bookings)
        {
            if (!shares.TryGetValue(b.UserId, out var share))
                shares[b.UserId] = share = await RevenueSplit.GetTeacherShareAsync(_db, b.UserId);
            Add(b.UserId, RevenueSplit.NetXaf(b.PriceXaf, share));
        }

        var commissions = await _db.AffiliateCommissions.AsNoTracking()
            .Where(c => c.Status == "confirmed")
            .Select(c => new { c.AffiliateAccount!.UserId, c.CommissionAmount }).ToListAsync(ct);
        foreach (var c in commissions) Add(c.UserId, c.CommissionAmount);

        // Le journal ajoute aux anciennes sources des écritures qu'aucun ancien
        // calcul ne connaissait (recharge, crédit administrateur, retrait
        // automatisé du Module 2) : elles sont rejouées tel quel pour que la
        // comparaison porte sur les seules sources historiques.
        var newSources = await _db.WalletTransactions.AsNoTracking()
            .Where(t => t.OwnerId != null && t.OwnerType != WalletOwnerTypes.Platform && t.Status == WalletEntryStatus.Confirmed
                     && (t.EntryType == WalletEntryTypes.Recharge || t.EntryType == WalletEntryTypes.AdminCredit))
            .Select(t => new { UserId = t.OwnerId!.Value, t.Amount }).ToListAsync(ct);
        foreach (var n in newSources) Add(n.UserId, n.Amount);

        // Retraits : l'ancien calcul réservait « pending » et « completed ».
        // Les statuts du retrait automatisé (Module 2) s'y rattachent :
        // « processing » réserve encore les fonds ; « failed » et « cancelled »
        // les ont rendus.
        var withdrawals = await _db.Withdrawals.AsNoTracking()
            .Where(w => w.Status == "pending" || w.Status == "processing" || w.Status == "completed")
            .Select(w => new { w.UserId, w.AmountXaf }).ToListAsync(ct);
        foreach (var w in withdrawals) Add(w.UserId, -w.AmountXaf);

        return totals.ToDictionary(kv => kv.Key, kv => RevenueSplit.Xaf(kv.Value));
    }
}

/// <summary>Libellés lisibles des écritures de retrait, partagés par le flux en direct et la reprise.</summary>
public static class WithdrawalLabels
{
    public static string MaskPhone(string? phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? "****" : new string('*', Math.Max(0, digits.Length - 3)) + digits[^3..];
    }

    private static string Operator(string? op) => string.Equals(op, "orange", StringComparison.OrdinalIgnoreCase) ? "Orange Money" : "MTN MoMo";

    public static string Requested(Withdrawal w) => $"Retrait demandé vers {Operator(w.Operator)} {MaskPhone(w.Phone)}";
    public static string Processed(Withdrawal w) => $"Retrait versé vers {Operator(w.Operator)} {MaskPhone(w.Phone)}";
}
