using System.Linq;
using System.Threading.Tasks;
using Backend.Models.DTOs;
using Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Préférences de notification et de confidentialité : valeurs par défaut
    /// sans enregistrement (sans rien écrire), création au premier enregistrement,
    /// mise à jour ensuite (une seule ligne par utilisateur), isolation.
    /// </summary>
    public class SettingsServiceTests
    {
        private static (TestApplicationDbContext Db, SettingsService Svc) Build()
        {
            var db = TestApplicationDbContext.Create();
            return (db, new SettingsService(db, NullLogger<SettingsService>.Instance));
        }

        [Fact]
        public async Task Les_notifications_par_defaut_activent_tout_sauf_les_promotions_et_n_ecrivent_rien()
        {
            var (db, svc) = Build();
            var s = await svc.GetNotificationSettingsAsync(1);
            Assert.True(s.EmailNotifications);
            Assert.True(s.PushNotifications);
            Assert.True(s.CourseCommunity);
            Assert.False(s.Promotions);
            Assert.True(s.Newsletters);
            Assert.True(s.LearningReminders);
            Assert.Empty(db.UserNotificationSettings);
        }

        [Fact]
        public async Task La_confidentialite_par_defaut_cache_la_progression_et_n_ecrit_rien()
        {
            var (db, svc) = Build();
            var s = await svc.GetPrivacySettingsAsync(1);
            Assert.True(s.ProfileVisible);
            Assert.False(s.ShowProgressPublic);
            Assert.True(s.AllowMessages);
            Assert.True(s.AllowFriends);
            Assert.Empty(db.UserPrivacySettings);
        }

        [Fact]
        public async Task Le_premier_enregistrement_cree_la_ligne_et_le_second_la_modifie()
        {
            var (db, svc) = Build();
            await svc.SaveNotificationSettingsAsync(1, new NotificationSettingsDto { Promotions = true, EmailNotifications = false });
            await svc.SaveNotificationSettingsAsync(1, new NotificationSettingsDto { Promotions = false, EmailNotifications = false, PushNotifications = true });
            var row = Assert.Single(db.UserNotificationSettings);
            Assert.False(row.Promotions);
            Assert.False(row.EmailNotifications);
            Assert.True(row.PushNotifications);
            var read = await svc.GetNotificationSettingsAsync(1);
            Assert.False(read.EmailNotifications);
        }

        [Fact]
        public async Task Les_preferences_d_un_utilisateur_n_affectent_pas_un_autre()
        {
            var (db, svc) = Build();
            await svc.SavePrivacySettingsAsync(1, new PrivacySettingsDto { ProfileVisible = false, ShowProgressPublic = true });
            var other = await svc.GetPrivacySettingsAsync(2);
            Assert.True(other.ProfileVisible);
            Assert.False(other.ShowProgressPublic);
            Assert.Single(db.UserPrivacySettings);
        }

        [Fact]
        public async Task L_enregistrement_de_confidentialite_est_idempotent_et_renvoie_les_valeurs_enregistrees()
        {
            var (db, svc) = Build();
            var dto = new PrivacySettingsDto { ProfileVisible = false, ShowProgressPublic = true, AllowMessages = false, AllowFriends = false };
            var a = await svc.SavePrivacySettingsAsync(5, dto);
            var b = await svc.SavePrivacySettingsAsync(5, dto);
            Assert.Equal(5, a.UserId);
            Assert.False(a.ProfileVisible);
            Assert.True(b.ShowProgressPublic);
            Assert.False(b.AllowMessages);
            Assert.Single(db.UserPrivacySettings.Where(p => p.UserId == 5));
        }
    }
}
