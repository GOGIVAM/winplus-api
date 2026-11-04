using Backend.Utils;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Censure des numéros de téléphone dans la messagerie et le forum : un
    /// numéro camerounais (avec ou sans indicatif) ou international explicite
    /// (+) est masqué, mais un grand nombre pédagogique ne l'est jamais. Fonction
    /// pure, aucune base requise.
    /// </summary>
    public class ContentSanitizerTests
    {
        private const string Mask = "[numéro masqué]";

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Un_texte_vide_ou_nul_devient_une_chaine_vide(string? text)
        {
            Assert.Equal(string.Empty, ContentSanitizer.CensorPhoneNumbers(text));
        }

        [Fact]
        public void Un_texte_sans_numero_est_renvoye_inchange()
        {
            const string text = "Rendez-vous demain pour corriger l'exercice 12 du chapitre 3.";
            Assert.Equal(text, ContentSanitizer.CensorPhoneNumbers(text));
        }

        [Theory]
        [InlineData("Appelle-moi au 677123456 ce soir", "Appelle-moi au " + Mask + " ce soir")]
        [InlineData("677 12 34 56", Mask)]
        [InlineData("677.12.34.56", Mask)]
        [InlineData("677-12-34-56", Mask)]
        [InlineData("237677123456", Mask)]
        [InlineData("+237677123456", Mask)]
        [InlineData("+237 677 12 34 56", Mask)]
        [InlineData("237 677123456", Mask)]
        public void Un_numero_camerounais_est_masque_avec_ou_sans_indicatif_et_separateurs(string input, string expected)
        {
            Assert.Equal(expected, ContentSanitizer.CensorPhoneNumbers(input));
        }

        [Theory]
        [InlineData("Contact: +33 6 12 34 56 78 merci", "Contact: " + Mask + " merci")]
        [InlineData("+241 01 23 45 67", Mask)]
        [InlineData("+14155552671", Mask)]
        public void Un_numero_international_explicite_est_masque(string input, string expected)
        {
            Assert.Equal(expected, ContentSanitizer.CensorPhoneNumbers(input));
        }

        [Fact]
        public void Plusieurs_numeros_dans_un_meme_message_sont_tous_masques()
        {
            var result = ContentSanitizer.CensorPhoneNumbers("Moi 677123456 ou mon frere +33 6 12 34 56 78.");
            Assert.Equal("Moi " + Mask + " ou mon frere " + Mask + ".", result);
        }

        [Theory]
        [InlineData("Le resultat est 6000000")]
        [InlineData("Population : 12345678901234")]
        [InlineData("Le code 6123456789 a dix chiffres")]
        [InlineData("Matricule 5771234")]
        public void Un_grand_nombre_sans_forme_de_numero_n_est_pas_censure(string input)
        {
            Assert.Equal(input, ContentSanitizer.CensorPhoneNumbers(input));
        }

        [Fact]
        public void Un_numero_local_qui_ne_commence_pas_par_6_n_est_pas_masque()
        {
            Assert.Equal("Tel 222123456", ContentSanitizer.CensorPhoneNumbers("Tel 222123456"));
        }
    }
}
