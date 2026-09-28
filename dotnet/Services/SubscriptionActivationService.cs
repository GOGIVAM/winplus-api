using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Intention d'abonnement portée par une commande (Module 18, correction §7.3
/// du suivi).
///
/// Une commande d'abonnement n'a pas de ligne <c>OrderItem</c> : elle ne porte
/// aucun contenu. Le plan souscrit et la période choisie étaient donc encodés
/// en texte libre dans <c>Order.Notes</c> (« subscription:{planId}:{billing} »),
/// écrits d'un côté et jamais relus de l'autre c'est la cause racine du
/// point 3.1.11 : le paiement aboutissait sans qu'aucune ligne
/// <c>Subscriptions</c> ne soit créée.
///
/// La donnée reste portée par <c>Order.Notes</c> : la rendre structurée au sens
/// du schéma demanderait deux colonnes de plus sur <c>orders</c>, donc une
/// migration appliquée en base, ce que cet environnement ne permet pas de
/// vérifier (voir la limite signalée dans le rapport). Le format est en
/// revanche devenu un type à part entière, avec un écrivain et un lecteur
/// uniques et stricts : plus aucune chaîne n'est composée ni découpée à la
/// main ailleurs dans le code.
/// </summary>
public readonly record struct SubscriptionOrderIntent(int PricingPlanId, string Billing)
{
    private const string Prefix = "subscription:";

    /// <summary>Périodes de facturation acceptées, et leur durée en mois.</summary>
    private static readonly Dictionary<string, int> BillingDurations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["monthly"] = 1,
        ["quarterly"] = 3,
        ["yearly"] = 12,
    };

    /// <summary>
    /// Vrai si la période demandée est connue. Une période inconnue est
    /// refusée à l'écriture plutôt que silencieusement ramenée au mois : sans
    /// ça, « yearlyy » facturait l'année et n'ouvrait qu'un mois.
    /// </summary>
    public static bool IsKnownBilling(string? billing) =>
        billing != null && BillingDurations.ContainsKey(billing);

    /// <summary>Durée en mois de la période de facturation.</summary>
    public int DurationMonths => BillingDurations[Billing];

    /// <summary>Forme stockée dans <c>Order.Notes</c>.</summary>
    public override string ToString() => $"{Prefix}{PricingPlanId}:{Billing.ToLowerInvariant()}";

    /// <summary>
    /// Relit l'intention depuis <c>Order.Notes</c>. Tolère les notes absentes,
    /// d'un autre type de commande, ou mal formées : dans tous ces cas la
    /// commande n'est simplement pas une commande d'abonnement.
    /// </summary>
    public static bool TryParse(string? notes, out SubscriptionOrderIntent intent)
    {
        intent = default;
        if (string.IsNullOrWhiteSpace(notes)) return false;

        var trimmed = notes.Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var parts = trimmed[Prefix.Length..].Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 1) return false;

        if (!int.TryParse(parts[0], out var planId) || planId <= 0) return false;

        // Période absente ou inconnue : le mensuel est le défaut historique du
        // DTO d'achat, donc la seule valeur qui ne change pas le comportement
        // d'une commande déjà créée sans période explicite.
        var billing = parts.Length >= 2 && IsKnownBilling(parts[1])
            ? parts[1].ToLowerInvariant()
            : "monthly";

        intent = new SubscriptionOrderIntent(planId, billing);
        return true;
    }
}

/// <summary>
/// Fin anticipée d'un abonnement : résiliation à la date du jour, avec les
/// mêmes écritures que le remplacement d'un abonnement actif par une nouvelle
/// activation (ci-dessous). Partagée avec l'approbation d'un remboursement
/// (décision 10.11, AdminRefundsController) pour qu'il n'existe qu'une seule
/// façon de couper un abonnement avant son terme.
///
/// <c>IsActive</c> est remis à false en plus du statut : la règle d'accès
/// (<see cref="ContentAccessService.HasActiveSubscriptionAsync"/>) exige les
/// deux, et SubscriptionExpirationService ne touche que le statut.
/// </summary>
public static class SubscriptionTermination
{
    public const string CancelledStatus = "cancelled";

    public static void EndNow(Subscription subscription, DateTime now)
    {
        subscription.Status = CancelledStatus;
        subscription.EndDate = now;
        subscription.IsActive = false;
        subscription.UpdatedAt = now;
    }
}

public interface ISubscriptionActivationService
{
    /// <summary>
    /// Crée l'abonnement correspondant à une commande d'abonnement dont le
    /// paiement vient d'être confirmé. Sans effet (et sans erreur) si la
    /// commande n'est pas une commande d'abonnement, si elle n'est pas payée,
    /// ou si son abonnement a déjà été créé.
    /// </summary>
    /// <returns>L'abonnement créé, ou null si rien n'était à faire.</returns>
    Task<Subscription?> ActivateFromOrderAsync(int orderId);
}

public class SubscriptionActivationService : ISubscriptionActivationService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SubscriptionActivationService> _logger;

    /// <summary>
    /// Statuts de commande qui valent « encaissement acquis ». Même liste, et
    /// même insensibilité à la casse, que <see cref="ContentAccessService"/> :
    /// les lignes héritées portent des casses mixtes.
    /// </summary>
    private static readonly string[] PaidOrderStatuses = { "completed", "paid" };

    public SubscriptionActivationService(ApplicationDbContext db, ILogger<SubscriptionActivationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Subscription?> ActivateFromOrderAsync(int orderId)
    {
        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
        {
            _logger.LogWarning("Activation d'abonnement demandée pour la commande {OrderId} introuvable", orderId);
            return null;
        }

        if (!SubscriptionOrderIntent.TryParse(order.Notes, out var intent))
            return null; // Commande de contenu ordinaire : rien à faire.

        if (!order.UserId.HasValue)
        {
            // Un abonnement se rattache à un compte : une commande invité ne
            // peut pas en produire. Le cas n'existe pas aujourd'hui
            // (POST /subscriptions/purchase exige un jeton) mais le signaler
            // vaut mieux que créer une ligne orpheline.
            _logger.LogError("Commande d'abonnement {OrderId} sans utilisateur : abonnement non créé", orderId);
            return null;
        }

        if (!PaidOrderStatuses.Contains(order.Status.ToLower()))
        {
            _logger.LogWarning(
                "Activation d'abonnement refusée pour la commande {OrderId} : statut {Status} non payé",
                orderId, order.Status);
            return null;
        }

        var userId = order.UserId.Value;

        var plan = await _db.PricingPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == intent.PricingPlanId);
        if (plan == null)
        {
            // Le paiement est encaissé et le plan a disparu entre-temps :
            // impossible de deviner la durée ni le palier, l'incident doit
            // être visible plutôt qu'avalé.
            _logger.LogError(
                "Commande d'abonnement {OrderId} payée mais plan {PlanId} introuvable : abonnement non créé",
                orderId, intent.PricingPlanId);
            return null;
        }

        // Idempotence. Le webhook NotchPay est rejouable, et le chemin de
        // synchronisation par consultation de statut confirme la même
        // commande une seconde fois : sans cette garde, un même paiement
        // empilait plusieurs abonnements. La marque d'unicité est l'abonnement
        // déjà actif sur ce plan, la table Subscriptions ne portant pas de
        // référence de commande (cf. limite du format d'intention ci-dessus).
        //
        // Limite connue de cette heuristique : deux commandes distinctes du
        // même plan payées presque simultanément ne produisent qu'un seul
        // abonnement, la seconde étant prise pour un rejeu. Le cas est tracé
        // en journal ; le lever proprement demande une référence de commande
        // sur Subscriptions, donc une migration de schéma.
        var now = DateTime.UtcNow;
        var alreadyActivated = await _db.Subscriptions.AnyAsync(s =>
            s.UserId == userId
            && s.PricingPlanId == intent.PricingPlanId
            && !s.IsDeleted
            && s.Status.ToLower() == "active"
            && s.StartDate >= order.CreatedAt.AddMinutes(-1));

        if (alreadyActivated)
        {
            _logger.LogWarning(
                "Abonnement déjà actif sur le plan {PlanId} pour l'utilisateur {UserId} depuis la création de la "
                + "commande {OrderId} : activation ignorée (rejeu de confirmation, ou seconde commande du même plan)",
                intent.PricingPlanId, userId, orderId);
            return null;
        }

        // Un seul abonnement actif à la fois : l'ancien est résilié à la date
        // du jour, comme le faisait déjà la souscription directe.
        var existing = await _db.Subscriptions
            .Where(s => s.UserId == userId && s.Status.ToLower() == "active" && !s.IsDeleted)
            .ToListAsync();

        foreach (var previous in existing)
            SubscriptionTermination.EndNow(previous, now);

        var subscription = new Subscription
        {
            UserId = userId,
            PricingPlanId = intent.PricingPlanId,
            PlanName = plan.Name,
            StartDate = now,
            EndDate = now.AddMonths(intent.DurationMonths),
            Status = "active",
            IsActive = true,
            // Renouvellement = nouvelle période payée sur le même plan ; un
            // changement de plan repart de zéro.
            RenewalCount = (existing.FirstOrDefault(s => s.PricingPlanId == intent.PricingPlanId)?.RenewalCount ?? -1) + 1,
            CreatedAt = now,
        };

        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Abonnement {SubscriptionId} créé pour l'utilisateur {UserId} (plan {PlanId}, {Billing}, fin {EndDate:u}) " +
            "à la confirmation de la commande {OrderId}",
            subscription.Id, userId, intent.PricingPlanId, intent.Billing, subscription.EndDate, orderId);

        return subscription;
    }
}
