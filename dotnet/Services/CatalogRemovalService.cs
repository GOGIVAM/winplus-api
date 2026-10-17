using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Retrait d'un contenu du catalogue (Module 21, décision §4.F/§6.4),
/// factorisé au lot 9.7a pour que TOUTES les suppressions le suivent : celle
/// de l'auteur (DELETE /api/subjects/{id}), celle du Catalogue admin et celle
/// de la bibliothèque admin (livres, vidéos), qui jusqu'ici retirait le
/// contenu sans rembourser ses acheteurs.
///
/// Chaque acheteur d'une commande payée est remboursé par crédit de son
/// portefeuille (jamais vers son moyen de paiement d'origine), le revenu déjà
/// compté pour l'auteur est contre-passé, l'acheteur est prévenu. Idempotent
/// (clés d'écriture par ligne de commande) : relancer ne rembourse pas deux fois.
/// </summary>
public interface ICatalogRemovalService
{
    /// <summary>Acheteurs à rembourser et montant total, sans rien écrire (affiché avant confirmation).</summary>
    Task<CatalogRemovalImpact> GetImpactAsync(int subjectId);

    /// <summary>Rembourse les acheteurs puis retire le contenu (suppression douce). False si introuvable.</summary>
    Task<CatalogRemovalResult?> RemoveAsync(int subjectId);
}

public sealed record CatalogRemovalImpact(int Buyers, decimal TotalRefundXaf);

public sealed record CatalogRemovalResult(int RefundedBuyers, decimal RefundedXaf, int Failures);

public class CatalogRemovalService : ICatalogRemovalService
{
    private readonly ApplicationDbContext _db;
    private readonly IWalletService _wallet;
    private readonly INtfyService _ntfy;
    private readonly ILogger<CatalogRemovalService> _logger;

    public CatalogRemovalService(ApplicationDbContext db, IWalletService wallet, INtfyService ntfy, ILogger<CatalogRemovalService> logger)
    {
        _db = db;
        _wallet = wallet;
        _ntfy = ntfy;
        _logger = logger;
    }

    private IQueryable<OrderItem> PaidItems(int subjectId) =>
        _db.OrderItems.AsNoTracking()
            .Where(oi => oi.SubjectId == subjectId && oi.Order.UserId != null && PaidOrderStatus.All.Contains(oi.Order.Status.ToLower()));

    /// <summary>
    /// Montant TTC réellement payé pour une ligne (décision du 2026-10-17) :
    /// `PriceAtPurchase` est le HT, le client a payé le TTC de la commande. La
    /// ligne reçoit sa part du total payé au prorata de son HT, ce qui tient
    /// compte d'une remise appliquée à la commande.
    /// </summary>
    internal static decimal PaidInclTax(decimal lineExclTax, decimal orderTotalPaid, decimal orderExclTaxSum)
    {
        if (lineExclTax <= 0m) return 0m;
        if (orderExclTaxSum <= 0m || orderTotalPaid <= 0m) return VatPolicy.LegacyTotalInclTax(lineExclTax);
        return decimal.Round(orderTotalPaid * lineExclTax / orderExclTaxSum, 0, MidpointRounding.AwayFromZero);
    }

    public async Task<CatalogRemovalImpact> GetImpactAsync(int subjectId)
    {
        var rows = await PaidItems(subjectId)
            .Select(oi => new { oi.Order.UserId, oi.PriceAtPurchase, oi.Order.TotalAmount, ExclSum = oi.Order.Items.Sum(x => x.PriceAtPurchase) })
            .ToListAsync();
        return new CatalogRemovalImpact(rows.Select(r => r.UserId).Distinct().Count(),
            rows.Sum(r => PaidInclTax(r.PriceAtPurchase, r.TotalAmount, r.ExclSum)));
    }

    public async Task<CatalogRemovalResult?> RemoveAsync(int subjectId)
    {
        var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.Id == subjectId);
        if (subject == null) return null;

        var paidItems = await PaidItems(subjectId)
            .Select(oi => new { oi.Id, oi.PriceAtPurchase, BuyerId = oi.Order.UserId, oi.Order.TotalAmount, ExclSum = oi.Order.Items.Sum(x => x.PriceAtPurchase) })
            .ToListAsync();

        var refunded = new HashSet<int>();
        decimal refundedXaf = 0m;
        var failures = 0;
        foreach (var item in paidItems)
        {
            try
            {
                var refundXaf = PaidInclTax(item.PriceAtPurchase, item.TotalAmount, item.ExclSum);
                if (refundXaf > 0)
                {
                    await _wallet.PostAsync(new WalletEntry(item.BuyerId, WalletEntryTypes.ContentRemovalRefund,
                        refundXaf, $"content_removal_refund:orderitem:{item.Id}",
                        $"Remboursement : « {subject.Title} » retiré de la vente", "OrderItem", item.Id));
                    refundedXaf += refundXaf;
                }

                var saleEntry = await _wallet.FindByKeyAsync(WalletService.CatalogSaleKey(item.Id));
                if (saleEntry != null)
                    await _wallet.ReverseAsync(saleEntry.Id, $"Contenu retiré de la vente : « {subject.Title} »");
                var commissionEntry = await _wallet.FindByKeyAsync(WalletService.CatalogCommissionKey(item.Id));
                if (commissionEntry != null)
                    await _wallet.ReverseAsync(commissionEntry.Id, $"Commission annulée, contenu retiré : « {subject.Title} »");

                refunded.Add(item.BuyerId!.Value);
                await _ntfy.PublishAsync($"winplus-user-{item.BuyerId}", "Contenu retiré de la vente",
                    $"« {subject.Title} » n'est plus disponible sur WinPlus. Le montant payé ({refundXaf:0} XAF) " +
                    "a été crédité sur votre portefeuille WinPlus.",
                    userId: item.BuyerId, type: "ContentRemoved", relatedEntityType: "subject", relatedEntityId: subjectId);
            }
            catch (Exception ex)
            {
                // Un échec sur un acheteur ne bloque ni la suppression ni les
                // autres remboursements : il est journalisé pour reprise manuelle.
                failures++;
                _logger.LogError(ex,
                    "Remboursement/contre-passation non posés pour l'acheteur {BuyerId} du contenu {SubjectId} supprimé (OrderItem {OrderItemId})",
                    item.BuyerId, subjectId, item.Id);
            }
        }

        subject.IsDeleted = true;
        subject.IsPublished = false;
        subject.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new CatalogRemovalResult(refunded.Count, refundedXaf, failures);
    }
}
