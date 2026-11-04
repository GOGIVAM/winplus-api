using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// E-mails de masse de l'administration : résolution des adresses par cible
    /// (comptes actifs, non supprimés, sans doublon), comptage des destinataires
    /// et des désabonnés, mise en file (campagne vide close tout de suite),
    /// conversion du texte en HTML échappé. Préférences simulées par Moq.
    /// </summary>
    public class AdminEmailCampaignServiceTests
    {
        private static TestApplicationDbContext Seed()
        {
            var db = TestApplicationDbContext.Create();
            db.Users.AddRange(
                new User { Id = 1, Email = "s1@t.l", Role = "student" },
                new User { Id = 2, Email = "s2@t.l", Role = "student" },
                new User { Id = 3, Email = "s3@t.l", Role = "student", IsActive = false },
                new User { Id = 4, Email = "s4@t.l", Role = "student", IsDeleted = true },
                new User { Id = 5, Email = "t1@t.l", Role = "teacher" },
                new User { Id = 6, Email = "p1@t.l", Role = "parent" },
                new User { Id = 7, Email = "i1@t.l", Role = "institution" });
            db.SaveChanges();
            return db;
        }

        private static Mock<INotificationPreferenceService> Prefs(params string[] optedOut)
        {
            var prefs = new Mock<INotificationPreferenceService>();
            prefs.Setup(p => p.FilterEmailsAsync(It.IsAny<IEnumerable<string>>(), NotificationCategory.Newsletters, It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<string> e, NotificationCategory c, CancellationToken t) => e.Where(x => !optedOut.Contains(x)).ToList());
            return prefs;
        }

        [Theory]
        [InlineData("students", 2)]
        [InlineData("teachers", 1)]
        [InlineData("parents", 1)]
        [InlineData("institutions", 1)]
        [InlineData("all", 5)]
        [InlineData("inconnue", 5)]
        public async Task La_cible_selectionne_les_comptes_actifs_du_bon_role(string target, int expected)
        {
            using var db = Seed();
            Assert.Equal(expected, (await AdminEmailCampaignService.ResolveAddressesAsync(db, target, null)).Count);
        }

        [Fact]
        public async Task La_cible_libre_renvoie_l_adresse_nettoyee_ou_rien()
        {
            using var db = Seed();
            Assert.Equal(new[] { "x@y.z" }, await AdminEmailCampaignService.ResolveAddressesAsync(db, "custom", "  x@y.z "));
            Assert.Empty(await AdminEmailCampaignService.ResolveAddressesAsync(db, "custom", "  "));
            Assert.Empty(await AdminEmailCampaignService.ResolveAddressesAsync(db, "custom", null));
        }

        [Fact]
        public async Task Les_adresses_en_double_ne_comptent_qu_une_fois()
        {
            using var db = Seed();
            db.Users.Add(new User { Id = 8, Email = "s1@t.l", Role = "student" });
            db.SaveChanges();
            Assert.Equal(2, (await AdminEmailCampaignService.ResolveAddressesAsync(db, "students", null)).Count);
        }

        [Fact]
        public async Task Le_comptage_retire_les_desabonnes_a_la_newsletter()
        {
            using var db = Seed();
            var svc = new AdminEmailCampaignService(db, Prefs("s1@t.l").Object);
            var (recipients, optedOut) = await svc.CountRecipientsAsync("students", null);
            Assert.Equal(1, recipients);
            Assert.Equal(1, optedOut);
        }

        [Fact]
        public async Task Une_adresse_libre_ignore_les_preferences()
        {
            using var db = Seed();
            var prefs = Prefs("x@y.z");
            var svc = new AdminEmailCampaignService(db, prefs.Object);
            Assert.Equal((1, 0), await svc.CountRecipientsAsync("custom", "x@y.z"));
            prefs.Verify(p => p.FilterEmailsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<NotificationCategory>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task La_mise_en_file_enregistre_la_campagne_a_envoyer()
        {
            using var db = Seed();
            var svc = new AdminEmailCampaignService(db, Prefs().Object);
            var c = await svc.QueueAsync("students", "ignoré", "  Sujet  ", "Corps", 9);
            Assert.Equal("queued", c.Status);
            Assert.Equal(2, c.RecipientCount);
            Assert.Equal("Sujet", c.Subject);
            Assert.Null(c.CustomEmail);
            Assert.Null(c.FinishedAt);
            Assert.Equal(9, c.CreatedByUserId);
            Assert.Single(db.AdminEmailCampaigns);
        }

        [Fact]
        public async Task Une_campagne_sans_destinataire_est_close_immediatement()
        {
            using var db = Seed();
            var svc = new AdminEmailCampaignService(db, Prefs("t1@t.l").Object);
            var c = await svc.QueueAsync("teachers", null, "S", "B", null);
            Assert.Equal("completed", c.Status);
            Assert.Equal(0, c.RecipientCount);
            Assert.Equal(1, c.OptedOutCount);
            Assert.NotNull(c.FinishedAt);
        }

        [Fact]
        public async Task Une_campagne_a_adresse_libre_conserve_l_adresse_nettoyee()
        {
            using var db = Seed();
            var svc = new AdminEmailCampaignService(db, Prefs().Object);
            var c = await svc.QueueAsync("custom", " test@x.y ", "S", "B", 1);
            Assert.Equal("test@x.y", c.CustomEmail);
            Assert.Equal("queued", c.Status);
        }

        [Fact]
        public void Le_corps_est_echappe_et_les_sauts_de_ligne_conserves()
        {
            var html = AdminEmailCampaignWorker.BuildHtml("Salut <b>toi</b>\nà bientôt & merci");
            Assert.Contains("Salut &lt;b&gt;toi&lt;/b&gt;<br>", html);
            Assert.DoesNotContain("<b>toi</b>", html);
            Assert.Contains("&amp; merci", html);
            Assert.Contains("Se désabonner", html);
        }
    }
}
