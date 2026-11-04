using System;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Sessions d'appareil : création active pour 30 jours, liste des sessions
    /// actives seulement (la plus récente d'abord), suppression logique,
    /// prolongation de l'expiration quand elle approche, isolation par utilisateur.
    /// Les dates relatives à l'horloge système sont comparées avec une tolérance.
    /// </summary>
    public class SessionServiceTests
    {
        private const int U = 4001, V = 4002;

        private static (TestApplicationDbContext Db, SessionService Svc) Build()
        {
            var db = TestApplicationDbContext.Create();
            return (db, new SessionService(db, NullLogger<SessionService>.Instance));
        }

        private static UserSession Row(int userId, bool active, DateTime lastActivity, DateTime expires) => new()
        {
            UserId = userId, IsActive = active, LastActivityAt = lastActivity, ExpiresAt = expires, CreatedAt = lastActivity,
            DeviceName = "tel", DeviceType = "mobile", IpAddress = "1.1.1.1", UserAgent = "ua", Location = "Douala",
        };

        [Fact]
        public async Task La_creation_ouvre_une_session_active_de_trente_jours()
        {
            var (db, svc) = Build();
            var dto = await svc.CreateSessionAsync(U, "PC", "desktop", "10.0.0.1", "Chrome", "Yaoundé");
            Assert.True(dto.IsActive);
            Assert.Equal("PC", dto.DeviceName);
            Assert.InRange((dto.ExpiresAt!.Value - DateTime.UtcNow).TotalDays, 29.9, 30.1);
            Assert.Single(db.UserSessions);
        }

        [Fact]
        public async Task La_liste_ne_contient_que_les_sessions_actives_de_l_utilisateur_les_plus_recentes_d_abord()
        {
            var (db, svc) = Build();
            var t = new DateTime(2026, 1, 10);
            db.UserSessions.AddRange(
                Row(U, true, t.AddDays(-2), t.AddDays(20)),
                Row(U, true, t, t.AddDays(20)),
                Row(U, false, t.AddDays(1), t.AddDays(20)),
                Row(V, true, t, t.AddDays(20)));
            db.SaveChanges();
            var list = await svc.GetUserSessionsAsync(U);
            Assert.Equal(2, list.Count);
            Assert.True(list[0].LastActivityAt > list[1].LastActivityAt);
            Assert.All(list, s => Assert.Equal(U, s.UserId));
        }

        [Fact]
        public async Task La_suppression_desactive_la_session_sans_la_supprimer_et_ignore_une_session_inconnue()
        {
            var (db, svc) = Build();
            var created = await svc.CreateSessionAsync(U, "PC", "desktop", "ip", "ua", "loc");
            await svc.DeleteSessionAsync(created.Id);
            await svc.DeleteSessionAsync(9999);
            Assert.False(db.UserSessions.Single().IsActive);
        }

        [Fact]
        public async Task La_deconnexion_generale_ne_touche_que_les_sessions_de_l_utilisateur()
        {
            var (db, svc) = Build();
            await svc.CreateSessionAsync(U, "a", "d", "ip", "ua", "l");
            await svc.CreateSessionAsync(U, "b", "d", "ip", "ua", "l");
            await svc.CreateSessionAsync(V, "c", "d", "ip", "ua", "l");
            await svc.DeleteAllSessionsAsync(U);
            Assert.Equal(0, db.UserSessions.Count(s => s.UserId == U && s.IsActive));
            Assert.Equal(1, db.UserSessions.Count(s => s.UserId == V && s.IsActive));
        }

        [Fact]
        public async Task L_activite_prolonge_une_session_qui_expire_dans_moins_de_sept_jours()
        {
            var (db, svc) = Build();
            db.UserSessions.Add(Row(U, true, DateTime.UtcNow.AddDays(-25), DateTime.UtcNow.AddDays(2)));
            db.SaveChanges();
            await svc.UpdateSessionActivityAsync(db.UserSessions.Single().Id);
            var s = db.UserSessions.Single();
            Assert.InRange((s.ExpiresAt!.Value - DateTime.UtcNow).TotalDays, 29.9, 30.1);
            Assert.InRange((DateTime.UtcNow - s.LastActivityAt).TotalMinutes, 0, 5);
        }

        [Fact]
        public async Task L_activite_ne_modifie_pas_l_expiration_lointaine_ni_une_session_inactive()
        {
            var (db, svc) = Build();
            var far = DateTime.UtcNow.AddDays(20);
            var oldActivity = DateTime.UtcNow.AddDays(-3);
            db.UserSessions.Add(Row(U, true, oldActivity, far));
            db.UserSessions.Add(Row(U, false, oldActivity, DateTime.UtcNow.AddDays(1)));
            db.SaveChanges();
            foreach (var s in db.UserSessions.ToList()) await svc.UpdateSessionActivityAsync(s.Id);
            var active = db.UserSessions.Single(s => s.IsActive);
            var inactive = db.UserSessions.Single(s => !s.IsActive);
            Assert.Equal(far, active.ExpiresAt);
            Assert.Equal(oldActivity, inactive.LastActivityAt);
        }

        [Fact]
        public async Task Lire_une_session_renvoie_ses_donnees_ou_rien_si_inconnue()
        {
            var (_, svc) = Build();
            var created = await svc.CreateSessionAsync(U, "PC", "desktop", "ip", "ua", "loc");
            var read = await svc.GetSessionByIdAsync(created.Id);
            Assert.Equal("PC", read.DeviceName);
            Assert.Null(await svc.GetSessionByIdAsync(9999));
        }
    }
}
