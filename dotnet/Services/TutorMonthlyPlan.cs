using Backend.Models.DTOs;

namespace Backend.Services;

/// <summary>
/// Règles pures de l'engagement mensuel (lot 9.4, suivi 17.7), sans base ni
/// horloge, testées par TutorEngagementTests.
///
/// <list type="bullet">
///   <item><b>Période</b> : 4 semaines consécutives à partir de la date de
///   début (jours StartDate à StartDate + 27 inclus). Chaque créneau hebdomadaire
///   y tombe exactement 4 fois.</item>
///   <item><b>Prix</b> : chaque séance est chiffrée par TutorPricing.Compute
///   (grille 9.1, prix de groupe 9.3), puis la remise mensuelle d % s'applique
///   séance par séance : prix facturé = arrondi XAF(prix × (100 - d) / 100). Le
///   total payé est la somme exacte de ces prix (chaque séance porte sa part,
///   libérée ou remboursée seule) ; la remise affichée = sous-total - total.</item>
///   <item><b>Frais Mobile Money</b> du paiement unique répartis au prorata du
///   prix des séances, le reste d'arrondi sur la dernière : la somme des parts
///   vaut toujours les frais du paiement.</item>
/// </list>
/// </summary>
public static class TutorMonthlyPlan
{
    public const int Weeks = 4;
    public const int PeriodDays = Weeks * 7;
    public const int MaxSlotsPerWeek = 14;

    /// <summary>Rappel de renouvellement envoyé au payeur 7 jours avant le début de la période suivante.</summary>
    public const int RenewalReminderDaysBefore = 7;

    /// <summary>
    /// Tentative de renouvellement automatique 3 jours avant le début de la
    /// période suivante : assez tôt pour que le tuteur ait son délai de réponse
    /// (préavis de 12, 24 ou 48 h, plafonné à 1 h avant la première séance),
    /// assez tard pour que le payeur ait pu recharger après le rappel de J-7.
    /// </summary>
    public const int AutoRenewDaysBefore = 3;

    public sealed record Slot(int DayOfWeek, TimeSpan Start, int DurationMinutes)
    {
        public TimeSpan End => Start + TimeSpan.FromMinutes(DurationMinutes);
    }

    public sealed record Occurrence(DateOnly Date, TimeSpan Start, int DurationMinutes)
    {
        public TimeSpan End => Start + TimeSpan.FromMinutes(DurationMinutes);
    }

    public static DateOnly EndDate(DateOnly start) => start.AddDays(PeriodDays - 1);
    public static DateOnly NextStartDate(DateOnly start) => start.AddDays(PeriodDays);

    /// <summary>Instant (UTC) de la tentative de renouvellement automatique d'une période commencée le <paramref name="start"/>.</summary>
    public static DateTime AutoRenewAt(DateOnly start) =>
        NextStartDate(start).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(-AutoRenewDaysBefore);

    public static DateTime RenewalReminderAt(DateOnly start) =>
        NextStartDate(start).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(-RenewalReminderDaysBefore);

    /// <summary>Remise mensuelle bornée côté serveur (0 à 30 %).</summary>
    public static int ClampDiscount(int percent) => Math.Clamp(percent, 0, Backend.Models.Entities.TutorPackageKinds.MaxMonthlyDiscountPercent);

    /// <summary>
    /// Valide et normalise les créneaux saisis : jour 0-6, début aligné sur une
    /// tranche, durée multiple de 30 min bornée, pas de doublon ni de
    /// chevauchement entre deux créneaux du même jour. Lève
    /// InvalidOperationException (message affichable).
    /// </summary>
    public static List<Slot> ParseSlots(IEnumerable<TutorEngagementSlotDto>? input, int maxSessionMinutes)
    {
        var list = (input ?? Enumerable.Empty<TutorEngagementSlotDto>()).ToList();
        if (list.Count == 0)
            throw new InvalidOperationException("Choisis au moins un créneau hebdomadaire.");
        if (list.Count > MaxSlotsPerWeek)
            throw new InvalidOperationException($"Un engagement mensuel compte au plus {MaxSlotsPerWeek} créneaux par semaine.");

        var slots = new List<Slot>();
        foreach (var s in list)
        {
            if (s.DayOfWeek is < 0 or > 6)
                throw new InvalidOperationException("Jour de la semaine invalide.");
            if (!TimeSpan.TryParse(s.StartTime, out var start) || start < TimeSpan.Zero || start >= TimeSpan.FromHours(24))
                throw new InvalidOperationException("Heure de début invalide.");
            if (!TutorPricing.IsAlignedOnSlice(start))
                throw new InvalidOperationException("Une séance commence à l'heure pile ou à la demie (tranches de 30 min).");
            if (s.DurationMinutes < TutorPricing.SliceMinutes || s.DurationMinutes % TutorPricing.SliceMinutes != 0)
                throw new InvalidOperationException("La durée d'une séance doit être un multiple de 30 minutes (30 minutes minimum).");
            if (s.DurationMinutes > maxSessionMinutes)
                throw new InvalidOperationException($"Une séance ne peut pas dépasser {maxSessionMinutes / 60.0:0.#} h.");
            var slot = new Slot(s.DayOfWeek, start, s.DurationMinutes);
            if (slot.End > TimeSpan.FromHours(24))
                throw new InvalidOperationException("Une séance ne peut pas déborder sur le jour suivant.");
            if (slots.Any(o => o.DayOfWeek == slot.DayOfWeek && o.Start < slot.End && o.End > slot.Start))
                throw new InvalidOperationException("Deux créneaux du même jour se chevauchent.");
            slots.Add(slot);
        }
        return slots.OrderBy(s => (s.DayOfWeek + 6) % 7).ThenBy(s => s.Start).ToList();
    }

    /// <summary>Les séances de la période : chaque créneau, chaque semaine (4 × nombre de créneaux), triées.</summary>
    public static List<Occurrence> Occurrences(DateOnly start, IEnumerable<Slot> slots)
    {
        var result = new List<Occurrence>();
        for (var i = 0; i < PeriodDays; i++)
        {
            var date = start.AddDays(i);
            foreach (var s in slots.Where(s => s.DayOfWeek == (int)date.DayOfWeek))
                result.Add(new Occurrence(date, s.Start, s.DurationMinutes));
        }
        return result.OrderBy(o => o.Date).ThenBy(o => o.Start).ToList();
    }

    /// <summary>Prix facturé d'une séance après remise mensuelle.</summary>
    public static decimal DiscountedPrice(decimal sessionTotalXaf, int discountPercent) =>
        RevenueSplit.Xaf(sessionTotalXaf * (100 - ClampDiscount(discountPercent)) / 100m);

    /// <summary>Répartit des frais entre des séances au prorata de leur prix ; la somme des parts vaut exactement <paramref name="feeXaf"/>.</summary>
    public static List<decimal> AllocateFee(decimal feeXaf, IReadOnlyList<decimal> prices)
    {
        var result = new List<decimal>(prices.Count);
        if (prices.Count == 0) return result;
        var fee = RevenueSplit.Xaf(Math.Max(0, feeXaf));
        var total = prices.Sum();
        var allocated = 0m;
        for (var i = 0; i < prices.Count; i++)
        {
            decimal share;
            if (i == prices.Count - 1) share = fee - allocated;
            else share = total <= 0 ? 0m : Math.Min(fee - allocated, RevenueSplit.Xaf(fee * prices[i] / total));
            share = Math.Clamp(share, 0, Math.Max(0, prices[i]));
            allocated += share;
            result.Add(share);
        }
        // Reste éventuel (dernière séance moins chère que le reliquat) : reporté sur les séances qui peuvent l'absorber.
        var rest = fee - allocated;
        for (var i = 0; rest > 0 && i < result.Count; i++)
        {
            var room = prices[i] - result[i];
            var add = Math.Min(room, rest);
            result[i] += add;
            rest -= add;
        }
        return result;
    }

    private static readonly string[] DayShort = { "dim.", "lun.", "mar.", "mer.", "jeu.", "ven.", "sam." };

    /// <summary>« mar. 14/10 18:00 » : libellé court d'une séance pour les messages de conflit.</summary>
    public static string Label(DateOnly date, TimeSpan start) =>
        $"{DayShort[(int)date.DayOfWeek]} {date:dd/MM} {TutorPricing.Hhmm(start)}";
}
