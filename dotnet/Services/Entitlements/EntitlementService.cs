using System.Globalization;
using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Backend.Services.Entitlements;

/// <summary>Origine d'un droit dans la fusion (affichée par la simulation admin).</summary>
public sealed record EntitlementSource(string Kind, string Label, int? RefId = null);

/// <summary>Plan payé (ou en essai) qui fonde les droits personnels.</summary>
public sealed record ActivePlanInfo(
    int SubscriptionId,
    int PricingPlanId,
    string? PlanKey,
    string PlanName,
    string Audience,
    int? PlanVersionId,
    int? VersionNumber,
    string? BillingPeriod,
    string Status,
    DateTime StartDate,
    DateTime? EndDate,
    DateTime? GraceUntil);

/// <summary>
/// Droits effectifs d'un utilisateur, après fusion gratuit → plan → donateurs
/// (parent, licence). Les grandeurs en tokens sont internes : elles ne sortent
/// jamais d'une API utilisateur (décision 8.3).
/// </summary>
public sealed class UserEntitlements
{
    public required int UserId { get; init; }
    public required string Role { get; init; }
    public string? Audience { get; init; }
    public required IReadOnlyDictionary<string, string> Values { get; init; }
    public ActivePlanInfo? Plan { get; init; }
    public required IReadOnlyList<EntitlementSource> Sources { get; init; }
    public long PersonalReferenceTokens { get; init; }
    public long ParentBonusTokens { get; init; }
    public long LicenseBonusTokens { get; init; }
    /// <summary>Référence du plan gratuit (base du multiplicateur 1x, 8.3).</summary>
    public long FreeReferenceTokens { get; init; }

    public long TotalReferenceTokens => PersonalReferenceTokens + ParentBonusTokens + LicenseBonusTokens;

    public int AiMultiplier => Math.Max(1, (int)Math.Round((double)TotalReferenceTokens / Math.Max(1, FreeReferenceTokens)));

    public string Raw(string key) =>
        Values.TryGetValue(key, out var v) ? v : EntitlementCatalog.Find(key)?.DefaultValue ?? "false";

    public bool Has(string key) => EntitlementCatalog.AsBool(Raw(key));

    public long Int(string key) => EntitlementCatalog.AsLong(Raw(key));

    public bool IsUnlimited(string key) => Int(key) == EntitlementCatalog.Unlimited;

    /// <summary>Vrai si un compteur actuel peut encore augmenter d'une unité.</summary>
    public bool AllowsOneMore(string key, long currentCount) => IsUnlimited(key) || currentCount < Int(key);

    /// <summary>Part décimale (0.70) d'un droit en pourcentage.</summary>
    public decimal Fraction(string key) => Int(key) / 100m;
}

public interface IEntitlementService
{
    /// <summary>Droits effectifs (cache court, invalidé à chaque changement d'abonnement ou de plan).</summary>
    Task<UserEntitlements> GetAsync(int userId, CancellationToken ct = default);

    /// <summary>Droits sans cache (simulation admin, audit de bascule).</summary>
    Task<UserEntitlements> ComputeAsync(int userId, CancellationToken ct = default);

    /// <summary>Droits du plan gratuit d'un public (version publiée en base, sinon catalogue).</summary>
    Task<IReadOnlyDictionary<string, string>> GetFreeValuesAsync(string audience, CancellationToken ct = default);

    /// <summary>Droits d'une version, complétés par ceux du plan gratuit de son public.</summary>
    Task<IReadOnlyDictionary<string, string>> GetVersionValuesAsync(int planVersionId, CancellationToken ct = default);

    /// <summary>Invalide le cache de tout le monde (un abonnement parent ou une licence touche d'autres comptes).</summary>
    void Invalidate();
}

/// <summary>
/// Service unique des droits (lot 9.7b, étude section 1.2). Il remplace toutes
/// les lectures de plan par nom ou par prix : le code appelant demande une
/// clé (<see cref="EntitlementCatalog"/>), jamais un palier.
///
/// Fusion, dans cet ordre :
///   1. droits du plan gratuit du public du compte (rôle) ;
///   2. droits de la version payée de l'abonnement en vigueur
///      (<see cref="SubscriptionRules"/>), qui remplacent les précédents ;
///   3. donateurs : parent lié dont le plan couvre l'enfant (catalogue et
///      bonus WinAI, 8.6 et 18.15), siège de licence d'établissement (lot 9.7b
///      étape 4). Un donateur ne fait que relever un droit (OU, maximum).
/// </summary>
public class EntitlementService : IEntitlementService
{
    /// <summary>Durée du cache : courte, le cache est surtout invalidé explicitement.</summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private static long _generation;

    private static readonly IEntitlementDonor[] DefaultDonors = { new LicenseEntitlementDonor() };

    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IEnumerable<IEntitlementDonor> _donors;

    public EntitlementService(ApplicationDbContext db, IMemoryCache cache, IEnumerable<IEntitlementDonor>? donors = null)
    {
        _db = db;
        _cache = cache;
        // Donateurs par défaut (licence d'établissement) : aussi pour les instances construites hors injection.
        _donors = donors != null && donors.Any() ? donors : DefaultDonors;
    }

    public void Invalidate() => Interlocked.Increment(ref _generation);

    /// <summary>Invalidation statique, pour les services qui n'ont pas l'instance sous la main.</summary>
    public static void InvalidateAll() => Interlocked.Increment(ref _generation);

    public async Task<UserEntitlements> GetAsync(int userId, CancellationToken ct = default)
    {
        var key = $"ent:{Interlocked.Read(ref _generation)}:{userId}";
        if (_cache.TryGetValue(key, out UserEntitlements? cached) && cached != null) return cached;
        var computed = await ComputeAsync(userId, ct);
        _cache.Set(key, computed, CacheDuration);
        return computed;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetFreeValuesAsync(string audience, CancellationToken ct = default)
    {
        var key = $"ent-free:{Interlocked.Read(ref _generation)}:{audience}";
        if (_cache.TryGetValue(key, out IReadOnlyDictionary<string, string>? cached) && cached != null) return cached;

        var values = EntitlementCatalog.All.ToDictionary(d => d.Key, d => d.DefaultValue, StringComparer.Ordinal);
        var freeVersionId = await _db.PlanVersions.AsNoTracking()
            .Where(v => v.Status == PlanVersionStatus.Published
                     && v.PricingPlan != null && !v.PricingPlan.IsDeleted
                     && v.PricingPlan.IsFreeTier && v.PricingPlan.Audience == audience)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => (int?)v.Id)
            .FirstOrDefaultAsync(ct);
        if (freeVersionId is int fid)
        {
            var rows = await _db.PlanEntitlements.AsNoTracking()
                .Where(e => e.PlanVersionId == fid)
                .Select(e => new { e.Key, e.Value })
                .ToListAsync(ct);
            foreach (var r in rows)
                if (EntitlementCatalog.IsKnown(r.Key)) values[r.Key] = r.Value;
        }

        _cache.Set(key, (IReadOnlyDictionary<string, string>)values, CacheDuration);
        return values;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetVersionValuesAsync(int planVersionId, CancellationToken ct = default)
    {
        var audience = await _db.PlanVersions.AsNoTracking().IgnoreQueryFilters()
            .Where(v => v.Id == planVersionId)
            .Select(v => v.PricingPlan!.Audience)
            .FirstOrDefaultAsync(ct) ?? PlanAudiences.Student;
        var values = new Dictionary<string, string>(await GetFreeValuesAsync(audience, ct), StringComparer.Ordinal);
        var rows = await _db.PlanEntitlements.AsNoTracking()
            .Where(e => e.PlanVersionId == planVersionId)
            .Select(e => new { e.Key, e.Value })
            .ToListAsync(ct);
        foreach (var r in rows)
            if (EntitlementCatalog.IsKnown(r.Key)) values[r.Key] = r.Value;
        return values;
    }

    /// <summary>
    /// Abonnement en vigueur d'un compte (le plus récent), plan et version
    /// inclus. Les plans archivés restent lus : un abonné garde ce qu'il a payé.
    /// </summary>
    public static async Task<Subscription?> FindInForceAsync(ApplicationDbContext db, int userId, DateTime now, CancellationToken ct = default) =>
        await db.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Include(s => s.PricingPlan)
            .Include(s => s.PlanVersion)
            .Where(s => s.UserId == userId)
            .Where(SubscriptionRules.InForce(now))
            .OrderByDescending(s => s.StartDate)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Version qui fonde les droits d'un abonnement : celle qu'il a payée ; à
    /// défaut (ligne antérieure au 9.7b), aucune : le compte garde les droits
    /// gratuits jusqu'à la migration (étude section 6, script manuel).
    /// </summary>
    private static int? EffectiveVersionId(Subscription s) => s.PlanVersionId;

    public async Task<UserEntitlements> ComputeAsync(int userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var role = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.Role).FirstOrDefaultAsync(ct) ?? "student";
        var audience = PlanAudiences.ForRole(role);

        var studentFree = await GetFreeValuesAsync(PlanAudiences.Student, ct);
        var freeReference = EntitlementCatalog.AsLong(studentFree[EntitlementCatalog.AiReferenceTokens]);
        if (freeReference <= 0) freeReference = 200_000;

        var values = new Dictionary<string, string>(
            await GetFreeValuesAsync(audience ?? PlanAudiences.Student, ct), StringComparer.Ordinal);
        var sources = new List<EntitlementSource> { new("free", $"Plan gratuit {PlanAudiences.Label(audience ?? PlanAudiences.Student).ToLowerInvariant()}") };

        ActivePlanInfo? planInfo = null;
        var sub = await FindInForceAsync(_db, userId, now, ct);
        // Un plan d'un autre public que le rôle du compte n'ouvre rien (étude
        // 0.2 point 7 : un abonnement professeur n'ouvrait pas à juste titre
        // le catalogue élève). Le contrôle du public se fait à la souscription ;
        // ici, une ligne incohérente héritée est ignorée.
        if (sub != null && EffectiveVersionId(sub) is int versionId
            && (audience == null || sub.PricingPlan?.Audience == null || sub.PricingPlan.Audience == audience))
        {
            var planValues = await _db.PlanEntitlements.AsNoTracking()
                .Where(e => e.PlanVersionId == versionId)
                .Select(e => new { e.Key, e.Value })
                .ToListAsync(ct);
            foreach (var r in planValues)
                if (EntitlementCatalog.IsKnown(r.Key)) values[r.Key] = r.Value;

            planInfo = new ActivePlanInfo(sub.Id, sub.PricingPlanId, sub.PricingPlan?.PlanKey,
                sub.PricingPlan?.Name ?? sub.PlanName ?? "Plan", sub.PricingPlan?.Audience ?? audience ?? PlanAudiences.Student,
                versionId, sub.PlanVersion?.VersionNumber, sub.BillingPeriod, sub.Status.ToLowerInvariant(),
                sub.StartDate, sub.EndDate, sub.GraceUntil);
            sources.Add(new EntitlementSource(sub.Status.ToLowerInvariant() == SubscriptionStatuses.Trialing ? "trial" : "plan",
                $"{planInfo.PlanName} (version {planInfo.VersionNumber})", sub.Id));
        }
        else if (sub != null && sub.PlanVersionId == null)
        {
            sources.Add(new EntitlementSource("legacy",
                $"Abonnement antérieur au 9.7b ({sub.PlanName ?? sub.PricingPlan?.Name}) : à migrer, droits gratuits en attendant", sub.Id));
        }

        var personal = EntitlementCatalog.AsLong(values[EntitlementCatalog.AiReferenceTokens]);

        // Donateur parent (8.6, 18.15) : seulement pour un compte élève.
        long parentBonus = 0;
        if (role == "student")
        {
            var parentIds = await _db.ParentStudentLinks.AsNoTracking()
                .Where(l => l.StudentId == userId && l.Status == "accepted")
                .Select(l => l.ParentId).Distinct().ToListAsync(ct);
            foreach (var parentId in parentIds)
            {
                var donation = await ComputeParentDonationAsync(parentId, userId, now, ct);
                if (donation == null) continue;
                if (donation.Value.OpensCatalog && !EntitlementCatalog.AsBool(values[EntitlementCatalog.CatalogAccess]))
                    values[EntitlementCatalog.CatalogAccess] = "true";
                parentBonus += donation.Value.BonusTokens;
                sources.Add(new EntitlementSource("parent", donation.Value.Label, parentId));
            }
        }

        // Autres donateurs (licences d'établissement, étape 4).
        long licenseBonus = 0;
        foreach (var donor in _donors)
        {
            var d = await donor.DonateAsync(_db, userId, role, now, ct);
            if (d == null) continue;
            foreach (var (k, v) in d.Values)
                if (EntitlementCatalog.IsKnown(k))
                    values[k] = values.TryGetValue(k, out var cur) ? EntitlementCatalog.Merge(k, cur, v) : v;
            if (d.ReferenceTokensFloor > personal) personal = d.ReferenceTokensFloor;
            licenseBonus += d.BonusTokens;
            sources.Add(new EntitlementSource(d.Kind, d.Label, d.RefId));
        }

        return new UserEntitlements
        {
            UserId = userId,
            Role = role,
            Audience = audience,
            Values = values,
            Plan = planInfo,
            Sources = sources,
            PersonalReferenceTokens = personal,
            ParentBonusTokens = parentBonus,
            LicenseBonusTokens = licenseBonus,
            FreeReferenceTokens = freeReference,
        };
    }

    /// <summary>
    /// Ce que le plan d'un parent apporte à un enfant donné : l'enfant doit
    /// figurer parmi les <c>children.max</c> premiers liens acceptés (les plus
    /// anciens, étude 1.4) ; le bonus WinAI par enfant est réparti entre les
    /// enfants couverts au-delà de <c>ai.parent_bonus_shares</c> parts (8.10).
    /// </summary>
    public async Task<(bool OpensCatalog, long BonusTokens, string Label)?> ComputeParentDonationAsync(
        int parentId, int childId, DateTime now, CancellationToken ct = default)
    {
        var parentSub = await FindInForceAsync(_db, parentId, now, ct);
        if (parentSub?.PlanVersionId is not int versionId) return null;
        if (parentSub.PricingPlan?.Audience is string aud && aud != PlanAudiences.Parent) return null;

        var pv = await GetVersionValuesAsync(versionId, ct);
        var max = EntitlementCatalog.AsLong(pv[EntitlementCatalog.ChildrenMax]);

        var covered = await CoveredChildrenAsync(_db, parentId, max, ct);
        if (!covered.Contains(childId)) return null;

        var perChild = EntitlementCatalog.AsLong(pv[EntitlementCatalog.AiParentBonusPerChild]);
        var shares = EntitlementCatalog.AsLong(pv[EntitlementCatalog.AiParentBonusShares]);
        var bonus = SharedBonus(perChild, shares, covered.Count);
        var opens = EntitlementCatalog.AsBool(pv[EntitlementCatalog.ChildrenCatalog]);
        return (opens, bonus, $"Plan {parentSub.PricingPlan?.Name ?? parentSub.PlanName} du parent");
    }

    /// <summary>Enfants couverts par un plan parent : liens acceptés les plus anciens, dans la limite <paramref name="max"/> (-1 = tous).</summary>
    public static async Task<List<int>> CoveredChildrenAsync(ApplicationDbContext db, int parentId, long max, CancellationToken ct = default)
    {
        var ordered = await db.ParentStudentLinks.AsNoTracking()
            .Where(l => l.ParentId == parentId && l.Status == "accepted")
            .OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
            .Select(l => l.StudentId)
            .ToListAsync(ct);
        var distinct = ordered.Distinct().ToList();
        return max == EntitlementCatalog.Unlimited ? distinct : distinct.Take((int)Math.Max(0, max)).ToList();
    }

    /// <summary>
    /// Enveloppe d'un donateur (8.10) : <paramref name="shares"/> bonus complets
    /// au plus, répartis à parts égales au-delà.
    /// </summary>
    public static long SharedBonus(long perBeneficiary, long shares, int beneficiaries)
    {
        if (perBeneficiary <= 0 || beneficiaries <= 0) return 0;
        if (shares == EntitlementCatalog.Unlimited || beneficiaries <= shares) return perBeneficiary;
        return perBeneficiary * Math.Max(0, shares) / beneficiaries;
    }

    /// <summary>Forme canonique d'un entier.</summary>
    public static string Canon(long n) => n.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Contribution d'un donateur (licence d'établissement…) aux droits d'un compte.</summary>
public sealed record EntitlementDonation(
    string Kind,
    string Label,
    int? RefId,
    IReadOnlyDictionary<string, string> Values,
    long BonusTokens = 0,
    long ReferenceTokensFloor = 0);

/// <summary>Source de droits extérieure au compte (licence d'établissement).</summary>
public interface IEntitlementDonor
{
    Task<EntitlementDonation?> DonateAsync(ApplicationDbContext db, int userId, string role, DateTime now, CancellationToken ct);
}
