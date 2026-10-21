using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Achat d'un parent pour son enfant (commande portant le marqueur
/// <see cref="CourseSales.ChildMarkerPrefix"/>) : droits de l'enfant sur les
/// épreuves et livres de la commande.
///
/// L'accès d'un enfant à une épreuve achetée par son parent exige une
/// inscription (Enrollment) ET une commande payée d'un parent lié
/// (<see cref="ContentAccessService"/>). L'achat immédiat par le portefeuille
/// créait l'inscription lui-même, mais une commande confirmée plus tard (complément
/// ou paiement Mobile Money) n'en créait aucune : l'enfant ne voyait jamais
/// l'épreuve. Appelé depuis <see cref="WalletService.SyncOrderAsync"/>, point
/// de passage de toutes les confirmations, et idempotent.
/// </summary>
public static class ChildSubjectAccess
{
    /// <returns>Nombre d'inscriptions créées.</returns>
    public static async Task<int> SyncForOrderAsync(ApplicationDbContext db, int orderId)
    {
        var order = await db.Orders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.Id, o.UserId, o.Status, o.Notes, o.CreatedAt })
            .FirstOrDefaultAsync();
        if (order?.UserId == null || !PaidOrderStatus.IsPaid(order.Status)) return 0;

        var childId = CourseSales.BeneficiaryOf(order.UserId.Value, order.Notes);
        if (childId == order.UserId.Value) return 0;

        var linked = await db.ParentStudentLinks.AsNoTracking()
            .AnyAsync(l => l.ParentId == order.UserId.Value && l.StudentId == childId && l.Status == "accepted");
        if (!linked) return 0;

        var lines = await db.OrderItems.AsNoTracking()
            .Where(oi => oi.OrderId == orderId)
            .Select(oi => new
            {
                oi.SubjectId,
                oi.CourseId,
                oi.PriceAtPurchase,
                Title = oi.Subject != null ? oi.Subject.Title : oi.Course != null ? oi.Course.Title : null,
            })
            .ToListAsync();

        var created = 0;
        foreach (var subjectId in lines.Where(l => l.SubjectId != null).Select(l => l.SubjectId!.Value).Distinct())
        {
            var enrolled = await db.Enrollments.AnyAsync(e => e.UserId == childId && e.SubjectId == subjectId);
            if (enrolled) continue;
            db.Enrollments.Add(new Enrollment { UserId = childId, SubjectId = subjectId });
            created++;
        }

        // Trace bénéficiaire lue par l'écran « impact d'achat » (registre
        // historique, comme l'achat pour un enfant à l'unité) : une seule fois
        // par commande.
        var traced = await db.ParentCreditLedgers.AsNoTracking().AnyAsync(l => l.OrderId == orderId);
        if (!traced)
        {
            var periodStart = new DateTime(order.CreatedAt.Year, order.CreatedAt.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            foreach (var line in lines)
                db.ParentCreditLedgers.Add(new ParentCreditLedger
                {
                    ParentId    = order.UserId.Value,
                    EntryType   = "consumption",
                    Amount      = VatPolicy.LegacyTotalInclTax(line.PriceAtPurchase),
                    ChildId     = childId,
                    OrderId     = orderId,
                    PeriodStart = periodStart,
                    Label       = line.Title,
                });
        }

        if (created > 0 || !traced) await db.SaveChangesAsync();
        return created;
    }
}

/// <summary>
/// Remise d'un code promo sur un panier valorisé, par la même règle que
/// <see cref="OrderService.CreateOrderAsync"/>. Sert aux paiements par le
/// solde, qui doivent connaître le montant remisé AVANT de débiter : le
/// contrôle de solde, la clé de doublon et le débit portent tous sur ce total.
/// </summary>
public static class CartPromo
{
    /// <param name="Discount">Remise en XAF (0 sans code).</param>
    /// <param name="Code">Code normalisé, ou null sans code.</param>
    /// <param name="Invalid">Vrai si un code a été envoyé mais n'est plus valable.</param>
    public sealed record Result(decimal Discount, string? Code, bool Invalid, string? Error);

    public static async Task<Result> ComputeAsync(IPromoCodeService? promo, int userId, IReadOnlyList<PricedCartItem> items, string? code)
    {
        var normalized = string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
        if (normalized == null) return new(0m, null, false, null);
        if (promo == null) return new(0m, null, true, null);

        var total = items.Sum(i => i.ServerPrice);
        var validation = await promo.ValidatePromoCodeAsync(userId, new ValidatePromoCodeRequest
        {
            Code = normalized,
            CartTotal = total,
            SubjectIds = items.Where(i => i.SubjectId != null).Select(i => i.SubjectId!.Value).ToList(),
            CatalogTotal = items.Where(i => i.SubjectId != null).Sum(i => i.ServerPrice),
            CourseIds = items.Where(i => i.CourseId != null).Select(i => i.CourseId!.Value).ToList(),
            CoursesTotal = items.Where(i => i.CourseId != null).Sum(i => i.ServerPrice),
        });
        if (!validation.IsValid) return new(0m, null, true, validation.ErrorMessage);

        var discount = decimal.Round(validation.DiscountAmount, 0, MidpointRounding.AwayFromZero);
        return new(Math.Clamp(discount, 0m, total), normalized, false, null);
    }
}
