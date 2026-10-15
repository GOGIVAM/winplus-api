using System.Linq.Expressions;
using Backend.Models.Entities;

namespace Backend.Services.Entitlements;

/// <summary>Statuts d'un abonnement (VARCHAR, 9 caractères au plus).</summary>
public static class SubscriptionStatuses
{
    public const string Active = "active";
    /// <summary>Essai gratuit ouvert par une promotion (18.14).</summary>
    public const string Trialing = "trialing";
    /// <summary>Renouvellement par le solde échoué : accès maintenu jusqu'à GraceUntil (18.15).</summary>
    public const string PastDue = "past_due";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";
}

/// <summary>
/// <b>Seule définition d'« abonnement en vigueur »</b> du lot 9.7b (l'audit en
/// recensait sept). Un abonnement est en vigueur s'il n'est ni supprimé ni
/// désactivé et :
///   - actif ou en essai, et pas encore arrivé à échéance ;
///   - ou en défaut de paiement, tant que la grâce de 3 jours court.
/// Une échéance nulle (lignes antérieures au 9.7b) vaut « sans échéance ».
/// Comparaison de statut insensible à la casse (lignes héritées « Active »).
/// </summary>
public static class SubscriptionRules
{
    public static Expression<Func<Subscription, bool>> InForce(DateTime now) => s =>
        !s.IsDeleted && s.IsActive &&
        (((s.Status.ToLower() == SubscriptionStatuses.Active || s.Status.ToLower() == SubscriptionStatuses.Trialing)
              && (s.EndDate == null || s.EndDate > now))
         || (s.Status.ToLower() == SubscriptionStatuses.PastDue && s.GraceUntil != null && s.GraceUntil > now));

    /// <summary>Même règle, évaluée en mémoire (doit rester identique à <see cref="InForce"/>).</summary>
    public static bool IsInForce(Subscription s, DateTime now)
    {
        if (s.IsDeleted || !s.IsActive) return false;
        var status = s.Status?.ToLowerInvariant();
        if (status is SubscriptionStatuses.Active or SubscriptionStatuses.Trialing)
            return s.EndDate == null || s.EndDate > now;
        return status == SubscriptionStatuses.PastDue && s.GraceUntil != null && s.GraceUntil > now;
    }

    /// <summary>Statuts qui occupent la place « un seul abonnement actif par compte ».</summary>
    public static readonly string[] OccupyingStatuses =
        { SubscriptionStatuses.Active, SubscriptionStatuses.Trialing, SubscriptionStatuses.PastDue };
}
