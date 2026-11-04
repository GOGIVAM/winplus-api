using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Codes promo : cibles (PromoCodeScopes, y compris les valeurs historiques),
    /// création et modification avec validation du modèle de promotion unifié,
    /// activation, et rattachement idempotent d'un code à une commande payée
    /// (usage enregistré, quota décompté une seule fois). Base en mémoire.
    /// </summary>
    public class PromoCodeScopesAndServiceTests
    {
        private static readonly DateTime From = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static PromoCodeService Service(TestApplicationDbContext db) =>
            new(db, NullLogger<PromoCodeService>.Instance);

        private static CreatePromoCodeRequest Request(string code = "RENTREE", decimal value = 20, string? type = null) =>
            new() { Code = code, DiscountType = type, DiscountValue = value, ValidFrom = From };

        // ── PromoCodeScopes ─────────────────────────────────────────────

        [Fact]
        public void Une_valeur_vide_vise_le_catalogue_et_les_formations()
        {
            Assert.Equal(new[] { "catalog", "courses" }, PromoCodeScopes.Expand(null).OrderBy(x => x));
            Assert.Equal(new[] { "catalog", "courses" }, PromoCodeScopes.Expand("  ").OrderBy(x => x));
        }

        [Fact]
        public void La_cible_catalog_couvre_aussi_les_formations()
        {
            var set = PromoCodeScopes.Expand("catalog");
            Assert.Contains("catalog", set);
            Assert.Contains("courses", set);
            Assert.DoesNotContain("subscriptions", set);
        }

        [Fact]
        public void La_valeur_historique_all_couvre_toutes_les_cibles_et_subscription_devient_subscriptions()
        {
            Assert.Equal(PromoCodeScopes.Targets.OrderBy(x => x), PromoCodeScopes.Expand("all").OrderBy(x => x));
            Assert.Equal(new[] { "subscriptions" }, PromoCodeScopes.Expand("subscription").ToArray());
        }

        [Fact]
        public void Les_cibles_inconnues_sont_ignorees_a_l_expansion()
        {
            Assert.Equal(new[] { "token_topup" }, PromoCodeScopes.Expand("token_topup, inconnue").ToArray());
        }

        [Fact]
        public void La_forme_canonique_est_triee_selon_l_ordre_des_cibles_en_minuscules()
        {
            Assert.Equal("courses,subscriptions", PromoCodeScopes.Normalize("Subscriptions, courses"));
            Assert.Equal("catalog,courses", PromoCodeScopes.Normalize("catalog"));
            Assert.Equal("catalog", PromoCodeScopes.Normalize(null));
            Assert.Equal("catalog", PromoCodeScopes.Normalize(" "));
        }

        [Fact]
        public void Une_cible_inconnue_rend_la_valeur_invalide()
        {
            Assert.Null(PromoCodeScopes.Normalize("catalog,magie"));
            Assert.False(PromoCodeScopes.IsValid("magie"));
            Assert.True(PromoCodeScopes.IsValid("all"));
            Assert.True(PromoCodeScopes.IsValid(null));
        }

        [Fact]
        public void AppliesTo_et_AppliesToCatalog_interrogent_les_cibles_effectives()
        {
            Assert.True(PromoCodeScopes.AppliesTo("subscriptions,token_topup", "token_topup"));
            Assert.False(PromoCodeScopes.AppliesTo("subscriptions", "catalog"));
            Assert.True(PromoCodeScopes.AppliesToCatalog("catalog"));
            Assert.True(PromoCodeScopes.AppliesToCatalog(null));
            Assert.False(PromoCodeScopes.AppliesToCatalog("subscription"));
        }

        [Fact]
        public void Les_natures_durees_et_eligibilites_connues_sont_reconnues_et_les_autres_refusees()
        {
            Assert.True(PromotionKinds.IsValid("trial_days"));
            Assert.False(PromotionKinds.IsValid("gratuit"));
            Assert.False(PromotionKinds.IsValid(null));
            Assert.True(PromotionDurations.IsValid("n_periods"));
            Assert.False(PromotionDurations.IsValid("toujours"));
            Assert.True(PromotionEligibility.IsValid("lapsed_subscribers"));
            Assert.False(PromotionEligibility.IsValid("tout_le_monde"));
        }

        // ── Création ────────────────────────────────────────────────────

        [Fact]
        public async Task La_creation_met_le_code_en_majuscules_et_applique_les_valeurs_par_defaut()
        {
            using var db = TestApplicationDbContext.Create();
            var dto = await Service(db).CreatePromoCodeAsync(1, Request("rentree"));

            Assert.Equal("RENTREE", dto.Code);
            Assert.Equal("Percentage", dto.DiscountType);
            Assert.Equal(PromotionKinds.DiscountPercent, dto.Promotion.Kind);
            Assert.Equal(1, dto.PerUserLimit);
            Assert.Equal("catalog,courses", dto.AppliesTo);
            Assert.True(dto.IsActive);
            Assert.Equal(0, dto.UsageCount);
            Assert.Single(db.PromoCodes.ToList());
        }

        [Fact]
        public async Task Un_montant_fixe_donne_la_nature_discount_amount()
        {
            using var db = TestApplicationDbContext.Create();
            var dto = await Service(db).CreatePromoCodeAsync(1, Request("BON500", 500, "FixedAmount"));
            Assert.Equal(PromotionKinds.DiscountAmount, dto.Promotion.Kind);
            Assert.Equal("FixedAmount", dto.DiscountType);
            Assert.Equal(500m, dto.DiscountValue);
        }

        [Fact]
        public async Task Un_code_existant_est_refuse_meme_avec_une_casse_differente()
        {
            using var db = TestApplicationDbContext.Create();
            var svc = Service(db);
            await svc.CreatePromoCodeAsync(1, Request("RENTREE"));
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreatePromoCodeAsync(1, Request("rentree")));
            Assert.Contains("already exists", ex.Message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(101)]
        public async Task Une_remise_en_pourcentage_hors_de_1_a_100_est_refusee(decimal value)
        {
            using var db = TestApplicationDbContext.Create();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreatePromoCodeAsync(1, Request("X", value)));
            Assert.Contains("entre 0 et 100", ex.Message);
        }

        [Fact]
        public async Task Une_remise_en_montant_nulle_est_refusee()
        {
            using var db = TestApplicationDbContext.Create();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreatePromoCodeAsync(1, Request("X", 0, "FixedAmount")));
            Assert.Contains("positive", ex.Message);
        }

        [Fact]
        public async Task Une_cible_invalide_est_refusee()
        {
            using var db = TestApplicationDbContext.Create();
            var req = Request("X");
            req.AppliesTo = "magie";
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreatePromoCodeAsync(1, req));
            Assert.Contains("Cibles invalides", ex.Message);
        }

        [Fact]
        public async Task Une_nature_d_offre_invalide_est_refusee()
        {
            using var db = TestApplicationDbContext.Create();
            var req = Request("X");
            req.Promotion = new PromotionFields { Kind = "gratuit" };
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreatePromoCodeAsync(1, req));
            Assert.Contains("Nature d'offre invalide", ex.Message);
        }

        [Fact]
        public async Task Une_duree_de_remise_invalide_est_refusee_et_n_periods_exige_un_nombre_de_periodes()
        {
            using var db = TestApplicationDbContext.Create();
            var bad = Request("A");
            bad.Promotion = new PromotionFields { SubscriptionDuration = "toujours" };
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreatePromoCodeAsync(1, bad));

            var noCount = Request("B");
            noCount.Promotion = new PromotionFields { SubscriptionDuration = PromotionDurations.NPeriods };
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreatePromoCodeAsync(1, noCount));
            Assert.Contains("nombre de périodes", ex.Message);
        }

        [Fact]
        public async Task Un_essai_gratuit_exige_une_duree_et_elle_est_bornee_a_un_an()
        {
            using var db = TestApplicationDbContext.Create();
            var missing = Request("A", 0);
            missing.Promotion = new PromotionFields { Kind = PromotionKinds.TrialDays };
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).CreatePromoCodeAsync(1, missing));
            Assert.Contains("durée de l'essai", ex.Message);
        }

        [Fact]
        public async Task Les_champs_de_promotion_sont_normalises_et_restitues()
        {
            using var db = TestApplicationDbContext.Create();
            var req = Request("ESSAI", 0);
            req.Promotion = new PromotionFields
            {
                Kind = PromotionKinds.TrialDays,
                TrialDays = 9999,
                Audiences = " Student,Parent ",
                BillingPeriods = "",
                PlanIds = new List<int> { 3, 4 },
                EligibleUserIds = new List<int>(),
                Eligibility = PromotionEligibility.NewAccounts,
                NewAccountDays = 0,
                BonusTokens = -5,
                Stackable = true,
            };
            req.AppliesTo = "subscriptions";

            var dto = await Service(db).CreatePromoCodeAsync(1, req);

            Assert.Equal(PromotionKinds.TrialDays, dto.Promotion.Kind);
            Assert.Equal(365, dto.Promotion.TrialDays);
            Assert.Equal("student,parent", dto.Promotion.Audiences);
            Assert.Null(dto.Promotion.BillingPeriods);
            Assert.Equal(new[] { 3, 4 }, dto.Promotion.PlanIds);
            Assert.Null(dto.Promotion.EligibleUserIds);
            Assert.Equal(1, dto.Promotion.NewAccountDays);
            Assert.Equal(0, dto.Promotion.BonusTokens);
            Assert.True(dto.Promotion.Stackable);
            Assert.Equal("subscriptions", dto.AppliesTo);
        }

        [Fact]
        public async Task Les_dates_sans_fuseau_sont_considerees_comme_UTC()
        {
            using var db = TestApplicationDbContext.Create();
            var req = Request("DATES");
            req.ValidFrom = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Unspecified);
            req.ValidUntil = new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Unspecified);
            var dto = await Service(db).CreatePromoCodeAsync(1, req);
            Assert.Equal(DateTimeKind.Utc, dto.ValidFrom.Kind);
            Assert.Equal(8, dto.ValidFrom.Hour);
            Assert.Equal(DateTimeKind.Utc, dto.ValidUntil!.Value.Kind);
        }

        // ── Lecture, activation, modification ───────────────────────────

        [Fact]
        public async Task La_lecture_par_code_ignore_la_casse_et_renvoie_null_si_inconnu()
        {
            using var db = TestApplicationDbContext.Create();
            var svc = Service(db);
            await svc.CreatePromoCodeAsync(1, Request("RENTREE"));
            Assert.Equal("RENTREE", (await svc.GetPromoCodeByCodeAsync("rentree"))!.Code);
            Assert.Null(await svc.GetPromoCodeByCodeAsync("AUCUN"));
        }

        [Fact]
        public async Task La_liste_masque_les_codes_inactifs_sauf_demande()
        {
            using var db = TestApplicationDbContext.Create();
            var svc = Service(db);
            var a = await svc.CreatePromoCodeAsync(1, Request("A"));
            await svc.CreatePromoCodeAsync(1, Request("B"));

            Assert.True(await svc.DeactivatePromoCodeAsync(a.Id));

            Assert.Equal(new[] { "B" }, (await svc.GetAllPromoCodesAsync()).Select(p => p.Code));
            Assert.Equal(2, (await svc.GetAllPromoCodesAsync(includeInactive: true)).Count);

            Assert.True(await svc.ActivatePromoCodeAsync(a.Id));
            Assert.Equal(2, (await svc.GetAllPromoCodesAsync()).Count);
        }

        [Fact]
        public async Task Activer_ou_desactiver_un_code_inconnu_renvoie_faux()
        {
            using var db = TestApplicationDbContext.Create();
            var svc = Service(db);
            Assert.False(await svc.ActivatePromoCodeAsync(404));
            Assert.False(await svc.DeactivatePromoCodeAsync(404));
        }

        [Fact]
        public async Task La_modification_remplace_les_champs_editables_et_conserve_le_code()
        {
            using var db = TestApplicationDbContext.Create();
            var svc = Service(db);
            var created = await svc.CreatePromoCodeAsync(1, Request("RENTREE", 20));

            var updated = await svc.UpdatePromoCodeAsync(created.Id, new UpdatePromoCodeRequest
            {
                Description = "Nouvelle description",
                DiscountValue = 35,
                UsageLimit = 100,
                MinimumPurchase = 2000,
                ApplicableSubjectIds = new List<int> { 1, 2 },
                AppliesTo = "subscriptions",
            });

            Assert.NotNull(updated);
            Assert.Equal("RENTREE", updated!.Code);
            Assert.Equal(35m, updated.DiscountValue);
            Assert.Equal(100, updated.UsageLimit);
            Assert.Equal(2000m, updated.MinimumPurchase);
            Assert.Equal(new[] { 1, 2 }, updated.ApplicableSubjectIds);
            Assert.Equal("subscriptions", updated.AppliesTo);
            Assert.Equal("Nouvelle description", updated.Description);
        }

        [Fact]
        public async Task La_modification_d_un_code_inconnu_renvoie_null_et_un_pourcentage_invalide_est_refuse()
        {
            using var db = TestApplicationDbContext.Create();
            var svc = Service(db);
            Assert.Null(await svc.UpdatePromoCodeAsync(404, new UpdatePromoCodeRequest { DiscountValue = 10 }));

            var created = await svc.CreatePromoCodeAsync(1, Request("RENTREE", 20));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                svc.UpdatePromoCodeAsync(created.Id, new UpdatePromoCodeRequest { DiscountValue = 150 }));
        }

        // ── Rattachement à une commande payée ───────────────────────────

        private static (TestApplicationDbContext Db, PromoCode Promo) SeedOrder(int? usageLimit = 5, int usageCount = 0, bool active = true, DateTime? until = null)
        {
            var db = TestApplicationDbContext.Create();
            db.Users.Add(new User { Id = 1, Email = "eleve@test.local", Role = "student", IsActive = true });
            var promo = new PromoCode
            {
                Id = 1, Code = "RENTREE", DiscountType = "Percentage", DiscountValue = 20,
                ValidFrom = From, ValidUntil = until, UsageLimit = usageLimit, UsageCount = usageCount, IsActive = active,
            };
            db.PromoCodes.Add(promo);
            db.Orders.Add(new Order { Id = 10, UserId = 1, OrderNumber = "WP-10", TotalAmount = 8000, DiscountAmount = 2000 });
            db.SaveChanges();
            return (db, promo);
        }

        [Fact]
        public async Task Le_rattachement_enregistre_l_usage_avec_la_remise_de_la_commande_et_decompte_le_quota()
        {
            var (db, _) = SeedOrder();
            using (db)
            {
                Assert.True(await Service(db).ApplyPromoCodeAsync(1, 10, "rentree"));

                var usage = Assert.Single(db.PromoCodeUsages.ToList());
                Assert.Equal(10, usage.OrderId);
                Assert.Equal(1, usage.UserId);
                Assert.Equal(2000m, usage.DiscountAmount);
                Assert.Equal(1, db.PromoCodes.Single().UsageCount);
                // Le total de la commande n'est jamais recalcule : la remise a deja ete appliquee.
                Assert.Equal(8000m, db.Orders.Single().TotalAmount);
            }
        }

        [Fact]
        public async Task Un_second_rattachement_de_la_meme_commande_est_sans_effet()
        {
            var (db, _) = SeedOrder();
            using (db)
            {
                var svc = Service(db);
                Assert.True(await svc.ApplyPromoCodeAsync(1, 10, "RENTREE"));
                Assert.True(await svc.ApplyPromoCodeAsync(1, 10, "RENTREE"));
                Assert.Single(db.PromoCodeUsages.ToList());
                Assert.Equal(1, db.PromoCodes.Single().UsageCount);
            }
        }

        [Fact]
        public async Task Un_code_inconnu_ou_une_commande_d_un_autre_utilisateur_est_refuse()
        {
            var (db, _) = SeedOrder();
            using (db)
            {
                var svc = Service(db);
                Assert.False(await svc.ApplyPromoCodeAsync(1, 10, "INCONNU"));
                Assert.False(await svc.ApplyPromoCodeAsync(2, 10, "RENTREE"));
                Assert.False(await svc.ApplyPromoCodeAsync(1, 999, "RENTREE"));
                Assert.Empty(db.PromoCodeUsages.ToList());
            }
        }

        [Fact]
        public async Task Un_code_epuise_entre_la_commande_et_le_paiement_garde_la_remise_mais_n_est_pas_decompte()
        {
            var (db, _) = SeedOrder(usageLimit: 1, usageCount: 1);
            using (db)
            {
                Assert.True(await Service(db).ApplyPromoCodeAsync(1, 10, "RENTREE"));
                Assert.Single(db.PromoCodeUsages.ToList());
                Assert.Equal(1, db.PromoCodes.Single().UsageCount);   // pas de depassement du quota
            }
        }

        [Fact]
        public async Task Un_code_desactive_ou_expire_n_est_pas_decompte_mais_l_usage_est_trace()
        {
            var (db, _) = SeedOrder(active: false);
            using (db)
            {
                Assert.True(await Service(db).ApplyPromoCodeAsync(1, 10, "RENTREE"));
                Assert.Equal(0, db.PromoCodes.Single().UsageCount);
                Assert.Single(db.PromoCodeUsages.ToList());
            }

            var (db2, _) = SeedOrder(until: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
            using (db2)
            {
                Assert.True(await Service(db2).ApplyPromoCodeAsync(1, 10, "RENTREE"));
                Assert.Equal(0, db2.PromoCodes.Single().UsageCount);
            }
        }
    }
}
