using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Repositories;
using Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Analytique d'usage : enregistrement d'un événement (nom obligatoire, type
    /// par défaut égal au nom), statistiques de session du jour, analytique
    /// utilisateur sur 7 jours, tableau de bord. Dépôt simulé par Moq.
    /// </summary>
    public class AnalyticsServiceTests
    {
        private static AnalyticsEvent Ev(int id, string type, DateTime at, int? user = 1) =>
            new() { Id = id, UserId = user, EventType = type, EventName = type, CreatedAt = at };

        private static (AnalyticsService Svc, Mock<IAnalyticsRepository> Repo) Build()
        {
            var repo = new Mock<IAnalyticsRepository>();
            repo.Setup(r => r.CreateAsync(It.IsAny<AnalyticsEvent>())).ReturnsAsync((AnalyticsEvent e) => { e.Id = 7; return e; });
            return (new AnalyticsService(repo.Object, NullLogger<AnalyticsService>.Instance), repo);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Un_evenement_sans_nom_est_refuse_et_rien_n_est_enregistre(string? name)
        {
            var (svc, repo) = Build();
            await Assert.ThrowsAsync<ArgumentException>(() => svc.TrackEventAsync(1, new TrackEventRequest { EventName = name }));
            repo.Verify(r => r.CreateAsync(It.IsAny<AnalyticsEvent>()), Times.Never);
        }

        [Fact]
        public async Task Le_type_par_defaut_est_le_nom_de_l_evenement()
        {
            var (svc, repo) = Build();
            var r = await svc.TrackEventAsync(5, new TrackEventRequest { EventName = "login_success" });
            Assert.Equal("login_success", r.EventType);
            Assert.Equal(7, r.Id);
            Assert.Equal(5, r.UserId);
            var typed = await svc.TrackEventAsync(null, new TrackEventRequest { EventName = "clic", EventType = "button_click", EventCategory = "ui", IpAddress = "1.2.3.4" });
            Assert.Equal("button_click", typed.EventType);
            Assert.Null(typed.UserId);
            Assert.Equal("ui", typed.EventCategory);
        }

        [Fact]
        public async Task Les_statistiques_de_session_comptent_cinq_minutes_par_evenement_du_jour()
        {
            var (svc, repo) = Build();
            var now = DateTime.UtcNow;
            repo.Setup(r => r.GetByUserAndDateRangeAsync(1, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1)))
                .ReturnsAsync(new List<AnalyticsEvent> { Ev(1, "a", now.AddMinutes(-10)), Ev(2, "a", now.AddMinutes(-5)), Ev(3, "b", now.AddMinutes(-1)) });
            var s = await svc.GetSessionStatsAsync(1);
            Assert.Equal(3, s.TotalEvents);
            Assert.Equal(15, s.TotalDurationMinutes);
            Assert.Equal(2, s.EventTypes["a"]);
            Assert.Equal(DateTime.UtcNow.Date, s.SessionStartTime);
        }

        [Fact]
        public async Task La_session_est_active_si_un_evenement_date_de_moins_d_une_heure()
        {
            var (svc, repo) = Build();
            var today = DateTime.UtcNow.Date;
            repo.Setup(r => r.GetByUserAndDateRangeAsync(1, today, today.AddDays(1)))
                .ReturnsAsync(new List<AnalyticsEvent> { Ev(1, "a", DateTime.UtcNow.AddHours(-3)) });
            Assert.False((await svc.GetSessionStatsAsync(1)).IsActive);
            repo.Setup(r => r.GetByUserAndDateRangeAsync(1, today, today.AddDays(1)))
                .ReturnsAsync(new List<AnalyticsEvent> { Ev(1, "a", DateTime.UtcNow.AddMinutes(-5)) });
            Assert.True((await svc.GetSessionStatsAsync(1)).IsActive);
        }

        [Fact]
        public async Task L_analytique_utilisateur_resume_les_sept_derniers_jours()
        {
            var (svc, repo) = Build();
            var all = new List<AnalyticsEvent> { Ev(3, "x", new DateTime(2026, 3, 1)), Ev(2, "y", new DateTime(2026, 2, 1)), Ev(1, "y", new DateTime(2026, 1, 1)) };
            repo.Setup(r => r.GetByUserIdAsync(1, 1, int.MaxValue)).ReturnsAsync(all);
            repo.Setup(r => r.GetByUserAndDateRangeAsync(1, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(Enumerable.Range(0, 15).Select(i => Ev(i, i < 10 ? "y" : "x", DateTime.UtcNow)).ToList());
            var a = await svc.GetUserAnalyticsAsync(1);
            Assert.Equal(3, a.TotalEvents);
            Assert.Equal(15, a.TotalEventLast7Days);
            Assert.Equal(2, a.AverageEventsPerDay);
            Assert.Equal("y", a.MostCommonEventType);
            Assert.Equal(new DateTime(2026, 1, 1), a.FirstEventDate);
            Assert.Equal(new DateTime(2026, 3, 1), a.LastEventDate);
        }

        [Fact]
        public async Task Sans_evenement_l_analytique_est_vide_sans_erreur()
        {
            var (svc, repo) = Build();
            repo.Setup(r => r.GetByUserIdAsync(1, 1, int.MaxValue)).ReturnsAsync(new List<AnalyticsEvent>());
            repo.Setup(r => r.GetByUserAndDateRangeAsync(1, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(new List<AnalyticsEvent>());
            var a = await svc.GetUserAnalyticsAsync(1);
            Assert.Equal(0, a.TotalEvents);
            Assert.Null(a.FirstEventDate);
            Assert.Null(a.MostCommonEventType);
        }

        [Fact]
        public async Task Le_tableau_de_bord_garde_les_cinq_types_les_plus_frequents()
        {
            var (svc, repo) = Build();
            repo.Setup(r => r.GetTotalEventsAsync()).ReturnsAsync(100);
            repo.Setup(r => r.GetEventTypeBreakdownAsync()).ReturnsAsync(new Dictionary<string, int>
            { ["a"] = 1, ["b"] = 6, ["c"] = 5, ["d"] = 4, ["e"] = 3, ["f"] = 2, ["g"] = 7 });
            repo.Setup(r => r.GetRecentEventsAsync(10)).ReturnsAsync(new List<AnalyticsEvent> { Ev(1, "g", new DateTime(2026, 1, 1)) });
            repo.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), 1, int.MaxValue))
                .ReturnsAsync(new List<AnalyticsEvent> { Ev(1, "g", DateTime.UtcNow), Ev(2, "g", DateTime.UtcNow) });
            var d = await svc.GetDashboardAnalyticsAsync();
            Assert.Equal(100, d.TotalEvents);
            Assert.Equal(2, d.Events24h);
            Assert.Equal(new[] { "g", "b", "c", "d", "e" }, d.TopEventTypes.Keys);
            Assert.Single(d.RecentEvents);
        }

        [Fact]
        public async Task Les_erreurs_du_depot_sont_relancees()
        {
            var (svc, repo) = Build();
            repo.Setup(r => r.GetRecentEventsAsync(It.IsAny<int>())).ThrowsAsync(new InvalidOperationException());
            repo.Setup(r => r.GetEventTypeBreakdownAsync()).ThrowsAsync(new InvalidOperationException());
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.GetRecentEventsAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.GetEventTypeBreakdownAsync());
        }
    }
}
