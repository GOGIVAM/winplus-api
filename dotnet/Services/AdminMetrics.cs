using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Indicateurs de l'administration calculés en base (lot 9.7a, suivi 18.1),
/// partagés par la vue globale, l'écran Revenus et l'écran Analytics pour
/// qu'un même chiffre ait partout la même définition.
/// </summary>
public static class AdminMetrics
{
    /// <summary>Statuts d'une commande encaissée : « completed » (Mobile Money, solde) et « paid » (portefeuille parent).</summary>
    public static readonly string[] CompletedOrderStatuses = { "completed", "paid" };

    /// <summary>
    /// Taux de conversion de la période [from, to[ : parmi les comptes inscrits
    /// sur la période, part de ceux qui ont passé au moins une commande
    /// encaissée sur la même période. Une seule requête SQL par terme ; aucune
    /// liste chargée en mémoire (l'ancien calcul lisait 100 commandes et 100
    /// comptes au plus, toutes commandes confondues).
    /// </summary>
    public static async Task<(int Registered, int Buyers, double? Rate)> ConversionAsync(ApplicationDbContext db, DateTime from, DateTime to)
    {
        var signups = db.Users.AsNoTracking().Where(u => !u.IsDeleted && u.Role != "admin" && u.CreatedAt >= from && u.CreatedAt < to);
        var registered = await signups.CountAsync();
        var buyers = await signups
            .Where(u => db.Orders.Any(o => o.UserId == u.Id && !o.IsDeleted
                && CompletedOrderStatuses.Contains(o.Status.ToLower())
                && o.CreatedAt >= from && o.CreatedAt < to))
            .CountAsync();
        double? rate = registered > 0 ? Math.Round(buyers * 100.0 / registered, 2) : null;
        return (registered, buyers, rate);
    }
}
