using System.Text.RegularExpressions;
using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Entitlements;

public sealed class PlanServiceException : Exception
{
    public int StatusCode { get; }
    public PlanServiceException(string message, int statusCode = 400) : base(message) { StatusCode = statusCode; }
}

public sealed record PlanPriceDto(string Period, decimal PriceTtc);

public sealed record PlanFeatureDto(string Key, string Label, string Group, string Value, string Display, bool Included);

/// <summary>Carte publique d'un plan (page tarifs, aperçu admin) : uniquement des droits réellement appliqués.</summary>
public sealed record PlanCardDto(
    int PlanId,
    string? PlanKey,
    string Name,
    string? Description,
    string Audience,
    bool IsFree,
    bool IsPopular,
    int VersionId,
    int VersionNumber,
    IReadOnlyList<PlanPriceDto> Prices,
    int AiMultiplier,
    IReadOnlyList<PlanFeatureDto> Features);

public sealed record PlanVersionAdminDto(
    int Id,
    int VersionNumber,
    string Status,
    string? Notes,
    DateTime CreatedAt,
    DateTime? PublishedAt,
    DateTime? ExistingSubscribersFrom,
    bool IsImprovement,
    DateTime? ArchivedAt,
    IReadOnlyList<PlanPriceDto> Prices,
    IReadOnlyDictionary<string, string> Entitlements,
    int Subscribers);

public sealed record PlanFamilyAdminDto(
    int Id,
    string? PlanKey,
    string Name,
    string? Description,
    string Audience,
    bool IsFreeTier,
    bool IsPopular,
    int SortOrder,
    bool IsArchived,
    int Subscribers,
    IReadOnlyList<PlanVersionAdminDto> Versions);

public sealed class CreatePlanFamilyRequest
{
    public string? Audience { get; set; }
    public string? PlanKey { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool IsFreeTier { get; set; }
    public bool IsPopular { get; set; }
    public int SortOrder { get; set; }
}

public sealed class UpdatePlanFamilyRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? IsPopular { get; set; }
    public int? SortOrder { get; set; }
}

public sealed class UpdatePlanVersionRequest
{
    public string? Notes { get; set; }
    /// <summary>Prix TTC par période ; null ou absent = période non proposée.</summary>
    public Dictionary<string, decimal?>? Prices { get; set; }
    public Dictionary<string, string?>? Entitlements { get; set; }
}

public sealed class GrantPlanRequest
{
    public int UserId { get; set; }
    public int PlanId { get; set; }
    public string? BillingPeriod { get; set; }
    /// <summary>Durée offerte en mois (1 à 36). Par défaut, la durée de la période.</summary>
    public int? Months { get; set; }
    public string? Reason { get; set; }
}

public sealed record PublishResult(int VersionId, bool IsImprovement, DateTime ExistingSubscribersFrom, int SubscribersMovedNow, int SubscribersNotified);

/// <summary>
/// Gestion des plans par l'administration (lot 9.7b, 18.10, 18.12, 18.13,
/// 18.19) : familles, versions en brouillon, publication, archivage, octroi
/// manuel. Toute écriture invalide le cache des droits.
/// </summary>
public class PlanCatalogService
{
    /// <summary>Préavis d'une hausse de prix ou d'une baisse de droits (18.12).</summary>
    public const int ChangeNoticeDays = 30;

    private static readonly Regex KeyPattern = new("^[a-z0-9][a-z0-9-]{1,48}$", RegexOptions.Compiled);

    private readonly ApplicationDbContext _db;
    private readonly IEntitlementService _entitlements;

    public PlanCatalogService(ApplicationDbContext db, IEntitlementService entitlements)
    {
        _db = db;
        _entitlements = entitlements;
    }

    // ── Lecture ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PlanFamilyAdminDto>> ListAsync(string? audience = null, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var query = _db.PricingPlans.IgnoreQueryFilters().AsNoTracking().Where(p => p.Audience != null);
        if (!string.IsNullOrWhiteSpace(audience)) query = query.Where(p => p.Audience == audience);
        var families = await query
            .Include(p => p.Versions).ThenInclude(v => v.Prices)
            .Include(p => p.Versions).ThenInclude(v => v.Entitlements)
            .OrderBy(p => p.Audience).ThenBy(p => p.SortOrder).ThenBy(p => p.Id)
            .ToListAsync(ct);

        var counts = await _db.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(SubscriptionRules.InForce(now))
            .Where(s => s.PlanVersionId != null)
            .GroupBy(s => s.PlanVersionId!.Value)
            .Select(g => new { VersionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.VersionId, x => x.Count, ct);

        return families.Select(f =>
        {
            var versions = f.Versions.OrderByDescending(v => v.VersionNumber).Select(v => ToDto(v, counts)).ToList();
            return new PlanFamilyAdminDto(f.Id, f.PlanKey, f.Name, f.Description, f.Audience!, f.IsFreeTier, f.IsPopular,
                f.SortOrder, f.IsDeleted, versions.Sum(v => v.Subscribers), versions);
        }).ToList();
    }

    private static PlanVersionAdminDto ToDto(PlanVersion v, IReadOnlyDictionary<int, int> counts) =>
        new(v.Id, v.VersionNumber, v.Status, v.Notes, v.CreatedAt, v.PublishedAt, v.ExistingSubscribersFrom, v.IsImprovement,
            v.ArchivedAt,
            v.Prices.OrderBy(p => BillingPeriods.Months(p.Period)).Select(p => new PlanPriceDto(p.Period, p.PriceTtc)).ToList(),
            v.Entitlements.OrderBy(e => e.Key).ToDictionary(e => e.Key, e => e.Value),
            counts.TryGetValue(v.Id, out var c) ? c : 0);

    /// <summary>Version publiée d'une famille (celle qui se vend), ou null.</summary>
    public async Task<PlanVersion?> PublishedVersionAsync(int planId, CancellationToken ct = default) =>
        await _db.PlanVersions.Include(v => v.Prices).Include(v => v.Entitlements)
            .Where(v => v.PricingPlanId == planId && v.Status == PlanVersionStatus.Published)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

    /// <summary>Cartes publiques des plans en vente d'un public (page tarifs web et mobile).</summary>
    public async Task<IReadOnlyList<PlanCardDto>> PublicCardsAsync(string audience, CancellationToken ct = default)
    {
        var ids = await _db.PlanVersions.AsNoTracking()
            .Where(v => v.Status == PlanVersionStatus.Published && v.PricingPlan != null
                     && !v.PricingPlan.IsDeleted && v.PricingPlan.Audience == audience)
            .OrderBy(v => v.PricingPlan!.SortOrder).ThenBy(v => v.PricingPlanId)
            .Select(v => v.Id)
            .ToListAsync(ct);
        var cards = new List<PlanCardDto>();
        foreach (var id in ids) cards.Add(await BuildCardAsync(id, ct));
        return cards;
    }

    public async Task<PlanCardDto> BuildCardAsync(int versionId, CancellationToken ct = default)
    {
        var v = await _db.PlanVersions.AsNoTracking().IgnoreQueryFilters()
            .Include(x => x.PricingPlan).Include(x => x.Prices)
            .FirstOrDefaultAsync(x => x.Id == versionId, ct)
            ?? throw new PlanServiceException("Version de plan introuvable.", 404);
        var plan = v.PricingPlan!;
        var audience = plan.Audience ?? PlanAudiences.Student;
        var values = await _entitlements.GetVersionValuesAsync(versionId, ct);
        var studentFree = await _entitlements.GetFreeValuesAsync(PlanAudiences.Student, ct);
        var freeRef = Math.Max(1, EntitlementCatalog.AsLong(studentFree[EntitlementCatalog.AiReferenceTokens]));
        var features = EntitlementCatalog.ForAudience(audience)
            .Where(d => d.ShowOnPricing)
            .Select(d =>
            {
                var val = values.TryGetValue(d.Key, out var x) ? x : d.DefaultValue;
                var included = d.Type != EntitlementType.Boolean || EntitlementCatalog.AsBool(val);
                return new PlanFeatureDto(d.Key, d.Label, d.Group ?? "", val, EntitlementCatalog.Display(d.Key, val, freeRef), included);
            }).ToList();
        var mult = (int)Math.Max(1, Math.Round((double)EntitlementCatalog.AsLong(values[EntitlementCatalog.AiReferenceTokens]) / freeRef));
        return new PlanCardDto(plan.Id, plan.PlanKey, plan.Name, plan.Description, audience, plan.IsFreeTier, plan.IsPopular,
            v.Id, v.VersionNumber,
            v.Prices.OrderBy(p => BillingPeriods.Months(p.Period)).Select(p => new PlanPriceDto(p.Period, p.PriceTtc)).ToList(),
            mult, features);
    }

    // ── Familles ────────────────────────────────────────────────────────────

    public async Task<PricingPlan> CreateFamilyAsync(CreatePlanFamilyRequest req, int actorId, CancellationToken ct = default)
    {
        var audience = req.Audience?.Trim().ToLowerInvariant();
        if (!PlanAudiences.IsValid(audience)) throw new PlanServiceException("Public invalide (student, parent ou teacher).");
        var key = req.PlanKey?.Trim().ToLowerInvariant();
        if (key == null || !KeyPattern.IsMatch(key)) throw new PlanServiceException("Clé invalide : lettres minuscules, chiffres et tirets (2 à 49 caractères).");
        var name = req.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100) throw new PlanServiceException("Nom obligatoire (100 caractères au plus).");
        if (await _db.PricingPlans.IgnoreQueryFilters().AnyAsync(p => p.PlanKey == key, ct))
            throw new PlanServiceException("Cette clé de plan existe déjà.", 409);
        if (req.IsFreeTier && await _db.PricingPlans.AnyAsync(p => p.Audience == audience && p.IsFreeTier, ct))
            throw new PlanServiceException("Ce public a déjà un plan gratuit actif.", 409);

        var now = DateTime.UtcNow;
        var plan = new PricingPlan
        {
            Name = name,
            Description = req.Description?.Trim(),
            Category = PlanAudiences.LegacyCategory(audience!),
            Audience = audience,
            PlanKey = key,
            IsFreeTier = req.IsFreeTier,
            IsPopular = req.IsPopular,
            SortOrder = req.SortOrder,
            Price = 0,
            Currency = "XAF",
            Period = "/mois",
            CreatedAt = now,
        };
        var free = await _entitlements.GetFreeValuesAsync(audience!, ct);
        var draft = new PlanVersion { VersionNumber = 1, Status = PlanVersionStatus.Draft, CreatedAt = now, CreatedByUserId = actorId };
        foreach (var def in EntitlementCatalog.ForAudience(audience!))
            draft.Entitlements.Add(new PlanEntitlement { Key = def.Key, Value = free[def.Key] });
        plan.Versions.Add(draft);
        _db.PricingPlans.Add(plan);
        await _db.SaveChangesAsync(ct);
        return plan;
    }

    public async Task<PricingPlan> UpdateFamilyAsync(int id, UpdatePlanFamilyRequest req, CancellationToken ct = default)
    {
        var plan = await _db.PricingPlans.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id && p.Audience != null, ct)
            ?? throw new PlanServiceException("Plan introuvable.", 404);
        if (req.Name != null)
        {
            var name = req.Name.Trim();
            if (name.Length is 0 or > 100) throw new PlanServiceException("Nom obligatoire (100 caractères au plus).");
            plan.Name = name;
        }
        if (req.Description != null) plan.Description = req.Description.Trim();
        if (req.IsPopular.HasValue) plan.IsPopular = req.IsPopular.Value;
        if (req.SortOrder.HasValue) plan.SortOrder = req.SortOrder.Value;
        plan.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _entitlements.Invalidate();
        return plan;
    }

    /// <summary>Archive (retire de la vente) ou rétablit une famille. Les abonnés en cours gardent leurs droits.</summary>
    public async Task<PricingPlan> SetArchivedAsync(int id, bool archived, CancellationToken ct = default)
    {
        var plan = await _db.PricingPlans.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id && p.Audience != null, ct)
            ?? throw new PlanServiceException("Plan introuvable.", 404);
        if (archived && plan.IsFreeTier)
            throw new PlanServiceException("Le plan gratuit d'un public ne peut pas être archivé : il fonde les droits de tous les comptes sans abonnement.");
        if (!archived && plan.IsFreeTier && await _db.PricingPlans.AnyAsync(p => p.Id != id && p.Audience == plan.Audience && p.IsFreeTier, ct))
            throw new PlanServiceException("Ce public a déjà un plan gratuit actif.", 409);
        plan.IsDeleted = archived;
        plan.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _entitlements.Invalidate();
        return plan;
    }

    // ── Versions ────────────────────────────────────────────────────────────

    /// <summary>Ouvre un brouillon (un seul par famille), copié de la version publiée ou de la dernière.</summary>
    public async Task<PlanVersion> CreateDraftAsync(int planId, int actorId, CancellationToken ct = default)
    {
        var plan = await _db.PricingPlans.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == planId && p.Audience != null, ct)
            ?? throw new PlanServiceException("Plan introuvable.", 404);
        var existing = await _db.PlanVersions.Include(v => v.Prices).Include(v => v.Entitlements)
            .FirstOrDefaultAsync(v => v.PricingPlanId == planId && v.Status == PlanVersionStatus.Draft, ct);
        if (existing != null) return existing;

        var source = await _db.PlanVersions.AsNoTracking().Include(v => v.Prices).Include(v => v.Entitlements)
            .Where(v => v.PricingPlanId == planId)
            .OrderByDescending(v => v.Status == PlanVersionStatus.Published).ThenByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);
        var next = (await _db.PlanVersions.Where(v => v.PricingPlanId == planId).MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0) + 1;
        var draft = new PlanVersion
        {
            PricingPlanId = planId,
            VersionNumber = next,
            Status = PlanVersionStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = actorId,
        };
        if (source != null)
        {
            foreach (var p in source.Prices) draft.Prices.Add(new PlanPrice { Period = p.Period, PriceTtc = p.PriceTtc });
            foreach (var e in source.Entitlements) draft.Entitlements.Add(new PlanEntitlement { Key = e.Key, Value = e.Value });
        }
        else
        {
            var free = await _entitlements.GetFreeValuesAsync(plan.Audience!, ct);
            foreach (var def in EntitlementCatalog.ForAudience(plan.Audience!))
                draft.Entitlements.Add(new PlanEntitlement { Key = def.Key, Value = free[def.Key] });
        }
        _db.PlanVersions.Add(draft);
        await _db.SaveChangesAsync(ct);
        return draft;
    }

    public async Task<PlanVersion> UpdateDraftAsync(int versionId, UpdatePlanVersionRequest req, CancellationToken ct = default)
    {
        var v = await _db.PlanVersions.Include(x => x.Prices).Include(x => x.Entitlements).Include(x => x.PricingPlan)
            .FirstOrDefaultAsync(x => x.Id == versionId, ct)
            ?? throw new PlanServiceException("Version introuvable.", 404);
        if (v.Status != PlanVersionStatus.Draft)
            throw new PlanServiceException("Seul un brouillon est modifiable : ouvrez une nouvelle version.", 409);
        var audience = v.PricingPlan!.Audience!;

        if (req.Notes != null) v.Notes = req.Notes.Trim().Length > 1000 ? req.Notes.Trim()[..1000] : req.Notes.Trim();

        if (req.Prices != null)
        {
            foreach (var (period, price) in req.Prices)
            {
                var p = period?.Trim().ToLowerInvariant();
                if (!BillingPeriods.IsValid(p)) throw new PlanServiceException($"Période inconnue : {period}.");
                var row = v.Prices.FirstOrDefault(x => x.Period == p);
                if (price is null)
                {
                    if (row != null) { v.Prices.Remove(row); _db.PlanPrices.Remove(row); }
                    continue;
                }
                if (price < 0 || price > 100_000_000 || price != Math.Round(price.Value))
                    throw new PlanServiceException("Prix TTC : montant entier en XAF, positif.");
                if (v.PricingPlan.IsFreeTier && price > 0)
                    throw new PlanServiceException("Un plan gratuit n'a pas de prix.");
                if (row == null) v.Prices.Add(new PlanPrice { Period = p!, PriceTtc = price.Value });
                else row.PriceTtc = price.Value;
            }
        }

        if (req.Entitlements != null)
        {
            foreach (var (key, raw) in req.Entitlements)
            {
                var def = EntitlementCatalog.Find(key) ?? throw new PlanServiceException($"Droit inconnu : {key}.");
                if (!def.Audiences.Contains(audience))
                    throw new PlanServiceException($"Le droit « {def.Label} » ne concerne pas ce public.");
                var canon = EntitlementCatalog.Normalize(key, raw, out var error) ?? throw new PlanServiceException(error!);
                var row = v.Entitlements.FirstOrDefault(x => x.Key == key);
                if (row == null) v.Entitlements.Add(new PlanEntitlement { Key = key, Value = canon });
                else row.Value = canon;
            }
        }

        await _db.SaveChangesAsync(ct);
        return v;
    }

    public async Task DiscardDraftAsync(int versionId, CancellationToken ct = default)
    {
        var v = await _db.PlanVersions.FirstOrDefaultAsync(x => x.Id == versionId, ct)
            ?? throw new PlanServiceException("Version introuvable.", 404);
        if (v.Status != PlanVersionStatus.Draft) throw new PlanServiceException("Seul un brouillon peut être abandonné.", 409);
        _db.PlanVersions.Remove(v);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Vrai si <paramref name="candidate"/> n'est pour aucun abonné moins
    /// favorable que <paramref name="current"/> : aucun prix en hausse sur une
    /// période proposée des deux côtés, aucun droit en baisse (18.12).
    /// </summary>
    public static bool IsImprovement(PlanVersion current, PlanVersion candidate)
    {
        foreach (var p in current.Prices)
        {
            var n = candidate.Prices.FirstOrDefault(x => x.Period == p.Period);
            if (n != null && n.PriceTtc > p.PriceTtc) return false;
        }
        var keys = current.Entitlements.Select(e => e.Key).Union(candidate.Entitlements.Select(e => e.Key));
        foreach (var key in keys)
        {
            var c = current.Entitlements.FirstOrDefault(e => e.Key == key)?.Value;
            var n = candidate.Entitlements.FirstOrDefault(e => e.Key == key)?.Value;
            if (EntitlementCatalog.Compare(key, c, n) < 0) return false;
        }
        return true;
    }

    /// <summary>
    /// Publie un brouillon (18.12) : l'ancienne version publiée est archivée.
    /// Amélioration → les abonnés en cours basculent tout de suite ; hausse de
    /// prix ou baisse de droits → ils gardent leur version jusqu'à leur
    /// premier renouvellement après le préavis, et sont prévenus.
    /// </summary>
    public async Task<PublishResult> PublishAsync(int versionId, int actorId, CancellationToken ct = default)
    {
        var v = await _db.PlanVersions.Include(x => x.Prices).Include(x => x.Entitlements).Include(x => x.PricingPlan)
            .FirstOrDefaultAsync(x => x.Id == versionId, ct)
            ?? throw new PlanServiceException("Version introuvable.", 404);
        if (v.Status != PlanVersionStatus.Draft) throw new PlanServiceException("Seul un brouillon peut être publié.", 409);
        var plan = v.PricingPlan!;
        if (!plan.IsFreeTier && !v.Prices.Any(p => p.PriceTtc > 0))
            throw new PlanServiceException("Un plan payant doit avoir au moins un prix TTC supérieur à zéro.");
        if (plan.IsFreeTier && v.Prices.Any(p => p.PriceTtc > 0))
            throw new PlanServiceException("Un plan gratuit n'a pas de prix.");
        foreach (var e in v.Entitlements)
            if (EntitlementCatalog.Normalize(e.Key, e.Value, out var error) == null) throw new PlanServiceException(error!);

        var now = DateTime.UtcNow;
        var previous = await _db.PlanVersions.Include(x => x.Prices).Include(x => x.Entitlements)
            .Where(x => x.PricingPlanId == plan.Id && x.Status == PlanVersionStatus.Published)
            .ToListAsync(ct);
        var current = previous.OrderByDescending(x => x.VersionNumber).FirstOrDefault();
        var improvement = current == null || IsImprovement(current, v);

        foreach (var p in previous)
        {
            p.Status = PlanVersionStatus.Archived;
            p.ArchivedAt = now;
        }
        v.Status = PlanVersionStatus.Published;
        v.PublishedAt = now;
        v.PublishedByUserId = actorId;
        v.IsImprovement = improvement;
        v.ExistingSubscribersFrom = improvement ? now : now.AddDays(ChangeNoticeDays);
        var monthly = v.Prices.FirstOrDefault(p => p.Period == BillingPeriods.Monthly)?.PriceTtc
                      ?? v.Prices.OrderBy(p => BillingPeriods.Months(p.Period)).FirstOrDefault()?.PriceTtc ?? 0;
        plan.Price = monthly;
        plan.UpdatedAt = now;

        var subscribers = await _db.Subscriptions
            .Where(s => s.PricingPlanId == plan.Id && s.PlanVersionId != null && s.PlanVersionId != v.Id)
            .Where(SubscriptionRules.InForce(now))
            .ToListAsync(ct);
        int moved = 0, notified = 0;
        if (improvement)
        {
            foreach (var s in subscribers) { s.PlanVersionId = v.Id; s.UpdatedAt = now; moved++; }
        }
        else
        {
            foreach (var s in subscribers)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = s.UserId,
                    Title = $"Évolution de votre plan {plan.Name}",
                    Message = $"Les conditions du plan {plan.Name} évoluent. Elles s'appliqueront à votre premier renouvellement "
                              + $"après le {v.ExistingSubscribersFrom:dd/MM/yyyy}. D'ici là, rien ne change pour vous.",
                    Type = "Subscription",
                    RelatedEntityType = "Subscription",
                    RelatedEntityId = s.Id,
                    CreatedAt = now,
                    User = null!,
                });
                notified++;
            }
        }

        await _db.SaveChangesAsync(ct);
        _entitlements.Invalidate();
        return new PublishResult(v.Id, improvement, v.ExistingSubscribersFrom.Value, moved, notified);
    }

    /// <summary>
    /// Version à appliquer au renouvellement d'un abonnement : la version
    /// publiée de sa famille si son préavis est échu à la date du
    /// renouvellement, sinon la version qu'il avait.
    /// </summary>
    public static async Task<PlanVersion?> VersionForRenewalAsync(ApplicationDbContext db, Subscription s, DateTime renewalAt, CancellationToken ct = default)
    {
        var published = await db.PlanVersions.Include(v => v.Prices)
            .Where(v => v.PricingPlanId == s.PricingPlanId && v.Status == PlanVersionStatus.Published)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);
        if (published != null && (published.ExistingSubscribersFrom ?? published.PublishedAt ?? renewalAt) <= renewalAt)
            return published;
        if (s.PlanVersionId is int id)
            return await db.PlanVersions.Include(v => v.Prices).FirstOrDefaultAsync(v => v.Id == id, ct);
        return published;
    }

    // ── Octroi manuel ───────────────────────────────────────────────────────

    /// <summary>
    /// Octroi manuel par l'administration (encaissement hors plateforme, geste
    /// commercial) : plan du public du compte uniquement, version publiée, un
    /// seul abonnement actif (l'abonnement du même plan est prolongé, un autre
    /// plan le remplace).
    /// </summary>
    public async Task<Subscription> GrantAsync(GrantPlanRequest req, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.UserId, ct)
            ?? throw new PlanServiceException("Utilisateur introuvable.", 404);
        var plan = await _db.PricingPlans.FirstOrDefaultAsync(p => p.Id == req.PlanId && p.Audience != null, ct)
            ?? throw new PlanServiceException("Plan introuvable ou archivé.", 404);
        var audience = PlanAudiences.ForRole(user.Role)
            ?? throw new PlanServiceException("Ce compte n'a pas de plan d'abonnement (les établissements passent par une licence).");
        if (plan.Audience != audience)
            throw new PlanServiceException($"Le plan {plan.Name} est réservé au public « {PlanAudiences.Label(plan.Audience)} » : ce compte est « {PlanAudiences.Label(audience)} ».");
        if (plan.IsFreeTier) throw new PlanServiceException("Le plan gratuit ne s'octroie pas : c'est l'état par défaut.");
        var version = await PublishedVersionAsync(plan.Id, ct)
            ?? throw new PlanServiceException("Ce plan n'a pas de version publiée.");
        var period = string.IsNullOrWhiteSpace(req.BillingPeriod) ? BillingPeriods.Monthly : req.BillingPeriod.Trim().ToLowerInvariant();
        if (!BillingPeriods.IsValid(period)) throw new PlanServiceException("Période invalide (monthly, quarterly ou yearly).");
        var months = Math.Clamp(req.Months ?? BillingPeriods.Months(period), 1, 36);

        var now = DateTime.UtcNow;
        var inForce = await _db.Subscriptions.Where(s => s.UserId == user.Id).Where(SubscriptionRules.InForce(now)).ToListAsync(ct);
        var same = inForce.FirstOrDefault(s => s.PricingPlanId == plan.Id);
        Subscription result;
        if (same != null)
        {
            var from = same.EndDate.HasValue && same.EndDate > now ? same.EndDate.Value : now;
            same.EndDate = from.AddMonths(months);
            same.PlanVersionId = version.Id;
            same.BillingPeriod = period;
            same.Audience = audience;
            same.PlanName = plan.Name;
            same.Status = SubscriptionStatuses.Active;
            same.GraceUntil = null;
            same.IsActive = true;
            same.RenewalCount += 1;
            same.UpdatedAt = now;
            result = same;
            foreach (var other in inForce.Where(s => s.Id != same.Id)) SubscriptionTermination.EndNow(other, now);
        }
        else
        {
            foreach (var other in inForce) SubscriptionTermination.EndNow(other, now);
            result = new Subscription
            {
                UserId = user.Id,
                PricingPlanId = plan.Id,
                PlanVersionId = version.Id,
                PlanName = plan.Name,
                BillingPeriod = period,
                Audience = audience,
                StartDate = now,
                EndDate = now.AddMonths(months),
                Status = SubscriptionStatuses.Active,
                IsActive = true,
                CreatedAt = now,
            };
            _db.Subscriptions.Add(result);
        }
        return result;
    }
}
