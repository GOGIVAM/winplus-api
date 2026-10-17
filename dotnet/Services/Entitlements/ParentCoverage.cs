using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Entitlements;

/// <summary>
/// Choix par le parent des enfants couverts par son plan (étude 9.7b 1.4),
/// quand il a plus d'enfants liés et acceptés que <c>children.max</c>
/// (cas d'une descente de plan : la liaison au-delà est conservée).
///
/// Règles :
///   - sans choix enregistré, les liens acceptés les plus anciens sont
///     couverts (règle par défaut, inchangée) ;
///   - le parent désigne de 1 à <c>children.max</c> enfants parmi ses
///     enfants acceptés ; les places non désignées restent attribuées par la
///     règle par défaut ;
///   - <b>un changement par cycle d'abonnement</b> : le cycle est le même que
///     celui de la dotation (<see cref="ParentAllowance.CurrentCycle"/>, un
///     mois compté depuis la date d'abonnement, borné par la période payée).
///     Sans plan payant en vigueur, un changement tous les 30 jours. Le
///     premier choix est toujours possible. Ce rythme évite de « faire
///     tourner » le catalogue d'un enfant à l'autre dans la même période ;
///   - le choix s'applique partout où le plan couvre un enfant : catalogue,
///     bonus WinAI (<see cref="EntitlementService"/>) et alertes en temps
///     réel (<c>ParentAdvisorThresholdService</c>).
/// </summary>
public static class ParentCoverage
{
    public static readonly TimeSpan FreeChangeInterval = TimeSpan.FromDays(30);

    public sealed record Child(int StudentId, string Name, string? AvatarUrl, DateTime LinkedAt, bool Covered, bool Chosen);

    public sealed record Status(
        long Max,
        bool Unlimited,
        bool SelectionNeeded,
        bool CustomSelection,
        bool CanChange,
        DateTime? LastChangedAt,
        DateTime? NextChangeAt,
        IReadOnlyList<Child> Children);

    public sealed record Outcome(bool Ok, string? Error, string? Code, Status? Status);

    /// <summary>Date à partir de laquelle un nouveau changement est permis (null : tout de suite).</summary>
    public static DateTime? NextChangeAt(ActivePlanInfo? plan, DateTime? lastChangedAt, DateTime now)
    {
        if (lastChangedAt == null) return null;
        if (plan != null)
        {
            var cycle = ParentAllowance.CurrentCycle(plan.StartDate, plan.EndDate, now);
            if (cycle != null)
                return lastChangedAt.Value < cycle.Start ? null : cycle.End;
        }
        var next = lastChangedAt.Value + FreeChangeInterval;
        return next <= now ? null : next;
    }

    public static async Task<Status> GetAsync(ApplicationDbContext db, IEntitlementService entitlements, int parentId,
        DateTime now, CancellationToken ct = default)
    {
        var ent = await entitlements.GetAsync(parentId, ct);
        var max = ent.Int(EntitlementCatalog.ChildrenMax);
        var unlimited = max == EntitlementCatalog.Unlimited;

        var links = await db.ParentStudentLinks.AsNoTracking()
            .Where(l => l.ParentId == parentId && l.Status == "accepted")
            .OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
            .Select(l => new
            {
                l.StudentId,
                l.CreatedAt,
                Name = ((l.Student!.FirstName ?? "") + " " + (l.Student!.LastName ?? "")).Trim(),
                l.Student!.AvatarUrl,
            })
            .ToListAsync(ct);
        var distinct = links.GroupBy(l => l.StudentId).Select(g => g.First()).ToList();

        var chosenRows = await db.ParentChildCoverages.AsNoTracking()
            .Where(c => c.ParentId == parentId)
            .Select(c => new { c.StudentId, c.SelectedAt })
            .ToListAsync(ct);
        var chosen = chosenRows.Select(c => c.StudentId).ToHashSet();
        DateTime? last = chosenRows.Count == 0 ? null : chosenRows.Max(c => c.SelectedAt);

        var covered = (await EntitlementService.CoveredChildrenAsync(db, parentId, max, ct)).ToHashSet();
        var next = NextChangeAt(ent.Plan, last, now);
        var needed = !unlimited && distinct.Count > max;

        return new Status(max, unlimited, needed, chosen.Count > 0, needed && next == null, last, next,
            distinct.Select(l => new Child(l.StudentId, string.IsNullOrWhiteSpace(l.Name) ? $"Élève #{l.StudentId}" : l.Name,
                l.AvatarUrl, l.CreatedAt, covered.Contains(l.StudentId), chosen.Contains(l.StudentId))).ToList());
    }

    /// <summary>Enregistre le choix du parent (remplace le précédent).</summary>
    public static async Task<Outcome> SetAsync(ApplicationDbContext db, IEntitlementService entitlements, int parentId,
        IReadOnlyCollection<int> studentIds, DateTime now, CancellationToken ct = default)
    {
        var current = await GetAsync(db, entitlements, parentId, now, ct);
        var ids = (studentIds ?? Array.Empty<int>()).Distinct().ToList();

        if (!current.SelectionNeeded)
            return new Outcome(false, current.Unlimited
                ? "Votre plan couvre tous vos enfants : il n'y a rien à choisir."
                : $"Votre plan couvre jusqu'à {current.Max} enfant{(current.Max > 1 ? "s" : "")} et vous en avez {current.Children.Count} : ils sont tous couverts.",
                "selection_not_needed", current);
        if (ids.Count == 0)
            return new Outcome(false, "Choisissez au moins un enfant.", "selection_empty", current);
        if (ids.Count > current.Max)
            return new Outcome(false, $"Votre plan couvre {current.Max} enfant{(current.Max > 1 ? "s" : "")} au plus : choisissez-en {current.Max}.", "selection_too_large", current);
        var linked = current.Children.Select(c => c.StudentId).ToHashSet();
        if (ids.Any(id => !linked.Contains(id)))
            return new Outcome(false, "Un des enfants choisis n'est pas lié à votre compte ou n'a pas encore accepté la liaison.", "child_not_linked", current);

        var currentChosen = current.Children.Where(c => c.Chosen).Select(c => c.StudentId).ToHashSet();
        if (currentChosen.SetEquals(ids))
            return new Outcome(true, null, null, current);

        if (!current.CanChange)
            return new Outcome(false,
                $"Vous avez déjà modifié ce choix pendant la période en cours. Prochain changement possible le {current.NextChangeAt:dd/MM/yyyy}.",
                "change_limit", current);

        var existing = await db.ParentChildCoverages.Where(c => c.ParentId == parentId).ToListAsync(ct);
        db.ParentChildCoverages.RemoveRange(existing);
        foreach (var id in ids)
            db.ParentChildCoverages.Add(new ParentChildCoverage { ParentId = parentId, StudentId = id, SelectedAt = now });
        await db.SaveChangesAsync(ct);

        // Le choix touche les droits des enfants (catalogue, bonus) : cache global invalidé.
        entitlements.Invalidate();
        return new Outcome(true, null, null, await GetAsync(db, entitlements, parentId, now, ct));
    }
}
