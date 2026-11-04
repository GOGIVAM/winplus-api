using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Réglages de plateforme : validation et normalisation des saisies par type
    /// et bornes, priorité base puis configuration puis défaut, valeurs hors
    /// bornes de la configuration ramenées dans les bornes, et modification par
    /// l'administration (refus, écriture, audit, invalidation du cache).
    /// </summary>
    public class PlatformSettingsServiceTests
    {
        private static IConfiguration Config(params (string, string)[] kv) =>
            new ConfigurationBuilder().AddInMemoryCollection(kv.Select(p => new KeyValuePair<string, string?>(p.Item1, p.Item2))).Build();

        private static PlatformSettings Settings(Dictionary<string, string>? values = null, IConfiguration? cfg = null) =>
            PlatformSettings.FromValues(values ?? new Dictionary<string, string>(), cfg);

        [Theory]
        [InlineData("true", "true")]
        [InlineData("TRUE", "true")]
        [InlineData("1", "true")]
        [InlineData("false", "false")]
        [InlineData("0", "false")]
        public void Normalize_accepte_les_booleens(string raw, string expected)
        {
            var def = PlatformSettingDefinitions.Find(PlatformSettingDefinitions.SignupEnabled)!;
            Assert.Equal((expected, (string?)null), PlatformSettingDefinitions.Normalize(def, raw));
        }

        [Fact]
        public void Normalize_refuse_un_booleen_invalide()
        {
            var def = PlatformSettingDefinitions.Find(PlatformSettingDefinitions.SignupEnabled)!;
            var (v, e) = PlatformSettingDefinitions.Normalize(def, "peut-etre");
            Assert.Null(v);
            Assert.NotNull(e);
        }

        [Theory]
        [InlineData("255", false)]
        [InlineData("256", true)]
        [InlineData("8000", true)]
        [InlineData("8001", false)]
        [InlineData("abc", false)]
        [InlineData("", false)]
        [InlineData("1000.5", false)]
        public void Normalize_entier_respecte_les_bornes_inclusives(string raw, bool ok)
        {
            var def = PlatformSettingDefinitions.Find(PlatformSettingDefinitions.WinAiChatMaxTokens)!;
            var (v, e) = PlatformSettingDefinitions.Normalize(def, raw);
            Assert.Equal(ok, e == null);
            Assert.Equal(ok, v != null);
        }

        [Theory]
        [InlineData("0,7", "0.7")]
        [InlineData(" 1.5 ", "1.5")]
        [InlineData("0", "0")]
        public void Normalize_decimal_accepte_la_virgule_et_les_espaces(string raw, string expected)
        {
            var def = PlatformSettingDefinitions.Find(PlatformSettingDefinitions.WinAiChatTemperature)!;
            Assert.Equal(expected, PlatformSettingDefinitions.Normalize(def, raw).Value);
        }

        [Theory]
        [InlineData("1.6")]
        [InlineData("-0.1")]
        [InlineData("x")]
        public void Normalize_decimal_refuse_hors_bornes_ou_non_numerique(string raw)
        {
            var def = PlatformSettingDefinitions.Find(PlatformSettingDefinitions.WinAiChatTemperature)!;
            Assert.NotNull(PlatformSettingDefinitions.Normalize(def, raw).Error);
        }

        [Fact]
        public void Normalize_texte_limite_la_longueur_et_accepte_null_comme_vide()
        {
            var def = PlatformSettingDefinitions.Find(PlatformSettingDefinitions.MaintenanceMessage)!;
            Assert.Equal("", PlatformSettingDefinitions.Normalize(def, null).Value);
            Assert.Equal("ok", PlatformSettingDefinitions.Normalize(def, "  ok  ").Value);
            Assert.NotNull(PlatformSettingDefinitions.Normalize(def, new string('a', 301)).Error);
            Assert.Null(PlatformSettingDefinitions.Normalize(def, new string('a', 300)).Error);
        }

        [Fact]
        public void Find_renvoie_null_pour_une_cle_inconnue_et_les_cles_sont_uniques()
        {
            Assert.Null(PlatformSettingDefinitions.Find("inconnue"));
            Assert.Equal(PlatformSettingDefinitions.All.Count, PlatformSettingDefinitions.All.Select(d => d.Key).Distinct().Count());
        }

        [Fact]
        public void Sans_base_ni_configuration_le_defaut_s_applique()
        {
            var s = Settings();
            Assert.True(s.GetBool(PlatformSettingDefinitions.SignupEnabled));
            Assert.False(s.GetBool(PlatformSettingDefinitions.MaintenanceMode));
            Assert.Equal(6000, s.GetInt(PlatformSettingDefinitions.WinAiChatMaxTokens));
            Assert.Equal(0.7m, s.GetDecimal(PlatformSettingDefinitions.WinAiChatTemperature));
            Assert.False(s.IsOverridden(PlatformSettingDefinitions.SignupEnabled));
        }

        [Fact]
        public void La_valeur_en_base_l_emporte_sur_la_configuration()
        {
            var s = Settings(new() { [PlatformSettingDefinitions.WinAiChatMaxTokens] = "1000" }, Config(("Chatbot:MaxTokens", "2000")));
            Assert.Equal(1000, s.GetInt(PlatformSettingDefinitions.WinAiChatMaxTokens));
            Assert.True(s.IsOverridden(PlatformSettingDefinitions.WinAiChatMaxTokens));
        }

        [Fact]
        public void Une_valeur_invalide_en_base_est_ignoree_au_profit_de_la_configuration()
        {
            var s = Settings(new() { [PlatformSettingDefinitions.WinAiChatMaxTokens] = "n'importe quoi" }, Config(("Chatbot:MaxTokens", "2000")));
            Assert.Equal(2000, s.GetInt(PlatformSettingDefinitions.WinAiChatMaxTokens));
        }

        [Fact]
        public void Une_configuration_hors_bornes_est_ramenee_dans_les_bornes()
        {
            Assert.Equal(8000, Settings(cfg: Config(("Chatbot:MaxTokens", "99999"))).GetInt(PlatformSettingDefinitions.WinAiChatMaxTokens));
            Assert.Equal(256, Settings(cfg: Config(("Chatbot:MaxTokens", "10"))).GetInt(PlatformSettingDefinitions.WinAiChatMaxTokens));
        }

        [Fact]
        public void Une_configuration_non_numerique_retombe_sur_le_defaut()
        {
            Assert.Equal(6000, Settings(cfg: Config(("Chatbot:MaxTokens", "beaucoup"))).GetInt(PlatformSettingDefinitions.WinAiChatMaxTokens));
        }

        [Fact]
        public void Une_cle_inconnue_leve_une_erreur()
        {
            Assert.Throws<ArgumentException>(() => Settings().GetRaw("inconnue"));
        }

        // ── Administration ──────────────────────────────────────────────────

        private static ClaimsPrincipal Admin(int id) =>
            new(new ClaimsIdentity(new[] { new Claim("user_id", id.ToString()) }, "test"));

        [Fact]
        public async Task La_mise_a_jour_refuse_une_cle_inconnue_ou_une_valeur_invalide_sans_rien_ecrire()
        {
            using var db = TestApplicationDbContext.Create();
            var audit = new Mock<IAdminAuditService>();
            var svc = new PlatformSettingsAdminService(db, Settings(), audit.Object);
            Assert.False((await svc.UpdateAsync("inconnue", "1", Admin(1), null)).Ok);
            var r = await svc.UpdateAsync(PlatformSettingDefinitions.WinAiChatMaxTokens, "10", Admin(1), null);
            Assert.False(r.Ok);
            Assert.NotNull(r.Error);
            Assert.Empty(db.PlatformSettings);
            audit.Verify(a => a.Add(It.IsAny<ClaimsPrincipal?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task La_mise_a_jour_cree_puis_modifie_la_ligne_et_ecrit_l_audit()
        {
            using var db = TestApplicationDbContext.Create();
            var audit = new Mock<IAdminAuditService>();
            audit.Setup(a => a.Add(It.IsAny<ClaimsPrincipal?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string?>()))
                .Returns(new AdminAuditLog());
            var svc = new PlatformSettingsAdminService(db, Settings(), audit.Object);
            var key = PlatformSettingDefinitions.WinAiChatMaxTokens;

            Assert.True((await svc.UpdateAsync(key, "1000", Admin(7), "1.2.3.4")).Ok);
            Assert.True((await svc.UpdateAsync(key, "1200", Admin(8), "1.2.3.4")).Ok);

            var row = Assert.Single(db.PlatformSettings);
            Assert.Equal("1200", row.Value);
            Assert.Equal("int", row.ValueType);
            Assert.Equal(8, row.UpdatedByUserId);
            audit.Verify(a => a.Add(It.IsAny<ClaimsPrincipal?>(), "1.2.3.4", AdminAuditActions.SettingChanged, "Setting", key, It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string?>()), Times.Exactly(2));
        }

        [Fact]
        public async Task La_liste_renvoie_un_element_par_definition()
        {
            using var db = TestApplicationDbContext.Create();
            var svc = new PlatformSettingsAdminService(db, Settings(), new Mock<IAdminAuditService>().Object);
            Assert.Equal(PlatformSettingDefinitions.All.Count, (await svc.ListAsync()).Count);
        }
    }
}
