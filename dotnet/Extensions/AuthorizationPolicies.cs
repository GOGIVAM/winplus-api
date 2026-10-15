namespace Backend.Extensions;

/// <summary>Noms des politiques d'autorisation déclarées dans Program.cs.</summary>
public static class AuthorizationPolicies
{
    /// <summary>
    /// Lot 9.7b (18.13) : gestion des plans, des promotions d'abonnement et des
    /// licences. Ouverte à l'administrateur et au rôle dédié
    /// <see cref="Roles.SubscriptionManager"/>.
    /// </summary>
    public const string SubscriptionManagement = "SubscriptionManagement";

    public static class Roles
    {
        /// <summary>Rôle « gestion des abonnements » (18.13) : console limitée aux plans, promotions et licences.</summary>
        public const string SubscriptionManager = "subscription_manager";
    }
}
