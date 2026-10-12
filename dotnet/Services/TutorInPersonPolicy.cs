using Backend.Models.Entities;

namespace Backend.Services;

/// <summary>
/// Règle unique du présentiel (lot 9.5, suivi 17.11 et 17.12) : un tuteur ne
/// propose et ne reçoit des séances à domicile, chez lui ou en lieu neutre que
/// s'il est vérifié pour le présentiel (une voie valide, voir
/// TutorVerificationService) ET que son présentiel n'est pas suspendu (seuil
/// de signalements). Sinon : en ligne uniquement. Appliquée côté serveur au
/// devis, à la réservation, à l'engagement mensuel (création et
/// renouvellement), aux réglages du tuteur, à la recherche et à l'affichage
/// public des modes.
/// </summary>
public static class TutorInPersonPolicy
{
    public const string Online = "online";
    public static readonly string[] InPersonModes = { "student_home", "tutor_home", "neutral_place" };

    public static bool IsInPerson(string? mode) => mode != null && InPersonModes.Contains(mode);

    public static bool IsVerified(TutorProfile p) => p.InPersonVerifiedAt != null;
    public static bool IsSuspended(TutorProfile p) => p.InPersonSuspendedAt != null;

    /// <summary>Présentiel possible maintenant.</summary>
    public static bool IsEligible(TutorProfile p) => IsVerified(p) && !IsSuspended(p);

    /// <summary>Message clair quand le présentiel n'est pas possible, sinon null.</summary>
    public static string? BlockReason(TutorProfile p)
    {
        if (IsSuspended(p))
            return "Le présentiel de ce répétiteur est suspendu en attendant l'examen d'un signalement. Les séances en ligne restent possibles.";
        if (!IsVerified(p))
            return "Ce répétiteur n'est pas encore vérifié pour le présentiel : seules les séances en ligne sont possibles.";
        return null;
    }

    /// <summary>Refuse un mode présentiel avec un tuteur non éligible (réservation, devis, engagement).</summary>
    public static void EnsureModeAllowed(TutorProfile p, string mode)
    {
        if (!IsInPerson(mode)) return;
        var reason = BlockReason(p);
        if (reason != null) throw new InvalidOperationException(reason);
    }

    // ── Modes effectivement proposés (affichage public, recherche) ──
    public static bool OffersAtStudentHome(TutorProfile p) => p.OffersAtStudentHome && IsEligible(p);
    public static bool OffersAtTutorHome(TutorProfile p) => p.OffersAtTutorHome && IsEligible(p);
    public static bool OffersNeutralPlace(TutorProfile p) => p.OffersNeutralPlace && IsEligible(p);
    public static bool OffersInPerson(TutorProfile p) =>
        OffersAtStudentHome(p) || OffersAtTutorHome(p) || OffersNeutralPlace(p);
}
