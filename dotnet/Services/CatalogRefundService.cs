using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Remboursement d'une commande catalogue approuvé par l'administration
/// (lot 9.7a, suivi 18.5) : crédit automatique au portefeuille de
/// l'acheteur, selon la règle des frais du 9.2 (payeur à l'origine de la
/// demande : les frais NotchPay de l'encaissement d'origine restent à sa
/// charge).
///
/// Deux temps, comme le 9.2 (<see cref="TutorRefundService"/>) :
/// <list type="number">
///   <item><see cref="DecideAsync"/> fige la décision (montant, frais, part
///   déjà restituée par le solde) dans la transaction de l'approbation ;
///   une décision existante n'est jamais recalculée (double clic, deuxième
///   administrateur) ;</item>
///   <item><see cref="WalletService.SyncOrderAsync"/> pose les écritures
///   (crédit de l'acheteur, frais récupérés par la plateforme), idempotentes
///   par clé et rejouées par la réconciliation si l'écriture échoue.</item>
/// </list>
/// La contre-passation du revenu du vendeur et de la commission (déjà
/// existante) est inchangée.
/// </summary>
public interface ICatalogRefundService
{
    /// <summary>
    /// Fige la décision de crédit de la commande (déjà passée en
    /// <c>refunded</c> par l'appelant). Renvoie null si la commande n'ouvre
    /// pas de crédit (invité, abonnement, recharge, sans contenu catalogue).
    /// </summary>
    Task<CatalogRefundDecision?> DecideAsync(int orderId, int? adminUserId);

    /// <summary>Simulation sans écriture (affichage avant décision).</summary>
    Task<CatalogRefundDecision?> PreviewAsync(int orderId);
}

public class CatalogRefundService : ICatalogRefundService
{
    private readonly ApplicationDbContext _db;
    private readonly IPlatformSettings? _settings;
    private readonly IConfiguration? _configuration;

    public CatalogRefundService(ApplicationDbContext db, IConfiguration? configuration = null, IPlatformSettings? settings = null)
    {
        _db = db;
        _configuration = configuration;
        _settings = settings;
    }

    public async Task<CatalogRefundDecision?> DecideAsync(int orderId, int? adminUserId)
    {
        var existing = await _db.CatalogRefundDecisions.AsNoTracking().FirstOrDefaultAsync(d => d.OrderId == orderId);
        if (existing != null) return existing;

        var decision = await ComputeAsync(orderId);
        if (decision == null) return null;

        decision.DecidedByUserId = adminUserId;
        decision.DecidedAt = DateTime.UtcNow;
        _db.CatalogRefundDecisions.Add(decision);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Décision concurrente déjà enregistrée : c'est elle qui fait foi.
            _db.Entry(decision).State = EntityState.Detached;
            return await _db.CatalogRefundDecisions.AsNoTracking().FirstOrDefaultAsync(d => d.OrderId == orderId);
        }
        return decision;
    }

    public Task<CatalogRefundDecision?> PreviewAsync(int orderId) => ComputeAsync(orderId);

    private async Task<CatalogRefundDecision?> ComputeAsync(int orderId)
    {
        var order = await _db.Orders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.Id, o.UserId, o.TotalAmount, o.PaymentMethod, o.Notes })
            .FirstOrDefaultAsync();
        if (order?.UserId == null) return null;

        // Abonnements (lot 9.7b) et recharges de portefeuille : hors 18.5.
        if (SubscriptionOrderIntent.TryParse(order.Notes, out _)) return null;
        if (await _db.WalletTopUps.AsNoTracking().AnyAsync(t => t.OrderId == orderId)) return null;
        var hasCatalogItem = await _db.OrderItems.AsNoTracking()
            .AnyAsync(oi => oi.OrderId == orderId && (oi.CourseId != null || oi.SubjectId > 0));
        if (!hasCatalogItem) return null;

        var total = RevenueSplit.Xaf(Math.Max(0m, order.TotalAmount));

        // Part réglée par le solde professeur : restituée par la
        // contre-passation de son débit (WalletService.SyncOrderAsync), jamais
        // créditée une seconde fois ici.
        var balanceDebit = await _db.WalletTransactions.AsNoTracking()
            .Where(t => t.IdempotencyKey == WalletService.BalancePurchaseKey(orderId))
            .Select(t => (decimal?)t.Amount)
            .FirstOrDefaultAsync();
        var balancePart = Math.Min(total, RevenueSplit.Xaf(Math.Abs(balanceDebit ?? 0m)));

        var isParentWallet = string.Equals(order.PaymentMethod, "parent_wallet", StringComparison.OrdinalIgnoreCase);
        var gross = Math.Max(0m, total - balancePart);

        decimal fee = 0m;
        if (!isParentWallet && gross > 0)
        {
            // Frais réels si NotchPay les a renvoyés, sinon au taux réglé (9.2).
            var recordedFee = await _db.Payments.AsNoTracking()
                .Where(p => p.OrderId == orderId && p.Status == "completed" && p.FeeAmount != null)
                .SumAsync(p => (decimal?)p.FeeAmount) ?? 0m;
            var percent = TutorPolicySettings.From(_configuration, _settings).NotchPayFeePercent;
            fee = recordedFee > 0 ? RevenueSplit.Xaf(recordedFee) : TutorRefundRules.NotchPayFee(gross, percent);
            fee = Math.Clamp(fee, 0m, gross);
        }

        return new CatalogRefundDecision
        {
            OrderId = orderId,
            BuyerUserId = order.UserId.Value,
            OrderTotalXaf = total,
            BalancePartXaf = balancePart,
            FeeXaf = fee,
            RefundXaf = gross - fee,
            PaymentKind = isParentWallet ? "parent_wallet" : gross == 0 ? "balance" : "mobile_money",
        };
    }
}
