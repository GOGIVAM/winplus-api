using Backend.Data;

namespace Backend.Services.Entitlements;

/// <summary>
/// Dotation du portefeuille parent (lot 9.7b, étude 1.4, décision 18.21 :
/// Basique 0 / Famille 1 000 / Famille+ 2 000 XAF). Le montant est le droit
/// <c>parent.monthly_credit_xaf</c> du plan en vigueur. Elle est <b>calée sur
/// la date d'abonnement</b>, non plus sur le mois civil : une dotation par
/// cycle d'un mois compté depuis le début de l'abonnement, dans la limite de
/// la période payée (un trimestre payé ouvre trois cycles). Elle expire à la
/// fin de son cycle (règles §2.6 inchangées : pas de report, pas de prorata).
/// </summary>
public static class ParentAllowance
{
    public sealed record Cycle(DateTime Start, DateTime End);

    /// <summary>Cycle mensuel en cours d'un abonnement commencé à <paramref name="start"/>, borné par sa fin.</summary>
    public static Cycle? CurrentCycle(DateTime start, DateTime? end, DateTime now)
    {
        if (now < start) return null;
        if (end.HasValue && now >= end.Value) return null;
        var months = (now.Year - start.Year) * 12 + now.Month - start.Month;
        var cycleStart = start.AddMonths(months);
        if (cycleStart > now) cycleStart = start.AddMonths(--months);
        var cycleEnd = start.AddMonths(months + 1);
        if (end.HasValue && cycleEnd > end.Value) cycleEnd = end.Value;
        return new Cycle(cycleStart, cycleEnd);
    }

    /// <summary>
    /// Écrit la dotation du cycle en cours si le plan en prévoit une (idempotent :
    /// clé du registre par parent et par mois de début de cycle).
    /// </summary>
    public static async Task<UserEntitlements> EnsureCurrentAsync(IEntitlementService entitlements, IWalletService wallet, int parentId, DateTime now)
    {
        var ent = await entitlements.GetAsync(parentId);
        var amount = ent.Int(EntitlementCatalog.ParentMonthlyCreditXaf);
        if (ent.Plan == null || amount <= 0) return ent;
        // Essai gratuit : aucune somme versée tant que rien n'a été payé.
        if (ent.Plan.Status == SubscriptionStatuses.Trialing) return ent;
        var cycle = CurrentCycle(ent.Plan.StartDate, ent.Plan.EndDate, now);
        if (cycle == null) return ent;
        await wallet.PostParentMonthlyAllocationAsync(parentId, amount, cycle.Start, cycle.End, ent.Plan.PlanName);
        return ent;
    }
}
