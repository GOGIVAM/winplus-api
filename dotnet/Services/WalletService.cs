using System.Collections.Concurrent;
using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>Décomposition du solde d'un portefeuille (Module 1 et 3).</summary>
/// <param name="AvailableXaf">Somme des écritures confirmées : ce qui peut être retiré ou dépensé.</param>
/// <param name="PendingXaf">Somme des écritures en attente (escrow non libéré, commission non mûrie).</param>
/// <param name="EngagedXaf">Montant réservé par des retraits en cours, déjà déduit de <paramref name="AvailableXaf"/>.</param>
/// <param name="IsAnomaly">Vrai si le solde disponible est négatif : anomalie signalée, jamais un solde normal.</param>
public sealed record WalletBalance(decimal AvailableXaf, decimal PendingXaf, decimal EngagedXaf, bool IsAnomaly);

/// <summary>Débit refusé faute de solde disponible suffisant.</summary>
public sealed class InsufficientWalletBalanceException : InvalidOperationException
{
    public decimal AvailableXaf { get; }
    public decimal RequiredXaf { get; }

    public InsufficientWalletBalanceException(decimal available, decimal required)
        : base($"Solde insuffisant : {available:0} XAF disponibles, {required:0} XAF requis.")
    {
        AvailableXaf = available;
        RequiredXaf = required;
    }
}

/// <summary>
/// Journal de portefeuille (Module 1, décision §14 du suivi).
///
/// Seul point d'écriture de <see cref="WalletTransaction"/>. Toute écriture
/// passe par une clé d'idempotence ; tout débit passe par
/// <see cref="RunLockedAsync{T}"/>, qui sérialise les opérations d'un même
/// propriétaire dans une transaction (verrou consultatif PostgreSQL), de sorte
/// que deux dépenses concurrentes ne puissent jamais engager deux fois le même
/// solde.
///
/// Les méthodes <c>Sync…Async</c> traduisent l'état d'un événement métier
/// (commande, réservation, commission) en écritures. Elles sont idempotentes :
/// appelées après chaque transition, et rejouées par la reprise et par la
/// réconciliation périodique, elles ne créent jamais de doublon.
/// </summary>
public interface IWalletService
{
    Task<WalletBalance> GetBalanceAsync(int userId);

    /// <summary>Somme des écritures confirmées du propriétaire (peut être négative : anomalie).</summary>
    Task<decimal> GetAvailableAsync(int userId);

    /// <summary>
    /// Exécute <paramref name="action"/> sous le verrou du portefeuille de
    /// <paramref name="userId"/>, dans une transaction (ouverte ici si
    /// l'appelant n'en a pas déjà une, et alors validée ici). Toute lecture de
    /// solde suivie d'un débit doit s'y trouver.
    /// </summary>
    Task<T> RunLockedAsync<T>(int userId, Func<Task<T>> action);

    /// <summary>
    /// Crédit ou écriture en attente, sans contrôle de solde. Idempotent : si
    /// la clé existe déjà, l'écriture existante est renvoyée.
    /// </summary>
    Task<WalletTransaction> PostAsync(WalletEntry entry);

    /// <summary>
    /// Débit contrôlé : à appeler depuis <see cref="RunLockedAsync{T}"/>. Lève
    /// <see cref="InsufficientWalletBalanceException"/> si le solde disponible
    /// ne couvre pas le montant. Idempotent sur la clé.
    /// </summary>
    Task<WalletTransaction> DebitAsync(WalletEntry entry);

    /// <summary>
    /// Contre-passe une écriture confirmée (écriture inverse, jamais une
    /// suppression). Idempotent : une seule contre-passation par écriture.
    /// Renvoie null si l'écriture n'existe pas ou n'est pas confirmée.
    /// </summary>
    Task<WalletTransaction?> ReverseAsync(long entryId, string description, int? createdByUserId = null);

    Task<WalletTransaction?> FindByKeyAsync(string idempotencyKey);

    /// <summary>
    /// Historique des écritures du propriétaire, filtrable par source
    /// (catalogue, cours_particulier, affiliation, recharge, credit_admin,
    /// achat, retrait), du plus récent au plus ancien. Les écritures de
    /// traitement de retrait (montant nul) sont incluses : elles marquent le
    /// virement effectif.
    /// </summary>
    Task<(List<WalletHistoryItem> Items, int Total)> GetHistoryAsync(int userId, string? source, int page, int pageSize);

    Task SyncOrderAsync(int orderId);
    Task SyncTutorBookingAsync(int bookingId);
    Task SyncAffiliateCommissionAsync(int commissionId);

    /// <summary>
    /// Module 18/30 : revenu d'une inscription à une session payante, à la
    /// même commission que le tutorat (§6.6 du suivi). Contrairement au
    /// tutorat, aucun escrow : confirmé dès l'inscription payée (pas
    /// d'infrastructure de libération différée pour les sessions aujourd'hui),
    /// contre-passé si l'inscription est ensuite remboursée.
    /// </summary>
    Task SyncSessionEnrollmentAsync(int sessionEnrollmentId);

    // ── Portefeuille parent (Module 14) ────────────────────────────────

    /// <summary>
    /// Crée la dotation mensuelle du parent si elle n'existe pas encore pour
    /// cette période (idempotent par <paramref name="periodStart"/>). Sans
    /// effet si une dotation existe déjà pour cette période, quel que soit le
    /// montant du plan au moment de l'appel (pas de recalcul au prorata).
    /// </summary>
    Task PostParentMonthlyAllocationAsync(int parentId, decimal amount, DateTime periodStart, DateTime expiresAt, string planName);

    /// <summary>
    /// Reliquat non expiré de la dotation mensuelle la plus récente du parent
    /// (0 si aucune, ou si entièrement consommée ou expirée), sa date
    /// d'expiration, et le total de la part permanente (recharges, net des
    /// achats qui n'ont pas pu être couverts par la dotation).
    /// </summary>
    Task<ParentWalletBreakdown> GetParentBreakdownAsync(int parentId);

    /// <summary>
    /// Débite le portefeuille parent pour un achat, en consommant d'abord le
    /// reliquat de dotation mensuelle encore valide, puis la part permanente
    /// (recharges). Lève <see cref="InsufficientWalletBalanceException"/> si
    /// le total des deux ne couvre pas <paramref name="amount"/>. À appeler
    /// sous <see cref="RunLockedAsync{T}"/>. Idempotent sur
    /// <paramref name="idempotencyKeyBase"/> (jusqu'à deux écritures, une par
    /// source consommée, dérivées de cette clé).
    /// </summary>
    Task DebitParentWalletAsync(int parentId, decimal amount, string idempotencyKeyBase, string description, string sourceType, int sourceId);
}

/// <summary>Décomposition du portefeuille parent (Module 14) : dotation expirable + recharges permanentes.</summary>
public sealed record ParentWalletBreakdown(decimal AllocationRemainingXaf, DateTime? AllocationExpiresAt, decimal AvailableXaf)
{
    /// <summary>Part permanente disponible (total disponible moins le reliquat de dotation).</summary>
    public decimal PermanentXaf => Math.Max(0, AvailableXaf - AllocationRemainingXaf);
}

/// <summary>Ligne d'historique de portefeuille, reliée à son événement d'origine par SourceType/SourceId.</summary>
public sealed record WalletHistoryItem(
    long Id,
    DateTime Date,
    string EntryType,
    string Source,
    string Type,
    string Label,
    decimal AmountXaf,
    decimal GrossAmountXaf,
    decimal CommissionXaf,
    string Status,
    string? SourceType,
    int? SourceId);

/// <summary>Correspondance entre les sources affichées et les types d'écriture.</summary>
public static class WalletSources
{
    public const string Catalogue = "catalogue";
    public const string Tutoring = "cours_particulier";
    public const string Affiliate = "affiliation";
    public const string Recharge = "recharge";
    public const string AdminCredit = "credit_admin";
    public const string Purchase = "achat";
    public const string Withdrawal = "retrait";
    /// <summary>Dotation mensuelle et son expiration (portefeuille parent, Module 14).</summary>
    public const string MonthlyAllocation = "dotation_mensuelle";
    public const string Other = "autre";

    public static readonly IReadOnlyDictionary<string, string[]> EntryTypesBySource = new Dictionary<string, string[]>
    {
        [Catalogue] = new[] { WalletEntryTypes.CatalogSale },
        [Tutoring] = new[] { WalletEntryTypes.TutoringRevenue },
        [Affiliate] = new[] { WalletEntryTypes.AffiliateCommission },
        [Recharge] = new[] { WalletEntryTypes.Recharge },
        [AdminCredit] = new[] { WalletEntryTypes.AdminCredit },
        [Purchase] = new[] { WalletEntryTypes.BalancePurchase, WalletEntryTypes.ClassAssignment, WalletEntryTypes.AllocationConsumption, WalletEntryTypes.ParentWalletPurchase, WalletEntryTypes.ContentRemovalRefund },
        [Withdrawal] = new[] { WalletEntryTypes.WithdrawalRequested, WalletEntryTypes.WithdrawalProcessed },
        [MonthlyAllocation] = new[] { WalletEntryTypes.MonthlyAllocation, WalletEntryTypes.AllocationExpired },
    };

    public static string ForEntryType(string entryType) =>
        EntryTypesBySource.FirstOrDefault(kv => kv.Value.Contains(entryType)).Key ?? Other;
}

/// <summary>Demande d'écriture. <see cref="Amount"/> est signé ; il est arrondi au XAF.</summary>
public sealed record WalletEntry(
    int? OwnerId,
    string EntryType,
    decimal Amount,
    string IdempotencyKey,
    string Description,
    string? SourceType = null,
    int? SourceId = null,
    string Status = WalletEntryStatus.Confirmed,
    DateTime? OccurredAt = null,
    int? CreatedByUserId = null,
    string? OwnerType = null);

public class WalletService : IWalletService
{
    /// <summary>Préfixe du verrou consultatif PostgreSQL (« WALL »), combiné à l'identifiant du propriétaire.</summary>
    private const long AdvisoryLockNamespace = 0x5741_4C4CL << 32;

    /// <summary>Repli hors PostgreSQL (fournisseur InMemory des tests) : verrou par propriétaire, propre au processus.</summary>
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> LocalLocks = new();

    private readonly ApplicationDbContext _db;
    private readonly ILogger<WalletService> _logger;

    public WalletService(ApplicationDbContext db, ILogger<WalletService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Lecture ─────────────────────────────────────────────────────────

    private IQueryable<WalletTransaction> OwnedBy(int userId) =>
        _db.WalletTransactions.AsNoTracking()
            .Where(t => t.OwnerId == userId && t.OwnerType != WalletOwnerTypes.Platform);

    public async Task<decimal> GetAvailableAsync(int userId) =>
        await OwnedBy(userId)
            .Where(t => t.Status == WalletEntryStatus.Confirmed)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

    public async Task<WalletBalance> GetBalanceAsync(int userId)
    {
        var available = await GetAvailableAsync(userId);

        var pending = await OwnedBy(userId)
            .Where(t => t.Status == WalletEntryStatus.Pending)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

        // Retrait en cours : demande débitée (réservation) sans écriture de
        // traitement ni contre-passation. Lu dans le journal seul.
        var all = _db.WalletTransactions.AsNoTracking();
        var engaged = await OwnedBy(userId)
            .Where(t => t.EntryType == WalletEntryTypes.WithdrawalRequested
                     && t.Status == WalletEntryStatus.Confirmed
                     && !all.Any(p => p.EntryType == WalletEntryTypes.WithdrawalProcessed
                                   && p.SourceType == t.SourceType && p.SourceId == t.SourceId)
                     && !all.Any(r => r.ReversesEntryId == t.Id))
            .SumAsync(t => (decimal?)-t.Amount) ?? 0m;

        var anomaly = available < 0;
        if (anomaly)
            _logger.LogError("Anomalie de portefeuille : solde disponible négatif ({Available} XAF) pour l'utilisateur {UserId}", available, userId);

        return new WalletBalance(available, pending, engaged, anomaly);
    }

    public Task<WalletTransaction?> FindByKeyAsync(string idempotencyKey) =>
        _db.WalletTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey);

    public async Task<(List<WalletHistoryItem> Items, int Total)> GetHistoryAsync(int userId, string? source, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var all = _db.WalletTransactions.AsNoTracking();
        var query = OwnedBy(userId);

        if (!string.IsNullOrWhiteSpace(source) && source != "all")
        {
            if (!WalletSources.EntryTypesBySource.TryGetValue(source, out var types))
                return (new List<WalletHistoryItem>(), 0);
            // Une contre-passation suit la source de l'écriture qu'elle annule.
            query = query.Where(t => types.Contains(t.EntryType)
                || (t.EntryType == WalletEntryTypes.Reversal
                    && all.Any(o => o.Id == t.ReversesEntryId && types.Contains(o.EntryType))));
        }

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(t => t.OccurredAt).ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        // Type d'origine des contre-passations, et prix brut des séances de
        // tutorat (le journal porte le net ; le brut et la commission sont
        // affichés comme avant).
        var reversedIds = rows.Where(r => r.ReversesEntryId != null).Select(r => r.ReversesEntryId!.Value).Distinct().ToList();
        var reversedTypes = reversedIds.Count == 0
            ? new Dictionary<long, string>()
            : await all.Where(o => reversedIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.EntryType);

        var bookingIds = rows.Where(r => r.EntryType == WalletEntryTypes.TutoringRevenue && r.SourceId != null)
            .Select(r => r.SourceId!.Value).Distinct().ToList();
        var bookingPrices = bookingIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await _db.TutorBookings.AsNoTracking().Where(b => bookingIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.PriceXaf);

        var items = rows.Select(r =>
        {
            var originType = r.EntryType == WalletEntryTypes.Reversal && r.ReversesEntryId != null
                && reversedTypes.TryGetValue(r.ReversesEntryId.Value, out var t) ? t : r.EntryType;
            var gross = Math.Abs(r.Amount);
            var commission = 0m;
            if (r.EntryType == WalletEntryTypes.TutoringRevenue && r.SourceId != null
                && bookingPrices.TryGetValue(r.SourceId.Value, out var price))
            {
                gross = price;
                commission = price - r.Amount;
            }
            var status = r.Status switch
            {
                WalletEntryStatus.Confirmed => "completed",
                WalletEntryStatus.Pending => "pending",
                _ => "reversed",
            };
            return new WalletHistoryItem(r.Id, r.OccurredAt, r.EntryType, WalletSources.ForEntryType(originType),
                r.Amount < 0 ? "debit" : "credit", r.Description, r.Amount, gross, commission, status,
                r.SourceType, r.SourceId);
        }).ToList();

        return (items, total);
    }

    // ── Verrou ──────────────────────────────────────────────────────────

    public async Task<T> RunLockedAsync<T>(int userId, Func<Task<T>> action)
    {
        if (!_db.Database.IsRelational())
        {
            var gate = LocalLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try { return await action(); }
            finally { gate.Release(); }
        }

        var ownTransaction = _db.Database.CurrentTransaction == null;
        await using var tx = ownTransaction ? await _db.Database.BeginTransactionAsync() : null;

        // Verrou de transaction : relâché au COMMIT/ROLLBACK, jamais oublié.
        await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", AdvisoryLockNamespace | (uint)userId);

        var result = await action();
        if (tx != null) await tx.CommitAsync();
        return result;
    }

    // ── Écriture ────────────────────────────────────────────────────────

    public async Task<WalletTransaction> PostAsync(WalletEntry entry)
    {
        var existing = await FindByKeyAsync(entry.IdempotencyKey);
        if (existing != null) return existing;

        var row = await BuildAsync(entry);
        _db.WalletTransactions.Add(row);
        try
        {
            await _db.SaveChangesAsync();
            return row;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Rejeu concurrent du même événement : l'autre écriture a gagné.
            _db.Entry(row).State = EntityState.Detached;
            return await FindByKeyAsync(entry.IdempotencyKey)
                ?? throw new InvalidOperationException($"Écriture {entry.IdempotencyKey} introuvable après conflit d'unicité.", ex);
        }
    }

    public async Task<WalletTransaction> DebitAsync(WalletEntry entry)
    {
        if (entry.OwnerId == null)
            throw new ArgumentException("Un débit porte toujours sur un portefeuille individuel.");

        var amount = RevenueSplit.Xaf(Math.Abs(entry.Amount));
        if (amount <= 0) throw new ArgumentException("Montant de débit invalide.");

        var existing = await FindByKeyAsync(entry.IdempotencyKey);
        if (existing != null) return existing;

        var available = await GetAvailableAsync(entry.OwnerId.Value);
        if (available < amount)
            throw new InsufficientWalletBalanceException(Math.Max(0, available), amount);

        return await PostAsync(entry with { Amount = -amount, Status = WalletEntryStatus.Confirmed });
    }

    public async Task<WalletTransaction?> ReverseAsync(long entryId, string description, int? createdByUserId = null)
    {
        var original = await _db.WalletTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == entryId);
        if (original == null || original.Status != WalletEntryStatus.Confirmed) return null;
        if (original.EntryType == WalletEntryTypes.Reversal) return null;

        var already = await _db.WalletTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.ReversesEntryId == entryId);
        if (already != null) return already;

        var reversal = new WalletTransaction
        {
            OwnerType = original.OwnerType,
            OwnerId = original.OwnerId,
            EntryType = WalletEntryTypes.Reversal,
            Amount = -original.Amount,
            Status = WalletEntryStatus.Confirmed,
            SourceType = original.SourceType,
            SourceId = original.SourceId,
            IdempotencyKey = $"reversal:{original.Id}",
            ReversesEntryId = original.Id,
            Description = Truncate(description, 300),
            CreatedByUserId = createdByUserId,
            OccurredAt = DateTime.UtcNow,
            SettledAt = DateTime.UtcNow,
        };

        var existingKey = await FindByKeyAsync(reversal.IdempotencyKey);
        if (existingKey != null) return existingKey;

        _db.WalletTransactions.Add(reversal);
        try
        {
            await _db.SaveChangesAsync();
            return reversal;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.Entry(reversal).State = EntityState.Detached;
            return await FindByKeyAsync(reversal.IdempotencyKey);
        }
    }

    /// <summary>Passe une écriture en attente à confirmée ou contre-passée. Sans effet si elle n'est plus en attente.</summary>
    private async Task SettlePendingAsync(string key, string targetStatus)
    {
        var row = await _db.WalletTransactions.FirstOrDefaultAsync(t => t.IdempotencyKey == key);
        if (row == null || row.Status != WalletEntryStatus.Pending) return;
        row.Status = targetStatus;
        row.SettledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private async Task<WalletTransaction> BuildAsync(WalletEntry e)
    {
        var ownerType = e.OwnerType;
        if (e.OwnerId == null)
        {
            ownerType = WalletOwnerTypes.Platform;
        }
        else if (ownerType == null)
        {
            var role = await _db.Users.AsNoTracking().IgnoreQueryFilters()
                .Where(u => u.Id == e.OwnerId.Value).Select(u => u.Role).FirstOrDefaultAsync();
            ownerType = WalletOwnerTypes.ForRole(role);
        }

        if (e.EntryType != WalletEntryTypes.WithdrawalProcessed && RevenueSplit.Xaf(e.Amount) == 0)
            throw new ArgumentException("Une écriture de portefeuille porte un montant non nul.");

        var now = DateTime.UtcNow;
        return new WalletTransaction
        {
            OwnerType = ownerType!,
            OwnerId = e.OwnerId,
            EntryType = e.EntryType,
            Amount = RevenueSplit.Xaf(e.Amount),
            Status = e.Status,
            SourceType = e.SourceType,
            SourceId = e.SourceId,
            IdempotencyKey = e.IdempotencyKey,
            Description = Truncate(e.Description, 300),
            CreatedByUserId = e.CreatedByUserId,
            OccurredAt = e.OccurredAt ?? now,
            CreatedAt = now,
            SettledAt = e.Status == WalletEntryStatus.Pending ? null : now,
        };
    }

    // ── Traduction des événements métier ────────────────────────────────

    public static string CatalogSaleKey(int orderItemId) => $"catalog_sale:orderitem:{orderItemId}";
    public static string BalancePurchaseKey(int orderId) => $"balance_purchase:order:{orderId}";
    public static string TutoringKey(int bookingId) => $"tutoring:booking:{bookingId}";
    public static string TutoringCommissionKey(int bookingId) => $"tutoring_commission:booking:{bookingId}";
    public static string AffiliateKey(int commissionId) => $"affiliate:commission:{commissionId}";
    public static string ClassAssignmentKey(int teacherClassContentId) => $"class_assignment:{teacherClassContentId}";
    public static string WithdrawalRequestedKey(int withdrawalId) => $"withdrawal:{withdrawalId}:requested";
    public static string WithdrawalProcessedKey(int withdrawalId) => $"withdrawal:{withdrawalId}:processed";

    /// <summary>
    /// Commande : vente catalogue créditée à l'auteur de chaque contenu tant
    /// que la commande est payée (<see cref="PaidOrderStatus"/>), contre-passée
    /// dès qu'elle ne l'est plus (remboursement approuvé, annulation). Le débit
    /// d'un paiement par solde, posé par le paiement lui-même, est restitué de
    /// la même façon.
    ///
    /// Commission catalogue : nulle tant que le Module 7 n'est pas livré. Le
    /// point d'insertion de l'écriture plateforme est marqué ci-dessous.
    /// </summary>
    public async Task SyncOrderAsync(int orderId)
    {
        var order = await _db.Orders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.Id, o.UserId, o.Status, o.PaymentMethod, o.OrderNumber, o.CreatedAt, o.CompletedDate })
            .FirstOrDefaultAsync();
        if (order == null) return;

        var paid = PaidOrderStatus.IsPaid(order.Status);
        var occurredAt = order.CreatedAt;

        var items = await _db.OrderItems.AsNoTracking()
            .Where(oi => oi.OrderId == orderId && oi.Subject != null && oi.Subject.AuthorUserId != null)
            .Select(oi => new { oi.Id, oi.PriceAtPurchase, AuthorId = oi.Subject!.AuthorUserId!.Value, Title = oi.Subject.Title })
            .ToListAsync();

        foreach (var item in items)
        {
            var key = CatalogSaleKey(item.Id);
            var amount = RevenueSplit.Xaf(item.PriceAtPurchase);
            if (paid)
            {
                if (amount <= 0) continue;
                await PostAsync(new WalletEntry(item.AuthorId, WalletEntryTypes.CatalogSale, amount, key,
                    $"Vente : {item.Title}", "OrderItem", item.Id, OccurredAt: occurredAt));
                // Module 7 : insérer ici l'écriture PlatformCommission (part
                // retenue selon le score WinAI) et réduire d'autant la part
                // auteur. Aucune commission catalogue n'est appliquée avant.
            }
            else
            {
                var entry = await FindByKeyAsync(key);
                if (entry != null)
                    await ReverseAsync(entry.Id, $"Annulation de la vente ({order.OrderNumber}, commande {order.Status}) : {item.Title}");
            }
        }

        // Part réglée par le solde (paiement par solde, ou part solde d'un
        // paiement combiné solde + Mobile Money) : restituée dès que la
        // commande n'est plus payée (remboursement approuvé, échec ou
        // expiration du complément Mobile Money, annulation).
        if (!paid && !IsAwaitingPayment(order.Status))
        {
            var debit = await FindByKeyAsync(BalancePurchaseKey(order.Id));
            if (debit != null)
                await ReverseAsync(debit.Id, $"Part payée par le solde restituée (commande {order.OrderNumber}, {order.Status})");
        }

        await SyncWalletTopUpAsync(orderId);
    }

    /// <summary>Commande encore en attente de son paiement (complément Mobile Money en cours) : rien à restituer.</summary>
    private static bool IsAwaitingPayment(string? status) =>
        (status ?? string.Empty).Trim().ToLowerInvariant() is "pending" or "processing";

    public static string TopUpKey(int topUpId) => $"recharge:topup:{topUpId}";

    /// <summary>
    /// Recharge personnelle (Module 3) : une seule écriture <c>Recharge</c> par
    /// paiement confirmé, même si la confirmation arrive plusieurs fois ; aucune
    /// écriture tant que le paiement n'est pas confirmé.
    /// </summary>
    private async Task SyncWalletTopUpAsync(int orderId)
    {
        var topUp = await _db.WalletTopUps.FirstOrDefaultAsync(t => t.OrderId == orderId);
        if (topUp == null) return;

        var status = await _db.Orders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.Status).FirstOrDefaultAsync();
        var normalized = (status ?? string.Empty).ToLowerInvariant();

        if (normalized == "completed")
        {
            await PostAsync(new WalletEntry(topUp.UserId, WalletEntryTypes.Recharge, topUp.AmountXaf, TopUpKey(topUp.Id),
                "Recharge Mobile Money", "WalletTopUp", topUp.Id));
            if (topUp.Status != "completed")
            {
                topUp.Status = "completed";
                topUp.CompletedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }
        else if (normalized is "failed" or "cancelled" && topUp.Status == "pending")
        {
            topUp.Status = "failed";
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Réservation de tutorat : revenu enseignant (part du plan) et commission
    /// plateforme, en attente dès le paiement encaissé, confirmés à la
    /// libération de l'escrow, annulés (statut <c>reversed</c>) si la séance est
    /// remboursée, refusée, expirée ou annulée avant libération.
    /// </summary>
    public async Task SyncTutorBookingAsync(int bookingId)
    {
        var booking = await _db.TutorBookings.AsNoTracking()
            .Where(b => b.Id == bookingId)
            .Select(b => new
            {
                b.Id, b.PriceXaf, b.PaymentStatus, b.Status, b.EscrowReleasedAt, b.Subject, b.SessionDate, b.CreatedAt,
                TeacherId = b.TutorProfile != null ? (int?)b.TutorProfile.UserId : null,
            })
            .FirstOrDefaultAsync();
        if (booking?.TeacherId == null) return;

        var teacherId = booking.TeacherId.Value;
        var key = TutoringKey(booking.Id);
        var commissionKey = TutoringCommissionKey(booking.Id);
        var released = booking.EscrowReleasedAt != null;
        var paidAndLive = string.Equals(booking.PaymentStatus, "completed", StringComparison.OrdinalIgnoreCase)
                       && booking.Status is not ("cancelled" or "rejected" or "expired");

        if (released || paidAndLive)
        {
            var status = released ? WalletEntryStatus.Confirmed : WalletEntryStatus.Pending;
            var occurredAt = booking.EscrowReleasedAt ?? booking.CreatedAt;
            var label = string.IsNullOrWhiteSpace(booking.Subject)
                ? $"Cours particulier du {booking.SessionDate:dd/MM/yyyy}"
                : $"Cours particulier : {booking.Subject} ({booking.SessionDate:dd/MM/yyyy})";

            if (await FindByKeyAsync(key) == null)
            {
                var share = await RevenueSplit.GetTeacherShareAsync(_db, teacherId);
                var net = RevenueSplit.NetXaf(booking.PriceXaf, share);
                var commission = booking.PriceXaf - net;
                if (net > 0)
                    await PostAsync(new WalletEntry(teacherId, WalletEntryTypes.TutoringRevenue, net, key, label,
                        "TutorBooking", booking.Id, status, occurredAt));
                if (commission > 0)
                    await PostAsync(new WalletEntry(null, WalletEntryTypes.PlatformCommission, commission, commissionKey,
                        $"Commission tutorat, réservation #{booking.Id}", "TutorBooking", booking.Id, status, occurredAt));
            }

            if (released)
            {
                await SettlePendingAsync(key, WalletEntryStatus.Confirmed);
                await SettlePendingAsync(commissionKey, WalletEntryStatus.Confirmed);
            }
            return;
        }

        // Ni libéré ni payé et actif : le revenu en attente ne sera jamais acquis.
        await SettlePendingAsync(key, WalletEntryStatus.Reversed);
        await SettlePendingAsync(commissionKey, WalletEntryStatus.Reversed);
    }

    /// <summary>
    /// Commission d'affiliation : en attente à sa naissance, confirmée après
    /// maturation (AffiliateCommissionMaturityService, non recréé ici), annulée
    /// si la commande est remboursée avant maturation, contre-passée par une
    /// écriture inverse si elle l'est après.
    /// </summary>
    public async Task SyncAffiliateCommissionAsync(int commissionId)
    {
        var c = await _db.AffiliateCommissions.AsNoTracking()
            .Where(x => x.Id == commissionId)
            .Select(x => new { x.Id, x.Status, x.CommissionAmount, x.OrderId, x.CreatedAt, x.ConfirmedAt, UserId = x.AffiliateAccount!.UserId })
            .FirstOrDefaultAsync();
        if (c == null) return;

        var key = AffiliateKey(c.Id);
        var amount = RevenueSplit.Xaf(c.CommissionAmount);
        var existing = await FindByKeyAsync(key);
        var label = $"Commission d'affiliation, commande #{c.OrderId}";

        switch ((c.Status ?? string.Empty).ToLowerInvariant())
        {
            case "pending":
                if (existing == null && amount > 0)
                    await PostAsync(new WalletEntry(c.UserId, WalletEntryTypes.AffiliateCommission, amount, key, label,
                        "AffiliateCommission", c.Id, WalletEntryStatus.Pending, c.CreatedAt));
                break;

            case "confirmed":
                if (existing == null)
                {
                    if (amount > 0)
                        await PostAsync(new WalletEntry(c.UserId, WalletEntryTypes.AffiliateCommission, amount, key, label,
                            "AffiliateCommission", c.Id, WalletEntryStatus.Confirmed, c.ConfirmedAt ?? c.CreatedAt));
                }
                else
                {
                    await SettlePendingAsync(key, WalletEntryStatus.Confirmed);
                }
                break;

            case "reversed":
                if (existing == null) break;
                if (existing.Status == WalletEntryStatus.Pending)
                    await SettlePendingAsync(key, WalletEntryStatus.Reversed);
                else if (existing.Status == WalletEntryStatus.Confirmed)
                    await ReverseAsync(existing.Id, $"Annulation de la commission d'affiliation, commande #{c.OrderId}");
                break;

            // « paid » n'est écrit par aucun code et n'entrait pas dans l'ancien
            // solde : aucun effet, plutôt que d'en inventer un.
        }
    }

    public static string SessionEnrollmentKey(int sessionEnrollmentId) => $"session_enrollment:{sessionEnrollmentId}";

    public async Task SyncSessionEnrollmentAsync(int sessionEnrollmentId)
    {
        var enrollment = await _db.SessionEnrollments.AsNoTracking()
            .Where(e => e.Id == sessionEnrollmentId)
            .Select(e => new { e.Id, e.PaymentStatus, e.PriceChargedXaf, TeacherId = e.Session != null ? (int?)e.Session.CreatedBy : null, Title = e.Session != null ? e.Session.Title : null, e.EnrolledAt })
            .FirstOrDefaultAsync();
        if (enrollment?.TeacherId == null || enrollment.PriceChargedXaf is not > 0) return;

        var teacherId = enrollment.TeacherId.Value;
        var key = SessionEnrollmentKey(enrollment.Id);
        var commissionKey = $"session_commission:{enrollment.Id}";
        var paid = string.Equals(enrollment.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase);

        if (paid)
        {
            if (await FindByKeyAsync(key) != null) return;

            var share = await RevenueSplit.GetTeacherShareAsync(_db, teacherId);
            var net = RevenueSplit.NetXaf(enrollment.PriceChargedXaf.Value, share);
            var commission = enrollment.PriceChargedXaf.Value - net;
            var label = $"Session payante : {enrollment.Title}";

            if (net > 0)
                await PostAsync(new WalletEntry(teacherId, WalletEntryTypes.TutoringRevenue, net, key, label,
                    "SessionEnrollment", enrollment.Id, OccurredAt: enrollment.EnrolledAt));
            if (commission > 0)
                await PostAsync(new WalletEntry(null, WalletEntryTypes.PlatformCommission, commission, commissionKey,
                    $"Commission session, inscription #{enrollment.Id}", "SessionEnrollment", enrollment.Id, OccurredAt: enrollment.EnrolledAt));
        }
        else
        {
            // Remboursée, ou inscription supprimée avant paiement : le revenu
            // déjà confirmé (s'il existe) est contre-passé.
            var existing = await FindByKeyAsync(key);
            if (existing is { Status: WalletEntryStatus.Confirmed })
                await ReverseAsync(existing.Id, $"Annulation de la session : {enrollment.Title}");
            var existingCommission = await FindByKeyAsync(commissionKey);
            if (existingCommission is { Status: WalletEntryStatus.Confirmed })
                await ReverseAsync(existingCommission.Id, $"Annulation de la session (commission) : {enrollment.Title}");
        }
    }

    // ── Portefeuille parent (Module 14) ─────────────────────────────────

    public static string ParentAllocationKey(int parentId, DateTime periodStart) =>
        $"parent_allocation:{parentId}:{periodStart:yyyyMM}";

    public static string ParentAllocationExpiryKey(long allocationEntryId) =>
        $"allocation_expiry:{allocationEntryId}";

    public async Task PostParentMonthlyAllocationAsync(int parentId, decimal amount, DateTime periodStart, DateTime expiresAt, string planName)
    {
        if (amount <= 0) return;
        await PostAsync(new WalletEntry(parentId, WalletEntryTypes.MonthlyAllocation, amount,
            ParentAllocationKey(parentId, periodStart), $"Dotation mensuelle — plan {planName}",
            OwnerType: WalletOwnerTypes.Parent, OccurredAt: periodStart));
        // ExpiresAt n'est pas porté par WalletEntry (immuable après écriture) :
        // posé séparément, une seule fois, juste après la création.
        var row = await _db.WalletTransactions.FirstOrDefaultAsync(t => t.IdempotencyKey == ParentAllocationKey(parentId, periodStart));
        if (row != null && row.ExpiresAt == null)
        {
            row.ExpiresAt = expiresAt;
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>Reliquat non consommé d'une écriture de dotation donnée (jamais négatif).</summary>
    private async Task<decimal> AllocationRemainingAsync(WalletTransaction allocation)
    {
        var consumed = await _db.WalletTransactions.AsNoTracking()
            .Where(t => t.SourceType == "WalletTransaction" && t.SourceId == allocation.Id
                     && t.EntryType == WalletEntryTypes.AllocationConsumption
                     && t.Status == WalletEntryStatus.Confirmed)
            .SumAsync(t => (decimal?)-t.Amount) ?? 0m;
        var expired = await _db.WalletTransactions.AsNoTracking()
            .AnyAsync(t => t.SourceType == "WalletTransaction" && t.SourceId == allocation.Id
                        && t.EntryType == WalletEntryTypes.AllocationExpired);
        if (expired) return 0m;
        return Math.Max(0, allocation.Amount - consumed);
    }

    /// <summary>La dotation mensuelle la plus récente du parent, non encore expirée (contre-passée).</summary>
    private async Task<WalletTransaction?> LatestLiveAllocationAsync(int parentId) =>
        await _db.WalletTransactions.AsNoTracking()
            .Where(t => t.OwnerId == parentId && t.OwnerType == WalletOwnerTypes.Parent
                     && t.EntryType == WalletEntryTypes.MonthlyAllocation
                     && t.Status == WalletEntryStatus.Confirmed
                     && !_db.WalletTransactions.Any(e => e.SourceType == "WalletTransaction" && e.SourceId == t.Id
                                                       && e.EntryType == WalletEntryTypes.AllocationExpired))
            .OrderByDescending(t => t.OccurredAt)
            .FirstOrDefaultAsync();

    public async Task<ParentWalletBreakdown> GetParentBreakdownAsync(int parentId)
    {
        var available = await GetAvailableAsync(parentId);
        var allocation = await LatestLiveAllocationAsync(parentId);
        if (allocation == null) return new ParentWalletBreakdown(0, null, available);

        var remaining = await AllocationRemainingAsync(allocation);
        // Le reliquat ne peut jamais dépasser le disponible total (garde-fou si
        // une incohérence survenait) ; on ne l'expose jamais négatif non plus.
        remaining = Math.Clamp(remaining, 0, Math.Max(0, available));
        return new ParentWalletBreakdown(remaining, allocation.ExpiresAt, available);
    }

    public async Task DebitParentWalletAsync(int parentId, decimal amount, string idempotencyKeyBase, string description, string sourceType, int sourceId)
    {
        amount = RevenueSplit.Xaf(Math.Abs(amount));
        if (amount <= 0) throw new ArgumentException("Montant de débit invalide.");

        // Idempotence globale : si l'une ou l'autre écriture dérivée de cette
        // clé existe déjà, l'opération a déjà eu lieu (rejeu de confirmation,
        // double soumission) — ne rien refaire.
        var allocKey = $"{idempotencyKeyBase}:alloc";
        var permKey = $"{idempotencyKeyBase}:perm";
        if (await FindByKeyAsync(allocKey) != null || await FindByKeyAsync(permKey) != null)
            return;

        var available = await GetAvailableAsync(parentId);
        if (available < amount)
            throw new InsufficientWalletBalanceException(Math.Max(0, available), amount);

        var allocation = await LatestLiveAllocationAsync(parentId);
        var fromAllocation = 0m;
        if (allocation != null)
        {
            var remaining = Math.Clamp(await AllocationRemainingAsync(allocation), 0, available);
            fromAllocation = Math.Min(remaining, amount);
        }
        var fromPermanent = amount - fromAllocation;

        if (fromAllocation > 0)
            // SourceId est un int : suffisant pour un identifiant de ligne de
            // journal (volume de la table très loin de 2^31) ; cohérent avec le
            // reste du journal, qui n'utilise que des SourceId int.
            await PostAsync(new WalletEntry(parentId, WalletEntryTypes.AllocationConsumption, -fromAllocation, allocKey,
                description, "WalletTransaction", checked((int)allocation!.Id), OwnerType: WalletOwnerTypes.Parent));

        if (fromPermanent > 0)
            await PostAsync(new WalletEntry(parentId, WalletEntryTypes.ParentWalletPurchase, -fromPermanent, permKey,
                description, sourceType, sourceId, OwnerType: WalletOwnerTypes.Parent));
    }

    // ── Outils ──────────────────────────────────────────────────────────

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value ?? string.Empty : value[..max];

    private static bool IsUniqueViolation(Exception ex)
    {
        for (Exception? cur = ex; cur != null; cur = cur.InnerException)
            if (cur is Npgsql.PostgresException pg && pg.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation)
                return true;
        return false;
    }
}
