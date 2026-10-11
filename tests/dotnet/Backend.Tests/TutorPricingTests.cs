using System;
using System.Collections.Generic;
using System.Linq;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 9.1 (suivi 17.2-17.3) : calcul du prix d'une séance par la grille
    /// tarifaire du répétiteur. Fonction pure, aucune base requise.
    /// </summary>
    public class TutorPricingTests
    {
        // Mardi 13 octobre 2026.
        private static readonly DateOnly Tuesday = new(2026, 10, 13);
        private static readonly DateOnly Saturday = new(2026, 10, 17);

        private static TutorPricing.Grid Grid(
            decimal? profileRate = null,
            IEnumerable<TutorRate>? rates = null,
            IEnumerable<TutorRateModifier>? modifiers = null,
            IEnumerable<TutorDurationDiscount>? discounts = null) =>
            new(profileRate, (rates ?? Array.Empty<TutorRate>()).ToList(),
                (modifiers ?? Array.Empty<TutorRateModifier>()).ToList(),
                (discounts ?? Array.Empty<TutorDurationDiscount>()).ToList());

        [Fact]
        public void Sans_grille_le_tarif_horaire_du_profil_est_facture_au_prorata_des_30_minutes()
        {
            var q = TutorPricing.Compute(Grid(profileRate: 3000), Tuesday, TimeSpan.FromHours(14), 30, "online", "Maths");
            Assert.Equal(1500m, q.TotalXaf);
            Assert.Equal("profile_default", q.BaseRateSource);
            Assert.Equal("14:30", q.EndTime);
        }

        [Fact]
        public void La_regle_la_plus_precise_matiere_et_mode_l_emporte()
        {
            var g = Grid(3000, new[]
            {
                new TutorRate { Subject = null, Mode = null, HourlyRateXaf = 3500 },
                new TutorRate { Subject = "Mathématiques", Mode = null, HourlyRateXaf = 4000 },
                new TutorRate { Subject = "Mathématiques", Mode = "in_person", HourlyRateXaf = 5000 },
                new TutorRate { Subject = null, Mode = "online", HourlyRateXaf = 2500 },
            });
            Assert.Equal(5000m, TutorPricing.Compute(g, Tuesday, TimeSpan.FromHours(10), 60, "student_home", "mathématiques").TotalXaf);
            Assert.Equal(4000m, TutorPricing.Compute(g, Tuesday, TimeSpan.FromHours(10), 60, "online", "Mathématiques").TotalXaf);
            Assert.Equal(2500m, TutorPricing.Compute(g, Tuesday, TimeSpan.FromHours(10), 60, "online", "Physique").TotalXaf);
            Assert.Equal(3500m, TutorPricing.Compute(g, Tuesday, TimeSpan.FromHours(10), 60, "tutor_home", "Physique").TotalXaf);
        }

        [Fact]
        public void Exemple_du_rapport_modulation_soir_par_tranche_et_degressivite()
        {
            // Base 4000/h ; « Soir » lun-ven 18:00-21:00 +20 % ; à partir de 2 h -10 %.
            // Mardi 17:00-19:30 : 2 tranches à 2000 + 3 tranches à 2400 = 11200 ; -10 % = 10080.
            var g = Grid(null,
                new[] { new TutorRate { Subject = "Maths", Mode = "online", HourlyRateXaf = 4000 } },
                new[] { new TutorRateModifier { Label = "Soir", DaysOfWeekMask = TutorPricing.ToMask(new[] { 1, 2, 3, 4, 5 }), StartTime = TimeSpan.FromHours(18), EndTime = TimeSpan.FromHours(21), PercentAdjustment = 20 } },
                new[] { new TutorDurationDiscount { MinDurationMinutes = 120, PercentDiscount = 10 } });

            var q = TutorPricing.Compute(g, Tuesday, TimeSpan.FromHours(17), 150, "online", "Maths");

            Assert.Equal(11200m, q.SubtotalXaf);
            Assert.Equal(10, q.DurationDiscountPercent);
            Assert.Equal(1120m, q.DurationDiscountXaf);
            Assert.Equal(10080m, q.TotalXaf);
            Assert.Equal(2, q.Segments.Count);
            Assert.Equal(("17:00", "18:00", 0, 4000m), (q.Segments[0].StartTime, q.Segments[0].EndTime, q.Segments[0].ModifierPercent, q.Segments[0].AmountXaf));
            Assert.Equal(("18:00", "19:30", 20, 7200m), (q.Segments[1].StartTime, q.Segments[1].EndTime, q.Segments[1].ModifierPercent, q.Segments[1].AmountXaf));
        }

        [Fact]
        public void Modulations_cumulees_bornees_et_jour_non_concerne_ignore()
        {
            var g = Grid(2000, null, new[]
            {
                new TutorRateModifier { Label = "Week-end", DaysOfWeekMask = TutorPricing.ToMask(new[] { 0, 6 }), PercentAdjustment = 80 },
                new TutorRateModifier { Label = "Soir", DaysOfWeekMask = 0, StartTime = TimeSpan.FromHours(18), EndTime = TimeSpan.FromHours(22), PercentAdjustment = 50 },
            });
            // Samedi soir : 80 + 50 = 130 % ramené à +100 %.
            Assert.Equal(2000m, TutorPricing.Compute(g, Saturday, TimeSpan.FromHours(19), 30, "online", null).TotalXaf);
            // Mardi matin : aucune modulation.
            Assert.Equal(1000m, TutorPricing.Compute(g, Tuesday, TimeSpan.FromHours(9), 30, "online", null).TotalXaf);
        }

        [Fact]
        public void Seul_le_palier_de_degressivite_le_plus_haut_atteint_s_applique()
        {
            var g = Grid(3000, null, null, new[]
            {
                new TutorDurationDiscount { MinDurationMinutes = 120, PercentDiscount = 5 },
                new TutorDurationDiscount { MinDurationMinutes = 180, PercentDiscount = 10 },
            });
            Assert.Equal(5700m, TutorPricing.Compute(g, Tuesday, TimeSpan.FromHours(9), 120, "online", null).TotalXaf);
            Assert.Equal(8100m, TutorPricing.Compute(g, Tuesday, TimeSpan.FromHours(9), 180, "online", null).TotalXaf);
            Assert.Equal(4500m, TutorPricing.Compute(g, Tuesday, TimeSpan.FromHours(9), 90, "online", null).TotalXaf);
        }

        [Fact]
        public void Arrondi_xaf_a_0_decimale_sur_le_total()
        {
            // 2 tranches de 1 h à 3333 → 3333 ; remise 5 % → 3166,35 → 3166.
            var g = Grid(3333, null, null, new[] { new TutorDurationDiscount { MinDurationMinutes = 60, PercentDiscount = 5 } });
            var q = TutorPricing.Compute(g, Tuesday, TimeSpan.FromHours(9), 60, "online", null);
            Assert.Equal(3166m, q.TotalXaf);
            Assert.Equal(q.SubtotalXaf - q.TotalXaf, q.DurationDiscountXaf);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(45)]
        [InlineData(-30)]
        public void Duree_hors_tranches_de_30_minutes_refusee(int minutes)
        {
            Assert.Throws<InvalidOperationException>(() =>
                TutorPricing.Compute(Grid(3000), Tuesday, TimeSpan.FromHours(9), minutes, "online", null));
        }

        [Fact]
        public void Sans_aucun_tarif_le_devis_est_refuse()
        {
            Assert.Throws<InvalidOperationException>(() =>
                TutorPricing.Compute(Grid(null), Tuesday, TimeSpan.FromHours(9), 60, "online", null));
        }

        [Fact]
        public void Plages_contigues_fusionnees_en_une_plage_continue()
        {
            var slots = new[]
            {
                new TutorAvailabilitySlot { DayOfWeek = 2, StartTime = TimeSpan.FromHours(16), EndTime = TimeSpan.FromHours(18) },
                new TutorAvailabilitySlot { DayOfWeek = 2, StartTime = TimeSpan.FromHours(14), EndTime = TimeSpan.FromHours(16) },
                new TutorAvailabilitySlot { DayOfWeek = 2, StartTime = TimeSpan.FromHours(19), EndTime = TimeSpan.FromHours(20) },
                new TutorAvailabilitySlot { DayOfWeek = 3, StartTime = TimeSpan.FromHours(8), EndTime = TimeSpan.FromHours(9) },
            };
            var ranges = TutorPricing.MergedRanges(slots, 2);
            Assert.Equal(2, ranges.Count);
            Assert.Equal((TimeSpan.FromHours(14), TimeSpan.FromHours(18)), ranges[0]);
            Assert.Equal((TimeSpan.FromHours(19), TimeSpan.FromHours(20)), ranges[1]);
        }

        [Fact]
        public void Validation_de_la_grille_rejette_les_valeurs_hors_bornes()
        {
            Assert.Throws<InvalidOperationException>(() => TutorPricing.ValidateRates(new[]
            {
                new TutorRateDto { Subject = "Maths", Mode = "online", HourlyRateXaf = 3000 },
                new TutorRateDto { Subject = "maths", Mode = "online", HourlyRateXaf = 3500 },
            }, 1));
            Assert.Throws<InvalidOperationException>(() => TutorPricing.ValidateModifiers(new[]
            {
                new TutorRateModifierDto { Label = "Soir", StartTime = "18:15", EndTime = "21:00", PercentAdjustment = 10 },
            }, 1));
            Assert.Throws<InvalidOperationException>(() => TutorPricing.ValidateDurationDiscounts(new[]
            {
                new TutorDurationDiscountDto { MinDurationMinutes = 120, PercentDiscount = 60 },
            }, 1));
            var ok = TutorPricing.ValidateModifiers(new[]
            {
                new TutorRateModifierDto { Label = "Week-end", DaysOfWeek = new() { 6, 0 }, PercentAdjustment = 15 },
            }, 1);
            Assert.Equal(TutorPricing.ToMask(new[] { 0, 6 }), ok.Single().DaysOfWeekMask);
        }

        [Fact]
        public void Sujets_de_seance_rapproches_des_matieres_du_repetiteur()
        {
            var topics = new List<string> { "Équations du second degré", "Dissertation", "Photosynthèse" };
            var matched = TutorTopicMatcher.MatchedTopics(topics, new[] { "Mathématiques", "Physique", "Prof de maths Terminale C" });
            Assert.Equal(new[] { "Équations du second degré" }, matched);

            var matchedPhilo = TutorTopicMatcher.MatchedTopics(topics, new[] { "Philosophie" });
            Assert.Equal(new[] { "Dissertation" }, matchedPhilo);
        }
    }
}
