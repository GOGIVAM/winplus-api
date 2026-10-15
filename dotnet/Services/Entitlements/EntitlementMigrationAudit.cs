using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Entitlements;

public sealed record EntitlementDiff(string Aspect, string Before, string After);

public sealed record EntitlementAuditRow(
    int UserId,
    string Email,
    string Role,
    string? LegacyPlan,
    string? TargetPlanKey,
    string Action,
    IReadOnlyList<EntitlementDiff> Differences);

/// <summary>
/// Lot 9.7b, étape 2 : audit de bascule, en lecture seule. Pour chaque compte
/// qui a un abonnement en cours, compare les droits <b>appliqués par l'ancien
/// code</b> (tout abonnement payant ouvrait le catalogue, quota WinAI déduit du
/// nom du plan, part professeur lue sur TeacherRevenueShare ou 80 %, enfants et
/// dotation jamais appliqués) aux droits que donnera la nouvelle grille après
/// la migration de l'étude section 6 (correspondance 6.3, script manuel
/// scripts/9.7b/migrate_subscriptions.sql). À lancer avant la bascule pour
/// relire chaque écart ; ne modifie rien.
///
/// La correspondance par nom de l'ancien plan ne sert qu'ici (et dans le
/// script de migration) : c'est la seule façon de relier les 10 anciens plans
/// à la nouvelle grille. Aucun contrôle d'accès ne la lit.
/// </summary>
public class EntitlementMigrationAudit
{
    /// <summary>Correspondance de l'étude 9.7b, section 6.3 (null = résilié, compte en gratuit).</summary>
    public static string? TargetPlanKey(string? legacyName, string? category)
    {
        var n = (legacyName ?? "").Trim().ToLowerInvariant();
        var c = (category ?? "").Trim().ToLowerInvariant();
        if (n.Contains("famille+") || n.Contains("famille +") || n.Contains("vip")) return "parent-famille-plus";
        if (n.Contains("famille")) return "parent-famille";
        if (n.Contains("annuel")) return "student-ultime";
        if (n.Contains("premium")) return "student-premium";
        if (n.Contains("standard")) return "student-standard";
        if (n.Contains("expert")) return "teacher-expert";
        if (n.Contains("pro") && c.StartsWith("teacher")) return "teacher-pro";
        if (n.Contains("pro")) return "teacher-pro";
        return null; // Starter, Basique professeur (gratuits) : résiliés
    }

    // Ancienne grille de référence par nom (AiUsagePolicy avant le 9.7b), recopiée
    // ici UNIQUEMENT pour mesurer l'écart : elle n'est plus appliquée nulle part.
    private static readonly (string Keyword, long Tokens)[] LegacyGrid =
    {
        ("annuel", 3_000_000), ("premium", 2_000_000), ("standard", 800_000), ("starter", 200_000),
        ("expert", 5_000_000), ("pro", 2_400_000), ("basique", 200_000), ("famille+", 2_000_000),
        ("famille +", 2_000_000), ("famille", 1_000_000), ("vip", 4_000_000),
    };

    private static long LegacyReference(PricingPlan? plan, string? name)
    {
        if (plan?.MaxChatMessages is int v && v > 0) return v;
        if (plan != null && plan.Price <= 0) return 200_000;
        var n = (name ?? plan?.Name ?? "").ToLowerInvariant();
        foreach (var (k, t) in LegacyGrid) if (n.Contains(k)) return t;
        return 800_000;
    }

    private readonly ApplicationDbContext _db;
    private readonly IEntitlementService _entitlements;

    public EntitlementMigrationAudit(ApplicationDbContext db, IEntitlementService entitlements)
    {
        _db = db;
        _entitlements = entitlements;
    }

    public async Task<IReadOnlyList<EntitlementAuditRow>> RunAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var subs = await _db.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Include(s => s.PricingPlan)
            .Where(s => !s.IsDeleted && s.Status.ToLower() == "active")
            .OrderBy(s => s.UserId).ThenByDescending(s => s.StartDate)
            .ToListAsync(ct);
        var userIds = subs.Select(s => s.UserId).Distinct().ToList();
        var users = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);
        var families = await _db.PricingPlans.AsNoTracking().Where(p => p.PlanKey != null)
            .ToDictionaryAsync(p => p.PlanKey!, ct);

        var rows = new List<EntitlementAuditRow>();
        foreach (var sub in subs.GroupBy(s => s.UserId).Select(g => g.First()))
        {
            if (!users.TryGetValue(sub.UserId, out var user)) continue;
            var audience = PlanAudiences.ForRole(user.Role);
            var legacyName = sub.PlanName ?? sub.PricingPlan?.Name;
            var paid = sub.PricingPlan?.Price > 0;

            // Ancien comportement réellement appliqué.
            var oldCatalog = paid;
            var oldRef = paid ? LegacyReference(sub.PricingPlan, legacyName) : 200_000;
            var oldShare = user.Role == "teacher" ? (sub.PricingPlan?.TeacherRevenueShare ?? 0.80m) : (decimal?)null;

            // Nouveau : déjà migré (version) ou cible de la correspondance 6.3.
            string? targetKey;
            IReadOnlyDictionary<string, string> newValues;
            string action;
            if (sub.PlanVersionId is int vid)
            {
                targetKey = sub.PricingPlan?.PlanKey;
                newValues = await _entitlements.GetVersionValuesAsync(vid, ct);
                action = "déjà migré";
            }
            else
            {
                targetKey = TargetPlanKey(legacyName, sub.PricingPlan?.Category);
                if (targetKey != null && families.TryGetValue(targetKey, out var fam) && fam.Audience != audience)
                {
                    targetKey = null;
                    action = "résilié : plan d'un autre public que le rôle du compte";
                }
                else action = targetKey == null ? "résilié : plan gratuit" : $"migré vers {targetKey}";

                var versionId = targetKey != null && families.TryGetValue(targetKey, out var f)
                    ? await _db.PlanVersions.AsNoTracking()
                        .Where(v => v.PricingPlanId == f.Id && v.Status == PlanVersionStatus.Published)
                        .Select(v => (int?)v.Id).FirstOrDefaultAsync(ct)
                    : null;
                newValues = versionId is int nv
                    ? await _entitlements.GetVersionValuesAsync(nv, ct)
                    : await _entitlements.GetFreeValuesAsync(audience ?? PlanAudiences.Student, ct);
                if (targetKey != null && versionId == null) action += " (grille absente : script SQL_Subscriptions97bGrid.sql à passer)";
            }

            var diffs = new List<EntitlementDiff>();
            var newCatalog = audience != PlanAudiences.Parent && EntitlementCatalog.AsBool(newValues[EntitlementCatalog.CatalogAccess]);
            if (newCatalog != oldCatalog) diffs.Add(new("catalogue", oldCatalog ? "ouvert" : "fermé", newCatalog ? "ouvert" : "fermé"));
            var newRef = EntitlementCatalog.AsLong(newValues[EntitlementCatalog.AiReferenceTokens]);
            if (newRef != oldRef) diffs.Add(new("référence WinAI (tokens)", oldRef.ToString("N0"), newRef.ToString("N0")));
            if (oldShare is decimal os)
            {
                var ns = EntitlementCatalog.AsLong(newValues[EntitlementCatalog.TeacherRevenueShare]) / 100m;
                if (ns != os) diffs.Add(new("part professeur", $"{os:P0}", $"{ns:P0}"));
            }
            if (audience == PlanAudiences.Parent)
            {
                diffs.Add(new("enfants couverts", "non appliqué", newValues[EntitlementCatalog.ChildrenMax]));
                diffs.Add(new("dotation par période (XAF)", "non versée", newValues[EntitlementCatalog.ParentMonthlyCreditXaf]));
                diffs.Add(new("catalogue des enfants", "fermé", EntitlementCatalog.AsBool(newValues[EntitlementCatalog.ChildrenCatalog]) ? "ouvert" : "fermé"));
            }
            rows.Add(new EntitlementAuditRow(user.Id, user.Email, user.Role, legacyName, targetKey, action, diffs));
        }
        return rows;
    }
}
