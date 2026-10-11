using Backend.Models.DTOs;
using Backend.Models.Entities;

namespace Backend.Services;

/// <summary>
/// Calcul du prix d'une séance de tutorat (lot 9.1, suivi Partie 17.2-17.3).
/// Fonction pure, sans accès base ni horloge : c'est la seule source de vérité
/// du prix. Utilisée par l'endpoint de devis et par la création de
/// réservation (TutorBookingService), et réutilisable telle quelle par les
/// lots 9.3 (groupes) et 9.4 (mensuel), qui appelleront Compute séance par
/// séance. Le client n'affiche jamais qu'un devis renvoyé par le serveur.
///
/// Formule :
///   1. tarif de base horaire B = règle la plus précise de la grille pour
///      (matière, catégorie de mode) : matière+mode → matière → mode → toutes,
///      puis TutorProfile.HourlyRateXaf (rétrocompatibilité) ;
///   2. la séance est découpée en tranches de 30 min ; chaque tranche coûte
///      B / 2 × (1 + m / 100), où m = somme des modulations dont la fenêtre
///      contient le début de la tranche (bornée à [-50, +100]) ;
///   3. sous-total S = somme exacte des tranches ;
///   4. dégressivité : le palier de durée le plus élevé atteint donne d % ;
///   5. total T = arrondi XAF (0 décimale, au plus proche) de S × (1 - d / 100).
///      Le montant de la remise affiché = arrondi(S) - T, déduit et non
///      arrondi séparément (décision §4.E).
///   6. groupe de N élèves (lot 9.3) : voir <see cref="ComputeGroup"/> ; T
///      devient SoloTotalXaf et TotalXaf le prix du groupe entier.
/// </summary>
public static class TutorPricing
{
    public const int SliceMinutes = 30;
    public const int MinModifierPercent = -50;
    public const int MaxModifierPercent = 100;
    public const int MaxDurationDiscountPercent = 50;
    public const int MaxRates = 40;
    public const int MaxModifiers = 10;
    public const int MaxDurationDiscounts = 5;
    public const decimal MaxHourlyRateXaf = 1_000_000m;

    public const string ModeOnline = "online";
    public const string ModeInPerson = "in_person";

    /// <summary>Les quatre modes de réservation se ramènent à deux catégories tarifaires (17.3 : en ligne / présentiel).</summary>
    public static string ModeCategory(string bookingMode) => bookingMode == ModeOnline ? ModeOnline : ModeInPerson;

    /// <summary>Instantané immuable de la grille d'un répétiteur.</summary>
    public sealed record Grid(
        decimal? ProfileHourlyRateXaf,
        IReadOnlyList<TutorRate> Rates,
        IReadOnlyList<TutorRateModifier> Modifiers,
        IReadOnlyList<TutorDurationDiscount> DurationDiscounts)
    {
        /// <summary>Réglages de groupe (lot 9.3) ; par défaut : séances individuelles seulement.</summary>
        public GroupPricing Group { get; init; } = GroupPricing.None;

        public static Grid FromProfile(TutorProfile p) => new(
            p.HourlyRateXaf,
            p.Rates.ToList(),
            p.RateModifiers.ToList(),
            p.DurationDiscounts.ToList())
        {
            Group = GroupPricing.FromProfile(p),
        };
    }

    // ── Séances de groupe (lot 9.3, suivi 17.3) ──────────────────────────

    public const string GroupPerStudent = "per_student";
    public const string GroupFlat = "flat";
    public const int MaxGroupSizeCap = 20;
    public const int MaxGroupDiscountPercent = 70;
    public const int MaxGroupDiscounts = 5;
    public const int MinGroupFlatPercent = 100;
    public const int MaxGroupFlatPercent = 1000;

    /// <summary>
    /// Méthode de prix de groupe choisie par le tuteur. <see cref="Method"/>
    /// null = pas de groupe (<see cref="MaxGroupSize"/> vaut alors 1).
    /// </summary>
    public sealed record GroupPricing(string? Method, int MaxGroupSize, int? FlatPercent, IReadOnlyList<TutorGroupDiscount> Discounts)
    {
        public static readonly GroupPricing None = new(null, 1, null, Array.Empty<TutorGroupDiscount>());

        public bool Enabled => Method is GroupPerStudent or GroupFlat && MaxGroupSize >= 2;

        public static GroupPricing FromProfile(TutorProfile p) =>
            p.GroupPricingMethod is GroupPerStudent or GroupFlat && p.MaxGroupSize >= 2
                ? new GroupPricing(p.GroupPricingMethod, Math.Min(p.MaxGroupSize, MaxGroupSizeCap), p.GroupFlatPricePercent, p.GroupDiscounts.ToList())
                : None;
    }

    /// <summary>
    /// Tarif de base pour (matière, catégorie de mode), avec la règle d'origine.
    /// Null si le répétiteur n'a fixé aucun tarif.
    /// </summary>
    public static (decimal Rate, string Source)? ResolveBaseHourlyRate(Grid grid, string? subject, string modeCategory)
    {
        bool SameSubject(TutorRate r) => r.Subject != null && subject != null
            && string.Equals(r.Subject.Trim(), subject.Trim(), StringComparison.OrdinalIgnoreCase);

        var candidates = new (Func<TutorRate, bool> Match, string Source)[]
        {
            (r => SameSubject(r) && r.Mode == modeCategory, "subject_mode"),
            (r => SameSubject(r) && r.Mode == null, "subject"),
            (r => r.Subject == null && r.Mode == modeCategory, "mode"),
            (r => r.Subject == null && r.Mode == null, "all"),
        };
        foreach (var (match, source) in candidates)
        {
            var rule = grid.Rates.FirstOrDefault(r => r.HourlyRateXaf > 0 && match(r));
            if (rule != null) return (rule.HourlyRateXaf, source);
        }

        if (grid.ProfileHourlyRateXaf is decimal fallback && fallback > 0)
            return (fallback, "profile_default");
        return null;
    }

    /// <summary>Plus bas tarif de base proposé (affichage « à partir de », tri et filtre de la recherche).</summary>
    public static decimal? FromHourlyRate(Grid grid)
    {
        var values = grid.Rates.Where(r => r.HourlyRateXaf > 0).Select(r => r.HourlyRateXaf).ToList();
        // Le tarif du profil ne reste le « à partir de » que s'il sert encore
        // de repli, c.-à-d. s'il n'existe pas de règle couvrant tout.
        var coversEverything = grid.Rates.Any(r => r.HourlyRateXaf > 0 && r.Subject == null && r.Mode == null);
        if (grid.ProfileHourlyRateXaf is decimal p && p > 0 && !coversEverything) values.Add(p);
        return values.Count == 0 ? null : values.Min();
    }

    public static bool DayMatches(int daysOfWeekMask, DayOfWeek day) =>
        daysOfWeekMask == 0 || (daysOfWeekMask & (1 << (int)day)) != 0;

    public static int ToMask(IEnumerable<int> days) =>
        days.Where(d => d is >= 0 and <= 6).Distinct().Aggregate(0, (m, d) => m | (1 << d));

    public static List<int> FromMask(int mask) =>
        Enumerable.Range(0, 7).Where(d => (mask & (1 << d)) != 0).ToList();

    /// <summary>Une modulation s'applique à une tranche si le jour correspond et si la tranche commence dans la fenêtre.</summary>
    public static bool ModifierApplies(TutorRateModifier m, DayOfWeek day, TimeSpan sliceStart)
    {
        if (!DayMatches(m.DaysOfWeekMask, day)) return false;
        if (m.StartTime is null || m.EndTime is null) return true;
        return sliceStart >= m.StartTime.Value && sliceStart < m.EndTime.Value;
    }

    /// <summary>
    /// Devis d'une séance. Lève InvalidOperationException si la durée n'est pas
    /// un multiple positif de 30 min ou si aucun tarif n'est défini.
    /// <paramref name="mode"/> est le mode de réservation (online, student_home…).
    /// </summary>
    public static TutorPriceQuoteDto Compute(Grid grid, DateOnly date, TimeSpan start, int durationMinutes, string mode, string? subject, int participantCount = 1)
    {
        if (durationMinutes < SliceMinutes || durationMinutes % SliceMinutes != 0)
            throw new InvalidOperationException("La durée d'une séance doit être un multiple de 30 minutes (30 minutes minimum).");
        if (participantCount < 1)
            throw new InvalidOperationException("Une séance compte au moins un élève.");
        if (participantCount > 1)
        {
            // Contrôlé avant tout calcul : un groupe refusé n'a pas de prix.
            if (!grid.Group.Enabled)
                throw new InvalidOperationException("Ce répétiteur ne propose pas de séances de groupe.");
            if (participantCount > grid.Group.MaxGroupSize)
                throw new InvalidOperationException($"Ce répétiteur accepte au plus {grid.Group.MaxGroupSize} élèves par séance.");
        }

        var category = ModeCategory(mode);
        var resolved = ResolveBaseHourlyRate(grid, subject, category)
            ?? throw new InvalidOperationException("Ce répétiteur n'a pas encore fixé son tarif.");
        var baseRate = resolved.Rate;
        var halfHourBase = baseRate / 2m;

        var slices = durationMinutes / SliceMinutes;
        var segments = new List<(TimeSpan Start, int Slices, int Percent, List<string> Labels, decimal Amount)>();
        var subtotal = 0m;

        for (var i = 0; i < slices; i++)
        {
            var sliceStart = start + TimeSpan.FromMinutes(i * SliceMinutes);
            var applied = grid.Modifiers.Where(m => ModifierApplies(m, date.DayOfWeek, sliceStart)).ToList();
            var percent = Math.Clamp(applied.Sum(m => m.PercentAdjustment), MinModifierPercent, MaxModifierPercent);
            var amount = halfHourBase * (100 + percent) / 100m;
            subtotal += amount;

            var labels = applied.Select(m => m.Label).ToList();
            if (segments.Count > 0 && segments[^1].Percent == percent && segments[^1].Labels.SequenceEqual(labels))
            {
                var last = segments[^1];
                segments[^1] = (last.Start, last.Slices + 1, last.Percent, last.Labels, last.Amount + amount);
            }
            else
            {
                segments.Add((sliceStart, 1, percent, labels, amount));
            }
        }

        var discountPercent = grid.DurationDiscounts
            .Where(d => d.MinDurationMinutes <= durationMinutes && d.PercentDiscount > 0)
            .OrderByDescending(d => d.MinDurationMinutes)
            .Select(d => Math.Clamp(d.PercentDiscount, 0, MaxDurationDiscountPercent))
            .FirstOrDefault();

        var total = RevenueSplit.Xaf(subtotal * (100 - discountPercent) / 100m);
        var subtotalXaf = RevenueSplit.Xaf(subtotal);
        var end = start + TimeSpan.FromMinutes(durationMinutes);
        var group = ComputeGroup(grid.Group, total, participantCount);

        return new TutorPriceQuoteDto
        {
            SessionDate = date.ToString("yyyy-MM-dd"),
            StartTime = Hhmm(start),
            EndTime = Hhmm(end),
            DurationMinutes = durationMinutes,
            Mode = mode,
            ModeCategory = category,
            Subject = subject,
            BaseHourlyRateXaf = baseRate,
            BaseRateSource = resolved.Source,
            Segments = segments.Select(s => new TutorPriceQuoteSegmentDto
            {
                StartTime = Hhmm(s.Start),
                EndTime = Hhmm(s.Start + TimeSpan.FromMinutes(s.Slices * SliceMinutes)),
                Minutes = s.Slices * SliceMinutes,
                ModifierPercent = s.Percent,
                ModifierLabels = s.Labels,
                EffectiveHourlyRateXaf = RevenueSplit.Xaf(baseRate * (100 + s.Percent) / 100m),
                AmountXaf = RevenueSplit.Xaf(s.Amount),
            }).ToList(),
            SubtotalXaf = subtotalXaf,
            DurationDiscountPercent = discountPercent,
            DurationDiscountXaf = subtotalXaf - total,
            ParticipantCount = participantCount,
            SoloTotalXaf = total,
            GroupPricingMethod = group.Method,
            GroupDiscountPercent = group.DiscountPercent,
            GroupFlatPercent = group.FlatPercent,
            PerStudentXaf = group.PerStudent,
            TotalXaf = group.Total,
        };
    }

    /// <summary>
    /// Prix de groupe (lot 9.3), appliqué au prix individuel T déjà calculé
    /// (créneaux, dégressivité de durée, prorata 30 min) :
    /// <list type="bullet">
    ///   <item>1 élève : T, quelle que soit la méthode ;</item>
    ///   <item>per_student : remise d % du palier le plus élevé atteint
    ///   (MinGroupSize ≤ N) ; prix par élève P = arrondi XAF(T × (1 - d/100)),
    ///   total = P × N (le total est toujours un multiple exact du prix par
    ///   élève affiché) ;</item>
    ///   <item>flat : total = arrondi XAF(T × f/100), f = pourcentage fixé par
    ///   le tuteur (100 à 1000), indépendant de N ; prix par élève indicatif =
    ///   arrondi(total / N).</item>
    /// </list>
    /// </summary>
    public static (string? Method, int DiscountPercent, int? FlatPercent, decimal PerStudent, decimal Total) ComputeGroup(GroupPricing group, decimal soloTotal, int participantCount)
    {
        if (participantCount <= 1)
            return (null, 0, null, soloTotal, soloTotal);

        if (group.Method == GroupFlat)
        {
            var percent = Math.Clamp(group.FlatPercent ?? MinGroupFlatPercent, MinGroupFlatPercent, MaxGroupFlatPercent);
            var flatTotal = RevenueSplit.Xaf(soloTotal * percent / 100m);
            return (GroupFlat, 0, percent, RevenueSplit.Xaf(flatTotal / participantCount), flatTotal);
        }

        var discount = group.Discounts
            .Where(d => d.MinGroupSize <= participantCount && d.PercentDiscount > 0)
            .OrderByDescending(d => d.MinGroupSize)
            .Select(d => Math.Clamp(d.PercentDiscount, 0, MaxGroupDiscountPercent))
            .FirstOrDefault();
        var perStudent = RevenueSplit.Xaf(soloTotal * (100 - discount) / 100m);
        return (GroupPerStudent, discount, null, perStudent, perStudent * participantCount);
    }

    /// <summary>
    /// Valide les réglages de groupe saisis par le tuteur ; renvoie (méthode,
    /// taille max, pourcentage fixe, paliers). Méthode vide = groupes désactivés.
    /// </summary>
    public static (string? Method, int MaxGroupSize, int? FlatPercent, List<TutorGroupDiscount> Discounts) ValidateGroupPricing(
        string? method, int? maxGroupSize, int? flatPercent, IEnumerable<TutorGroupDiscountDto>? discounts, int tutorProfileId)
    {
        var m = string.IsNullOrWhiteSpace(method) ? null : method.Trim();
        if (m == null || m == "none")
            return (null, 1, null, new List<TutorGroupDiscount>());
        if (m != GroupPerStudent && m != GroupFlat)
            throw new InvalidOperationException("Méthode de prix de groupe invalide (prix par élève dégressif ou prix fixe).");

        var max = maxGroupSize ?? 0;
        if (max < 2 || max > MaxGroupSizeCap)
            throw new InvalidOperationException($"La taille maximale d'un groupe est comprise entre 2 et {MaxGroupSizeCap} élèves.");

        if (m == GroupFlat)
        {
            if (flatPercent is not int f || f < MinGroupFlatPercent || f > MaxGroupFlatPercent)
                throw new InvalidOperationException($"Le prix fixe du groupe se règle entre {MinGroupFlatPercent} % et {MaxGroupFlatPercent} % du prix individuel.");
            return (m, max, f, new List<TutorGroupDiscount>());
        }

        var list = (discounts ?? Enumerable.Empty<TutorGroupDiscountDto>()).ToList();
        if (list.Count > MaxGroupDiscounts)
            throw new InvalidOperationException($"Le prix de groupe ne peut pas dépasser {MaxGroupDiscounts} paliers.");
        var result = new List<TutorGroupDiscount>();
        foreach (var d in list)
        {
            if (d.MinGroupSize < 2 || d.MinGroupSize > max)
                throw new InvalidOperationException($"Un palier de groupe commence entre 2 élèves et la taille maximale ({max}).");
            if (d.PercentDiscount < 1 || d.PercentDiscount > MaxGroupDiscountPercent)
                throw new InvalidOperationException($"La remise par élève d'un palier est comprise entre 1 % et {MaxGroupDiscountPercent} %.");
            if (result.Any(x => x.MinGroupSize == d.MinGroupSize))
                throw new InvalidOperationException("Deux paliers de groupe ne peuvent pas commencer à la même taille.");
            result.Add(new TutorGroupDiscount { TutorProfileId = tutorProfileId, MinGroupSize = d.MinGroupSize, PercentDiscount = d.PercentDiscount });
        }
        return (m, max, null, result.OrderBy(x => x.MinGroupSize).ToList());
    }

    // ── Validation de la grille saisie par le répétiteur ─────────────────

    /// <summary>Normalise et valide les tarifs de base ; lève InvalidOperationException avec un message lisible.</summary>
    public static List<TutorRate> ValidateRates(IEnumerable<TutorRateDto> input, int tutorProfileId)
    {
        var list = input.ToList();
        if (list.Count > MaxRates)
            throw new InvalidOperationException($"La grille ne peut pas dépasser {MaxRates} tarifs de base.");

        var result = new List<TutorRate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in list)
        {
            var subject = string.IsNullOrWhiteSpace(r.Subject) ? null : Truncate(r.Subject.Trim(), 100);
            var mode = string.IsNullOrWhiteSpace(r.Mode) ? null : r.Mode.Trim();
            if (mode != null && mode != ModeOnline && mode != ModeInPerson)
                throw new InvalidOperationException("Mode de tarif invalide (en ligne ou présentiel).");
            if (r.HourlyRateXaf <= 0 || r.HourlyRateXaf > MaxHourlyRateXaf)
                throw new InvalidOperationException("Chaque tarif de base doit être un montant positif en XAF.");
            if (!seen.Add($"{subject}|{mode}"))
                throw new InvalidOperationException($"Tarif en double pour {subject ?? "toutes les matières"} / {ModeLabel(mode)}.");

            result.Add(new TutorRate
            {
                TutorProfileId = tutorProfileId,
                Subject = subject,
                Mode = mode,
                HourlyRateXaf = RevenueSplit.Xaf(r.HourlyRateXaf),
            });
        }
        return result;
    }

    public static List<TutorRateModifier> ValidateModifiers(IEnumerable<TutorRateModifierDto> input, int tutorProfileId)
    {
        var list = input.ToList();
        if (list.Count > MaxModifiers)
            throw new InvalidOperationException($"La grille ne peut pas dépasser {MaxModifiers} modulations par créneau.");

        var result = new List<TutorRateModifier>();
        foreach (var m in list)
        {
            var label = string.IsNullOrWhiteSpace(m.Label) ? "Modulation" : Truncate(m.Label.Trim(), 60);
            if (m.PercentAdjustment == 0 || m.PercentAdjustment < MinModifierPercent || m.PercentAdjustment > MaxModifierPercent)
                throw new InvalidOperationException($"« {label} » : la modulation doit être comprise entre {MinModifierPercent} % et +{MaxModifierPercent} % (et non nulle).");
            if (m.DaysOfWeek.Any(d => d is < 0 or > 6))
                throw new InvalidOperationException($"« {label} » : jour de la semaine invalide.");

            TimeSpan? start = null, end = null;
            var hasStart = !string.IsNullOrWhiteSpace(m.StartTime);
            var hasEnd = !string.IsNullOrWhiteSpace(m.EndTime);
            if (hasStart != hasEnd)
                throw new InvalidOperationException($"« {label} » : indique l'heure de début et l'heure de fin, ou aucune des deux (toute la journée).");
            if (hasStart)
            {
                start = ParseAlignedTime(m.StartTime!, label);
                end = ParseAlignedTime(m.EndTime!, label, allowMidnightEnd: true);
                if (end <= start)
                    throw new InvalidOperationException($"« {label} » : l'heure de fin doit être après l'heure de début (pas de fenêtre à cheval sur minuit).");
            }
            var mask = ToMask(m.DaysOfWeek);
            if (mask == 0 && !hasStart)
                throw new InvalidOperationException($"« {label} » : choisis des jours ou une plage horaire, sinon la modulation s'applique à toutes les séances.");

            result.Add(new TutorRateModifier
            {
                TutorProfileId = tutorProfileId,
                Label = label,
                DaysOfWeekMask = mask,
                StartTime = start,
                EndTime = end,
                PercentAdjustment = m.PercentAdjustment,
            });
        }
        return result;
    }

    public static List<TutorDurationDiscount> ValidateDurationDiscounts(IEnumerable<TutorDurationDiscountDto> input, int tutorProfileId)
    {
        var list = input.ToList();
        if (list.Count > MaxDurationDiscounts)
            throw new InvalidOperationException($"La grille ne peut pas dépasser {MaxDurationDiscounts} paliers de dégressivité.");

        var result = new List<TutorDurationDiscount>();
        foreach (var d in list)
        {
            if (d.MinDurationMinutes < 60 || d.MinDurationMinutes > 720 || d.MinDurationMinutes % SliceMinutes != 0)
                throw new InvalidOperationException("Un palier de dégressivité commence à une durée multiple de 30 min, entre 1 h et 12 h.");
            if (d.PercentDiscount < 1 || d.PercentDiscount > MaxDurationDiscountPercent)
                throw new InvalidOperationException($"La remise d'un palier de dégressivité est comprise entre 1 % et {MaxDurationDiscountPercent} %.");
            if (result.Any(x => x.MinDurationMinutes == d.MinDurationMinutes))
                throw new InvalidOperationException("Deux paliers de dégressivité ne peuvent pas commencer à la même durée.");
            result.Add(new TutorDurationDiscount
            {
                TutorProfileId = tutorProfileId,
                MinDurationMinutes = d.MinDurationMinutes,
                PercentDiscount = d.PercentDiscount,
            });
        }
        return result.OrderBy(x => x.MinDurationMinutes).ToList();
    }

    // ── Agenda en tranches de 30 min (17.1-17.2) ─────────────────────────

    /// <summary>
    /// Plages de disponibilité actives d'un jour, fusionnées quand elles se
    /// touchent ou se chevauchent (ex. 14:00-16:00 + 16:00-18:00 = une plage
    /// continue 14:00-18:00) : la durée maximale d'une séance est la longueur
    /// de la partie libre en continu, pas celle d'une ligne de la grille.
    /// </summary>
    public static List<(TimeSpan Start, TimeSpan End)> MergedRanges(IEnumerable<TutorAvailabilitySlot> slots, int dayOfWeek)
    {
        var ordered = slots
            .Where(s => s.IsActive && s.DayOfWeek == dayOfWeek && s.EndTime > s.StartTime)
            .OrderBy(s => s.StartTime)
            .ToList();
        var merged = new List<(TimeSpan Start, TimeSpan End)>();
        foreach (var s in ordered)
        {
            if (merged.Count > 0 && s.StartTime <= merged[^1].End)
            {
                if (s.EndTime > merged[^1].End) merged[^1] = (merged[^1].Start, s.EndTime);
            }
            else
            {
                merged.Add((s.StartTime, s.EndTime));
            }
        }
        return merged;
    }

    /// <summary>Premier début de tranche (:00 ou :30) à partir de t.</summary>
    public static TimeSpan CeilToSlice(TimeSpan t)
    {
        var minutes = (int)Math.Ceiling(t.TotalMinutes / SliceMinutes) * SliceMinutes;
        return TimeSpan.FromMinutes(minutes);
    }

    public static bool IsAlignedOnSlice(TimeSpan t) =>
        t.Seconds == 0 && t.Milliseconds == 0 && (int)t.TotalMinutes % SliceMinutes == 0;

    /// <summary>Sujets de séance : nettoyés, dédoublonnés, 5 au plus, 100 caractères chacun.</summary>
    public const int MaxTopics = 5;
    public static List<string> SanitizeTopics(IEnumerable<string>? topics) =>
        (topics ?? Enumerable.Empty<string>())
            .Select(t => (t ?? "").Trim())
            .Where(t => t.Length > 0)
            .Select(t => Truncate(t, 100))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTopics)
            .ToList();

    public static string Hhmm(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}";

    private static TimeSpan ParseAlignedTime(string value, string label, bool allowMidnightEnd = false)
    {
        if (allowMidnightEnd && value.Trim() is "24:00")
            return TimeSpan.FromHours(24);
        if (!TimeSpan.TryParse(value, out var t) || t < TimeSpan.Zero || t >= TimeSpan.FromHours(24))
            throw new InvalidOperationException($"« {label} » : heure invalide ({value}).");
        if (!IsAlignedOnSlice(t))
            throw new InvalidOperationException($"« {label} » : les heures se règlent par tranches de 30 min (ex. 18:00 ou 18:30).");
        return t;
    }

    private static string ModeLabel(string? mode) => mode switch
    {
        ModeOnline => "en ligne",
        ModeInPerson => "présentiel",
        _ => "tous les modes",
    };

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
