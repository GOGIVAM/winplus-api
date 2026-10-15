using Backend.Data;
using Backend.Services.Entitlements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Backend.Services;

/// <summary>
/// Décomposition d'un montant tutorat en part enseignant et part plateforme
/// (Module 19, décision §4.E), partagée par le journal de portefeuille
/// (Module 1) et les écrans de revenus, pour que les deux annoncent toujours
/// exactement le même net pour la même séance.
///
/// Le XAF n'a pas de sous-unité : seule la commission est arrondie, le net
/// s'obtient par soustraction, de sorte que net + commission = prix payé.
///
/// Lot 9.7b (18.15, 18.21) : la part vient du droit <c>teacher.revenue_share</c>
/// (Gratuit 70 / Fondateur 70 / Pro 75 / Expert 80 %, siège enseignant d'une
/// licence = au moins 75 %), et non plus d'un plan lu par nom. Sans abonnement
/// payant, c'est le palier le plus bas (plan gratuit), et non plus 80 %. Elle
/// est <b>figée au paiement</b> sur la réservation ou l'inscription.
/// </summary>
public static class RevenueSplit
{
    private static readonly IMemoryCache NoCache = new MemoryCache(new MemoryCacheOptions());

    public static decimal CommissionXaf(decimal grossXaf, decimal revenueShare) =>
        Math.Round(grossXaf * (1 - revenueShare), 0, MidpointRounding.AwayFromZero);

    public static decimal NetXaf(decimal grossXaf, decimal revenueShare) =>
        grossXaf - CommissionXaf(grossXaf, revenueShare);

    /// <summary>Arrondi XAF à zéro décimale (décision §4.E).</summary>
    public static decimal Xaf(decimal amount) =>
        Math.Round(amount, 0, MidpointRounding.AwayFromZero);

    /// <summary>Part enseignant actuelle (droits effectifs, sans cache).</summary>
    public static async Task<decimal> GetTeacherShareAsync(ApplicationDbContext db, int teacherId)
    {
        var ent = await new EntitlementService(db, NoCache).ComputeAsync(teacherId);
        return ent.Fraction(EntitlementCatalog.TeacherRevenueShare);
    }

    /// <summary>
    /// Part figée d'une séance : celle enregistrée au paiement ; à défaut
    /// (première observation du paiement), la part actuelle, aussitôt figée.
    /// </summary>
    public static async Task<decimal> FrozenBookingShareAsync(ApplicationDbContext db, int bookingId, int teacherId)
    {
        var booking = await db.TutorBookings.FirstOrDefaultAsync(b => b.Id == bookingId);
        if (booking?.TeacherShareAtPayment is decimal frozen) return frozen;
        var share = await GetTeacherShareAsync(db, teacherId);
        if (booking != null)
        {
            booking.TeacherShareAtPayment = share;
            await db.SaveChangesAsync();
        }
        return share;
    }

    /// <summary>Même règle pour l'inscription payante à une session.</summary>
    public static async Task<decimal> FrozenSessionShareAsync(ApplicationDbContext db, int sessionEnrollmentId, int teacherId)
    {
        var enrollment = await db.SessionEnrollments.FirstOrDefaultAsync(e => e.Id == sessionEnrollmentId);
        if (enrollment?.TeacherShareAtPayment is decimal frozen) return frozen;
        var share = await GetTeacherShareAsync(db, teacherId);
        if (enrollment != null)
        {
            enrollment.TeacherShareAtPayment = share;
            await db.SaveChangesAsync();
        }
        return share;
    }
}
