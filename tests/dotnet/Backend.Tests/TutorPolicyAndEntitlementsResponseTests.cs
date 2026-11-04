using System;
using System.Collections.Generic;
using System.Linq;
using Backend.Controllers;
using Backend.Services;
using Backend.Services.Entitlements;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Réponses publiques construites sans serveur : politique de tutorat
    /// (réglages bornés, valeurs injectées dans le texte, sections stables) et
    /// droits de l'utilisateur (clés internes de tokens jamais exposées, valeurs
    /// typées, plan et multiplicateur IA).
    /// </summary>
    public class TutorPolicyAndEntitlementsResponseTests
    {
        private static IConfiguration Config(params (string, string)[] kv) =>
            new ConfigurationBuilder().AddInMemoryCollection(kv.Select(p => new KeyValuePair<string, string?>(p.Item1, p.Item2))).Build();

        private static object? P(object o, string name) => o.GetType().GetProperty(name)!.GetValue(o);

        // ── Réglages de la politique ──────────────────────────────────────────

        [Fact]
        public void Sans_configuration_les_reglages_par_defaut_s_appliquent()
        {
            var s = TutorPolicySettings.From(Config());
            Assert.Equal(24, s.DisputeWindowHours);
            Assert.Equal(3m, s.NotchPayFeePercent);
            Assert.Equal(TimeSpan.FromHours(24), s.DisputeWindow);
            Assert.Equal(s, TutorPolicySettings.Default);
        }

        [Theory]
        [InlineData("0", 1)]
        [InlineData("-5", 1)]
        [InlineData("500", 168)]
        [InlineData("48", 48)]
        public void Le_delai_de_contestation_configure_est_borne_de_1_a_168_heures(string raw, int expected)
        {
            Assert.Equal(expected, TutorPolicySettings.From(Config(("TutorBooking:DisputeWindowHours", raw))).DisputeWindowHours);
        }

        [Theory]
        [InlineData("-2", 0)]
        [InlineData("99", 10)]
        [InlineData("2.5", 2.5)]
        public void Les_frais_de_paiement_configures_sont_bornes_de_0_a_10_pour_cent(string raw, double expected)
        {
            Assert.Equal((decimal)expected, TutorPolicySettings.From(Config(("TutorBooking:NotchPayFeePercent", raw))).NotchPayFeePercent);
        }

        // ── Document de politique ─────────────────────────────────────────────

        [Fact]
        public void Le_document_reprend_les_reglages_les_bornes_et_la_version()
        {
            var dto = TutorPolicyDocument.Build(new TutorPolicySettings(48, 2.5m));
            Assert.Equal(48, dto.DisputeWindowHours);
            Assert.Equal(2.5m, dto.NotchPayFeePercent);
            Assert.Equal(TutorPolicy.Version, dto.Version);
            Assert.Equal(TutorPolicy.FullRefundHoursMin, dto.Bounds.FullRefundHoursMin);
            Assert.Equal(TutorPolicy.DefaultNoRefundHours, dto.Bounds.DefaultNoRefundHours);
        }

        [Fact]
        public void Les_sections_ont_des_identifiants_uniques_et_du_contenu()
        {
            var dto = TutorPolicyDocument.Build(TutorPolicySettings.Default);
            Assert.NotEmpty(dto.Sections);
            Assert.Equal(dto.Sections.Count, dto.Sections.Select(s => s.Id).Distinct().Count());
            Assert.All(dto.Sections, s =>
            {
                Assert.False(string.IsNullOrWhiteSpace(s.Title));
                Assert.NotEmpty(s.Paragraphs);
                Assert.All(s.Paragraphs, p => Assert.False(string.IsNullOrWhiteSpace(p)));
            });
        }

        [Fact]
        public void Les_frais_et_le_delai_sont_injectes_dans_le_texte()
        {
            var dto = TutorPolicyDocument.Build(new TutorPolicySettings(36, 2.5m));
            var text = string.Join("\n", dto.Sections.SelectMany(s => s.Paragraphs));
            Assert.Contains("2,5 %", text);
            Assert.Contains("36 h après chaque séance", text);
            Assert.DoesNotContain("{", text);
        }

        // ── Droits de l'utilisateur ───────────────────────────────────────────

        private static UserEntitlements Ent(Dictionary<string, string> values, ActivePlanInfo? plan = null, long personal = 200000, long parentBonus = 0, long licenseBonus = 0) =>
            new()
            {
                UserId = 1, Role = "student", Audience = "student", Values = values, Plan = plan,
                Sources = new List<EntitlementSource> { new("plan", "Plan Plus") },
                PersonalReferenceTokens = personal, ParentBonusTokens = parentBonus, LicenseBonusTokens = licenseBonus, FreeReferenceTokens = 200000,
            };

        [Fact]
        public void Les_cles_internes_de_tokens_et_les_cles_inconnues_ne_sont_pas_exposees()
        {
            var res = MeEntitlementsController.ToResponse(Ent(new()
            {
                [EntitlementCatalog.AiReferenceTokens] = "999999",
                ["cle.inventee"] = "true",
                [EntitlementCatalog.CatalogAccess] = "true",
            }));
            var values = (Dictionary<string, object>)P(res, "entitlements")!;
            Assert.Equal(new[] { EntitlementCatalog.CatalogAccess }, values.Keys);
        }

        [Fact]
        public void Les_valeurs_sont_typees_booleen_ou_entier()
        {
            var res = MeEntitlementsController.ToResponse(Ent(new()
            {
                [EntitlementCatalog.CatalogAccess] = "true",
                [EntitlementCatalog.CatalogPreviewPages] = "7",
            }));
            var values = (Dictionary<string, object>)P(res, "entitlements")!;
            Assert.Equal(true, values[EntitlementCatalog.CatalogAccess]);
            Assert.Equal(7L, values[EntitlementCatalog.CatalogPreviewPages]);
        }

        [Fact]
        public void Sans_plan_la_reponse_n_a_pas_de_plan_et_le_multiplicateur_vaut_un()
        {
            var res = MeEntitlementsController.ToResponse(Ent(new()));
            Assert.Null(P(res, "plan"));
            Assert.Equal(1, P(res, "aiMultiplier"));
            Assert.Equal(false, P(res, "aiHasParentBonus"));
            Assert.Equal(false, P(res, "aiHasLicenseBonus"));
            Assert.Equal(EntitlementCatalog.Unlimited, P(res, "unlimited"));
        }

        [Fact]
        public void Le_plan_actif_et_les_bonus_sont_resumes_sans_exposer_les_tokens()
        {
            var plan = new ActivePlanInfo(5, 2, "plus", "Plus", "student", 9, 1, "monthly", "active", new DateTime(2026, 1, 1), new DateTime(2026, 2, 1), null);
            var res = MeEntitlementsController.ToResponse(Ent(new(), plan, personal: 400000, parentBonus: 200000, licenseBonus: 200000));
            Assert.Equal(4, P(res, "aiMultiplier"));
            Assert.Equal(true, P(res, "aiHasParentBonus"));
            Assert.Equal(true, P(res, "aiHasLicenseBonus"));
            var planObj = P(res, "plan")!;
            Assert.Equal("Plus", P(planObj, "name"));
            Assert.Equal("active", P(planObj, "status"));
            Assert.Null(res.GetType().GetProperty("personalReferenceTokens"));
            Assert.Null(res.GetType().GetProperty("totalReferenceTokens"));
        }
    }
}
