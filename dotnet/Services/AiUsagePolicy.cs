namespace Backend.Services;

/// <summary>Limite WinAI atteinte (décision 8.10 du suivi).</summary>
public enum AiLimitKind
{
    /// <summary>Fenêtre glissante de 5 heures.</summary>
    Session,
    /// <summary>Fenêtre glissante de 7 jours.</summary>
    Week,
}

/// <summary>Une ligne du journal <c>AiTokenUsages</c>, réduite à ce que le calcul de fenêtre lit.</summary>
public readonly record struct AiUsageRecord(DateTime At, int Tokens);

/// <summary>
/// État d'une fenêtre. <see cref="Limit"/> et <see cref="Used"/> sont des
/// grandeurs INTERNES (tokens) : elles ne sortent jamais de l'API (8.3).
/// </summary>
public readonly record struct AiWindowState(
    long Limit,
    long Used,
    DateTime? AnchorAt,
    bool IsReached,
    DateTime? ResetsAt);

/// <summary>Sort d'un <c>ClientMessageId</c> déjà présent dans le journal (point A).</summary>
public enum AiReplayVerdict
{
    /// <summary>Aucune ligne : nouveau message, réserve normale.</summary>
    NewMessage,
    /// <summary>
    /// Ligne non finalisée et récente : c'est le même message (repli REST
    /// après 8 s, « Réessayer ») on le rattache sans second décompte.
    /// </summary>
    Reattach,
    /// <summary>
    /// Ligne finalisée (coût réel enregistré) ou trop ancienne : l'identifiant
    /// est consommé. La requête est traitée comme un NOUVEAU message, décompté
    /// normalement sous un identifiant serveur. Plus aucun message gratuit.
    /// </summary>
    ConsumedId,
}

/// <summary>
/// Règles chiffrées du quota WinAI : ratios session/semaine, durées, réserve
/// forfaitaire. Leur copie Python est <c>backend/python/services/ai_quota.py</c>
/// (bloc « POLICY ») ; les deux doivent rester identiques (blocs BEGIN/END).
///
/// ⚠ Lot 9.7b : la grille de référence par NOM de plan et la grille des bonus
/// parent / établissement ont été <b>supprimées</b> ici et en Python. La
/// référence d'un compte et ses bonus viennent des droits typés
/// (<c>Services/Entitlements/EntitlementService</c>, clés
/// <c>ai.reference_tokens</c>, <c>ai.parent_bonus_per_child</c>,
/// <c>ai.parent_bonus_shares</c>, licences), jamais d'un nom de plan.
///
/// Classe sans dépendance (ni EF ni ASP.NET) : elle est vérifiable isolément.
/// </summary>
public static class AiUsagePolicy
{
    // BEGIN POLICY_CONSTANTS
    public const int WeekDivisor = 4;
    public const int SessionDivisorOfWeek = 3;
    public const int SessionHours = 5;
    public const int WeekDays = 7;
    public const int ReplayWindowMinutes = 15;
    public const int ProvisionalTokensPerMessage = 1_000;
    public const int CharsPerTokenEstimate = 4;
    // END POLICY_CONSTANTS

    public static readonly TimeSpan SessionLength = TimeSpan.FromHours(SessionHours);
    public static readonly TimeSpan WeekLength = TimeSpan.FromDays(WeekDays);

    /// <summary>
    /// Durée pendant laquelle un <c>ClientMessageId</c> non finalisé peut être
    /// rattaché (repli REST après 8 s, « Réessayer »). Au-delà, l'identifiant
    /// n'ouvre plus rien : la requête est décomptée comme un nouveau message.
    /// </summary>
    public static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(ReplayWindowMinutes);

    /// <summary>
    /// Multiplicateur relatif au plan gratuit (8.3), seule grandeur exposée.
    /// <paramref name="freeReferenceTokens"/> = référence du plan gratuit élève
    /// (droit <c>ai.reference_tokens</c> du plan gratuit).
    /// </summary>
    public static int MultiplierFor(long referenceTokens, long freeReferenceTokens) =>
        Math.Max(1, (int)Math.Round((double)referenceTokens / Math.Max(1, freeReferenceTokens)));

    // ════════════════════════════════════════════════════════════════════
    // Limites dérivées (8.10)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Limite hebdomadaire = référence ÷ 4 (≈ un mois de 4 semaines).</summary>
    public static long WeeklyLimit(long referenceTokens) => Math.Max(1, referenceTokens / WeekDivisor);

    /// <summary>Limite de session = un tiers de la semaine (3 sessions pleines épuisent la semaine).</summary>
    public static long SessionLimit(long referenceTokens) => Math.Max(1, WeeklyLimit(referenceTokens) / SessionDivisorOfWeek);

    /// <summary>
    /// Coût estimé quand le fournisseur n'a pas remonté d'usage mais qu'une
    /// réponse a été servie : forfait de réserve (≈ le prompt) + la sortie
    /// relayée à ~4 caractères par token (même règle que l'estimation Python).
    /// </summary>
    public static int EstimateServedTokens(int relayedChars) =>
        ProvisionalTokensPerMessage + Math.Max(0, relayedChars) / CharsPerTokenEstimate;

    /// <summary>
    /// Fenêtre glissante <b>ancrage retenu et documenté</b> :
    ///
    ///  - La fenêtre à l'instant <c>now</c> couvre <c>]now − L, now]</c>.
    ///  - Son <b>ancre</b> est le premier enregistrement du journal dans cet
    ///    intervalle : sans message, pas de fenêtre ouverte (rien compté) ; le
    ///    premier message « ouvre » la fenêtre.
    ///  - La consommation est la somme des <c>TokensCharged</c> depuis l'ancre,
    ///    lue dans <c>AiTokenUsages</c> (aucun compteur séparé).
    ///  - La limite est atteinte quand cette somme ≥ limite.
    ///  - <b>Réinitialisation</b> (<see cref="AiWindowState.ResetsAt"/>) :
    ///    instant où, sans nouvel envoi, les enregistrements les plus anciens
    ///    sortent de la fenêtre assez pour repasser sous la limite. C'est
    ///    l'heure à laquelle l'utilisateur peut de nouveau écrire.
    ///
    /// Pourquoi glissante et non « bloc fixe qui repart à zéro » : un bloc fixe
    /// ancré au premier message dépend de l'ancre du bloc précédent, donc de
    /// tout l'historique ; il ne se recalcule pas exactement depuis une
    /// fenêtre bornée du journal et exigerait un état stocké (ancre) qui peut
    /// diverger. La fenêtre glissante se recalcule exactement, à chaque appel,
    /// à partir des seules lignes des 7 derniers jours, et elle résiste aux
    /// lignes libérées (supprimées).
    /// </summary>
    public static AiWindowState Evaluate(IEnumerable<AiUsageRecord> records, DateTime now, TimeSpan length, long limit)
    {
        var from = now - length;
        var inWindow = records.Where(r => r.At > from).OrderBy(r => r.At).ToList();
        long used = inWindow.Sum(r => (long)Math.Max(0, r.Tokens));
        var anchor = inWindow.Count > 0 ? inWindow[0].At : (DateTime?)null;
        var reached = used >= limit;

        DateTime? resetsAt = null;
        if (reached)
        {
            var remaining = used;
            foreach (var r in inWindow)
            {
                remaining -= Math.Max(0, r.Tokens);
                if (remaining < limit)
                {
                    resetsAt = r.At + length;
                    break;
                }
            }
        }

        return new AiWindowState(limit, used, anchor, reached, resetsAt);
    }

    /// <summary>
    /// Limite qui bloque effectivement : si les deux sont atteintes, celle dont
    /// la réinitialisation est la plus tardive (c'est elle qui décide quand
    /// l'utilisateur pourra réécrire) ; à égalité, la semaine.
    /// </summary>
    public static (AiLimitKind? Kind, DateTime? ResetsAt) Binding(AiWindowState session, AiWindowState week)
    {
        if (week.IsReached && session.IsReached)
            return (session.ResetsAt > week.ResetsAt)
                ? (AiLimitKind.Session, session.ResetsAt)
                : (AiLimitKind.Week, week.ResetsAt);
        if (week.IsReached) return (AiLimitKind.Week, week.ResetsAt);
        if (session.IsReached) return (AiLimitKind.Session, session.ResetsAt);
        return (null, null);
    }

    /// <summary>
    /// Classement d'un identifiant déjà connu (point A). Seule une ligne
    /// NON finalisée et créée depuis moins de <see cref="ReplayWindow"/> peut
    /// être rattachée ; tout le reste consomme l'identifiant.
    /// </summary>
    public static AiReplayVerdict ClassifyExisting(bool exists, bool isFinalized, DateTime createdAt, DateTime now)
    {
        if (!exists) return AiReplayVerdict.NewMessage;
        if (isFinalized) return AiReplayVerdict.ConsumedId;
        if (createdAt <= now - ReplayWindow) return AiReplayVerdict.ConsumedId;
        return AiReplayVerdict.Reattach;
    }

    /// <summary>Libellé utilisateur du refus (sans aucun nombre de tokens, 8.3).</summary>
    public static string LimitMessage(AiLimitKind kind, DateTime? resetsAtUtc)
    {
        var when = resetsAtUtc is DateTime r ? FormatResetTime(r) : null;
        return kind switch
        {
            AiLimitKind.Session => when is null
                ? "Vous avez atteint votre limite de session WinAI (5 heures glissantes). Passez à un plan supérieur pour continuer."
                : $"Vous avez atteint votre limite de session WinAI. Vous pourrez de nouveau écrire {when}. Un plan supérieur augmente cette limite.",
            _ => when is null
                ? "Vous avez atteint votre limite hebdomadaire WinAI (7 jours glissants). Passez à un plan supérieur pour continuer."
                : $"Vous avez atteint votre limite hebdomadaire WinAI. Vous pourrez de nouveau écrire {when}. Un plan supérieur augmente cette limite.",
        };
    }

    /// <summary>
    /// Heure de réinitialisation lisible. Le produit vise le Cameroun
    /// (Afrique/Douala, UTC+1 sans heure d'été) : on formate dans ce fuseau
    /// fixe, sans dépendre de la base de fuseaux de l'hôte. Le client reçoit
    /// aussi l'horodatage UTC brut (<c>resetsAt</c>) pour le formater lui-même.
    /// Noms de jours et de mois écrits en dur : pas de dépendance à la culture
    /// « fr-FR » (absente d'un hôte Linux en mode globalisation invariante),
    /// et libellé strictement identique à la copie Python.
    /// </summary>
    public static string FormatResetTime(DateTime resetsAtUtc)
    {
        var local = DateTime.SpecifyKind(resetsAtUtc, DateTimeKind.Utc).AddHours(1);
        var day = FrenchDays[((int)local.DayOfWeek + 6) % 7];
        var month = FrenchMonths[local.Month - 1];
        return $"le {day} {local.Day} {month} à {local.Hour:00}h{local.Minute:00}";
    }

    private static readonly string[] FrenchDays =
        { "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi", "dimanche" };

    private static readonly string[] FrenchMonths =
        { "janvier", "février", "mars", "avril", "mai", "juin", "juillet",
          "août", "septembre", "octobre", "novembre", "décembre" };
}
