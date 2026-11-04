using System.Linq;
using Backend.Utils;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Normalisation des numéros et détection de l'opérateur Mobile Money (lot
    /// 10.8) : un seul endroit pour ce qui était copié dans cinq services. Un
    /// retrait n'est accepté que vers un mobile camerounais ; le canal NotchPay
    /// dépend du préfixe (Orange ou MTN). Fonctions pures, aucune base requise.
    /// </summary>
    public class PhoneNumbersTests
    {
        // ── Chiffres et format international ────────────────────────────

        [Theory]
        [InlineData("+237 677-12.34.56", "237677123456")]
        [InlineData("(677) 123 456", "677123456")]
        [InlineData("abc", "")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void Digits_ne_garde_que_les_chiffres(string? input, string expected)
        {
            Assert.Equal(expected, PhoneNumbers.Digits(input));
        }

        [Theory]
        [InlineData("677123456", "237677123456")]
        [InlineData("677 12 34 56", "237677123456")]
        [InlineData("237677123456", "237677123456")]
        [InlineData("+237677123456", "237677123456")]
        [InlineData("+241012345 67", "24101234567")]
        [InlineData(null, "")]
        public void Un_numero_local_de_neuf_chiffres_recoit_l_indicatif_237_les_autres_sont_inchanges(string? input, string expected)
        {
            Assert.Equal(expected, PhoneNumbers.ToE164Digits(input));
        }

        // ── Retrait vers un mobile camerounais ──────────────────────────

        [Theory]
        [InlineData("677123456", "237677123456")]
        [InlineData("237677123456", "237677123456")]
        [InlineData("+237 677 12 34 56", "237677123456")]
        [InlineData("00237677123456", "237677123456")]
        [InlineData("655000000", "237655000000")]
        public void Un_mobile_camerounais_valide_est_renvoye_au_format_237_plus_neuf_chiffres(string input, string expected)
        {
            Assert.Equal(expected, PhoneNumbers.CameroonMobileOrNull(input));
        }

        [Theory]
        [InlineData("577123456")]        // 9 chiffres mais ne commence pas par 6
        [InlineData("67712345")]         // trop court
        [InlineData("6771234567")]       // trop long sans indicatif
        [InlineData("237577123456")]     // indicatif 237 puis un numero fixe
        [InlineData("241012345678")]     // autre pays
        [InlineData("+33612345678")]
        [InlineData("")]
        [InlineData(null)]
        public void Un_numero_qui_n_est_pas_un_mobile_camerounais_est_refuse(string? input)
        {
            Assert.Null(PhoneNumbers.CameroonMobileOrNull(input));
        }

        // ── Canal NotchPay ──────────────────────────────────────────────

        [Theory]
        [InlineData("655123456")]
        [InlineData("659123456")]
        [InlineData("690123456")]
        [InlineData("699123456")]
        [InlineData("+237690123456")]
        [InlineData("237655123456")]
        public void Les_prefixes_Orange_donnent_le_canal_orange(string phone)
        {
            Assert.Equal("cm.orange", PhoneNumbers.CameroonMobileMoneyChannel(phone));
        }

        [Theory]
        [InlineData("677123456")]
        [InlineData("650123456")]        // hors liste Orange : MTN
        [InlineData("670123456")]
        [InlineData("+237677123456")]
        [InlineData("680123456")]
        public void Les_autres_prefixes_donnent_le_canal_mtn(string phone)
        {
            Assert.Equal("cm.mtn", PhoneNumbers.CameroonMobileMoneyChannel(phone));
        }

        [Theory]
        [InlineData("")]
        [InlineData("12")]
        [InlineData(null)]
        public void Un_numero_trop_court_ou_absent_retombe_sur_mtn(string? phone)
        {
            Assert.Equal("cm.mtn", PhoneNumbers.CameroonMobileMoneyChannel(phone));
        }

        // ── Pays d'un numéro ────────────────────────────────────────────

        [Theory]
        [InlineData("677123456", "CM")]                  // local de 9 chiffres : camerounais
        [InlineData("+237677123456", "CM")]
        [InlineData("00237677123456", "CM")]
        [InlineData("+241 01 23 45 67", "GA")]
        [InlineData("0024101234567", "GA")]
        [InlineData("+242 061234567", "CG")]
        [InlineData("+235 12345678", "TD")]
        [InlineData("+236 12345678", "CF")]
        [InlineData("+240 123456789", "GQ")]
        public void Le_pays_est_reconnu_parmi_les_pays_CEMAC(string phone, string expected)
        {
            Assert.Equal(expected, PhoneNumbers.CountryOf(phone));
        }

        [Theory]
        [InlineData("+33612345678")]      // hors CEMAC
        [InlineData("+2411234")]          // longueur nationale incorrecte
        [InlineData("+2376771234")]       // trop court
        [InlineData("12345678")]          // local de 8 chiffres sans indicatif
        [InlineData("")]
        [InlineData(null)]
        public void Un_numero_hors_CEMAC_ou_de_longueur_incorrecte_n_a_pas_de_pays(string? phone)
        {
            Assert.Null(PhoneNumbers.CountryOf(phone));
        }

        [Fact]
        public void Un_numero_de_neuf_chiffres_precede_d_un_plus_n_est_pas_presume_camerounais()
        {
            // Le « + » annonce un format international : sans indicatif reconnu, pas de pays.
            Assert.Null(PhoneNumbers.CountryOf("+677123456"));
        }

        // ── Table des pays ──────────────────────────────────────────────

        [Fact]
        public void La_table_CEMAC_contient_les_six_pays_avec_des_indicatifs_distincts()
        {
            Assert.Equal(6, PhoneNumbers.Cemac.Count);
            Assert.Equal(6, PhoneNumbers.Cemac.Select(c => c.Code).Distinct().Count());
            Assert.Equal(6, PhoneNumbers.Cemac.Select(c => c.DialCode).Distinct().Count());
            var cm = PhoneNumbers.Cemac.Single(c => c.Code == PhoneNumbers.Cameroon);
            Assert.Equal("237", cm.DialCode);
            Assert.Equal(9, cm.NationalLength);
        }
    }
}
