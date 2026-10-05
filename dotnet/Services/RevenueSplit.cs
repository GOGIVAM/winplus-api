using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Décomposition d'un montant tutorat en part enseignant et part plateforme
/// (Module 19, décision §4.E), partagée par le journal de portefeuille
/// (Module 1) et les écrans de revenus, pour que les deux annoncent toujours
/// exactement le même net pour la même séance.
///
/// Le XAF n'a pas de sous-unité : seule la commission est arrondie, le net
/// s'obtient par soustraction, de sorte que net + commission = prix payé.
/// </summary>
public static class RevenueSplit
{
    /// <summary>Part enseignant appliquée faute d'abonnement actif (comportement historique).</summary>
    public const decimal DefaultTeacherShare = 0.80m;

    public static decimal CommissionXaf(decimal grossXaf, decimal revenueShare) =>
        Math.Round(grossXaf * (1 - revenueShare), 0, MidpointRounding.AwayFromZero);

    public static decimal NetXaf(decimal grossXaf, decimal revenueShare) =>
        grossXaf - CommissionXaf(grossXaf, revenueShare);

    /// <summary>Arrondi XAF à zéro décimale (décision §4.E).</summary>
    public static decimal Xaf(decimal amount) =>
        Math.Round(amount, 0, MidpointRounding.AwayFromZero);

    /// <summary>Part enseignant lue sur le plan d'abonnement actif du professeur (70/75/80 %).</summary>
    public static async Task<decimal> GetTeacherShareAsync(ApplicationDbContext db, int teacherId) =>
        await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == teacherId && s.Status == "active" && !s.IsDeleted)
            .OrderByDescending(s => s.StartDate)
            .Select(s => s.PricingPlan != null ? s.PricingPlan.TeacherRevenueShare : null)
            .FirstOrDefaultAsync() ?? DefaultTeacherShare;
}
