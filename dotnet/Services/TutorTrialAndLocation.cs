using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Utils;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>Refus d'une règle de réservation avec un code stable pour les clients (400 {success:false, code, message}).</summary>
public sealed class TutorBookingRuleException : InvalidOperationException
{
    public string Code { get; }
    public TutorBookingRuleException(string code, string message) : base(message) { Code = code; }
}

/// <summary>
/// Séance d'essai (T7, validée par Georges le 27/11/2026). Le tuteur la propose (TrialSessionEnabled) à son prix
/// d'essai (TrialSessionPriceXaf, null ou 0 = gratuite), sans modulation ni dégressivité. Conditions : séance isolée,
/// individuelle, 60 minutes au plus, et aucune réservation antérieure (hors annulée, refusée, expirée) avec ce tuteur,
/// ni pour l'élève ni pour le payeur.
/// </summary>
public static class TutorTrial
{
    public const int MaxMinutes = 60;
    public const string FreePaymentMethod = "free";
    public const string NotRequiredPaymentStatus = "not_required";
    private static readonly string[] NotCounted = { "cancelled", "rejected", "expired" };

    public static async Task<bool> HasHistoryAsync(ApplicationDbContext db, int tutorProfileId, IReadOnlyCollection<int> studentIds, int? payerUserId)
    {
        var ids = studentIds.ToList();
        return await db.TutorBookings.AsNoTracking().AnyAsync(b =>
            b.TutorProfileId == tutorProfileId && !NotCounted.Contains(b.Status)
            && (ids.Contains(b.StudentUserId) || b.Participants.Any(p => ids.Contains(p.StudentUserId))
                || (payerUserId != null && (b.PayerUserId == payerUserId || b.BookedByUserId == payerUserId))));
    }

    /// <summary>Contrôles indépendants de l'appelant (devis comme réservation).</summary>
    public static void EnsureOffered(TutorProfile profile, int participantCount, int durationMinutes)
    {
        if (!profile.TrialSessionEnabled)
            throw new TutorBookingRuleException("essai_non_disponible", "Ce répétiteur ne propose pas de séance d'essai.");
        if (participantCount != 1)
            throw new TutorBookingRuleException("essai_non_disponible", "La séance d'essai est individuelle : un seul élève.");
        if (durationMinutes > MaxMinutes)
            throw new TutorBookingRuleException("essai_duree", $"La séance d'essai dure {MaxMinutes} minutes au plus.");
    }

    /// <summary>Prix d'essai appliqué au devis : total et prix par élève remplacés, prix normal gardé pour l'affichage.</summary>
    public static void Apply(TutorPriceQuoteDto quote, decimal? trialPriceXaf)
    {
        var price = Math.Max(0, trialPriceXaf ?? 0);
        quote.RegularTotalXaf = quote.TotalXaf;
        quote.IsTrial = true;
        quote.TrialLabel = price == 0 ? "Séance d'essai gratuite" : "Séance d'essai";
        quote.TotalXaf = price;
        quote.PerStudentXaf = price;
    }
}

/// <summary>
/// Lieu d'une séance en présentiel (T8, validé par Georges le 27/11/2026) : exigé au domicile de l'élève et dans un
/// lieu neutre ; au domicile du tuteur, l'indication de son profil est reprise si rien n'est envoyé ; ignoré en ligne.
/// Le filtre des coordonnées s'applique (un téléphone, un e-mail ou un lien restent refusés ; une adresse passe).
/// </summary>
public static class TutorSessionLocation
{
    public const int MaxLength = 200;

    private static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        return t.Length <= MaxLength ? t : t[..MaxLength];
    }

    public static (string? Address, string? Note) Resolve(TutorProfile profile, string mode, string? address, string? note)
    {
        if (mode == "online") return (null, null);
        var a = Clean(address);
        var n = Clean(note);
        if (mode is "student_home" or "neutral_place" && a == null)
            throw new TutorBookingRuleException("lieu_requis", "Indique le lieu de la séance : quartier, rue et un repère.");
        if (mode == "tutor_home" && a == null)
            a = Clean(profile.TutorHomeAddressHint);
        if (ContentModeration.Check(a, n) is { } refus)
            throw new TutorBookingRuleException(refus.Code, refus.Message);
        return (a, n);
    }
}
