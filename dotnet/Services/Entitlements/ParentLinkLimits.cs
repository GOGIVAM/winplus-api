using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Entitlements;

/// <summary>
/// Lot 9.7b : <c>children.max</c> appliqué côté serveur (étude 0.2 point 8 :
/// il n'était appliqué nulle part). Un parent ne peut avoir plus de liens
/// (acceptés ou en attente) que son plan ne couvre d'enfants. Sans abonnement,
/// la liaison et le suivi de base restent gratuits pour 1 enfant (plan
/// gratuit). Les liens déjà acceptés au-delà (après une descente de plan)
/// sont conservés : seuls les plus anciens restent couverts par le plan.
/// </summary>
public static class ParentLinkLimits
{
    public sealed record Verdict(bool Allowed, long Max, int Current, string? Message);

    public static async Task<Verdict> CheckNewLinkAsync(ApplicationDbContext db, IEntitlementService entitlements,
        int parentId, int studentId, bool countPending = true, CancellationToken ct = default)
    {
        var ent = await entitlements.GetAsync(parentId, ct);
        var max = ent.Int(EntitlementCatalog.ChildrenMax);
        if (max == EntitlementCatalog.Unlimited) return new Verdict(true, max, 0, null);
        var current = await db.ParentStudentLinks.AsNoTracking()
            .Where(l => l.ParentId == parentId && l.StudentId != studentId
                     && (l.Status == "accepted" || (countPending && l.Status == "pending")))
            .Select(l => l.StudentId).Distinct().CountAsync(ct);
        if (current < max) return new Verdict(true, max, current, null);
        var message = max <= 1
            ? "Votre plan suit 1 enfant. Passez à un plan Famille pour en suivre davantage."
            : $"Votre plan suit {max} enfants au plus. Passez à un plan supérieur pour en ajouter.";
        return new Verdict(false, max, current, message);
    }
}
