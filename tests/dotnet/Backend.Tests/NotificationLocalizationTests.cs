using System;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Textes de notification bilingues : choix de la langue (« en », « en-GB »
    /// donnent l'anglais, tout le reste le français), concaténation, lecture de
    /// la langue du compte avec repli français, et envoi localisé.
    /// </summary>
    public class NotificationLocalizationTests
    {
        [Theory]
        [InlineData("en", true)]
        [InlineData("EN-gb", true)]
        [InlineData("  en-US ", true)]
        [InlineData("fr", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsEnglish_reconnait_toute_valeur_anglaise(string? locale, bool expected) =>
            Assert.Equal(expected, NotificationLocales.IsEnglish(locale));

        [Fact]
        public void LocalizedText_choisit_la_langue_et_se_concatene()
        {
            var sum = new LocalizedText("Bonjour", "Hello") + new LocalizedText(" Paul", " Paul");
            Assert.Equal("Bonjour Paul", sum.For("fr"));
            Assert.Equal("Hello Paul", sum.For("en"));
            Assert.Equal("Bonjour Paul", sum.For(null));
        }

        [Fact]
        public async Task ForUserAsync_retombe_sur_le_francais_sans_base_sans_utilisateur_ou_compte_inconnu()
        {
            using var db = TestApplicationDbContext.Create();
            Assert.Equal("fr", await NotificationLocales.ForUserAsync(null, 1));
            Assert.Equal("fr", await NotificationLocales.ForUserAsync(db, null));
            Assert.Equal("fr", await NotificationLocales.ForUserAsync(db, 4242));
        }

        [Fact]
        public async Task ForUserAsync_lit_la_langue_du_compte()
        {
            using var db = TestApplicationDbContext.Create();
            db.Users.Add(new User { Id = 1, Email = "e@t.local", Role = "student", Locale = "en-GB" });
            db.Users.Add(new User { Id = 2, Email = "f@t.local", Role = "student", Locale = "fr" });
            db.SaveChanges();
            Assert.Equal("en", await NotificationLocales.ForUserAsync(db, 1));
            Assert.Equal("fr", await NotificationLocales.ForUserAsync(db, 2));
        }

        [Fact]
        public async Task PublishLocalized_envoie_le_texte_dans_la_langue_du_destinataire()
        {
            using var db = TestApplicationDbContext.Create();
            db.Users.Add(new User { Id = 1, Email = "e@t.local", Role = "student", Locale = "en" });
            db.SaveChanges();
            var ntfy = new Mock<INtfyService>();
            await ntfy.Object.PublishLocalizedAsync(db, "topic", new LocalizedText("Titre", "Title"), new LocalizedText("Corps", "Body"), userId: 1);
            ntfy.Verify(n => n.PublishAsync("topic", "Title", "Body", "default", null, 1, "General", null, null), Times.Once);
        }
    }
}
