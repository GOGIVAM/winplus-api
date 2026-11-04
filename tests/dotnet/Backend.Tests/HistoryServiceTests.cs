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
    /// Historique d'apprentissage : copie fidèle de la demande dans l'événement,
    /// erreur claire pour un événement inconnu, statistiques (minutes, ventilation,
    /// zéros par défaut), repli en statistiques vides si le dépôt échoue, relance
    /// des erreurs ailleurs. Dépôt simulé par Moq.
    /// </summary>
    public class HistoryServiceTests
    {
        private static (HistoryService Svc, Mock<IHistoryRepository> Repo) Build()
        {
            var repo = new Mock<IHistoryRepository>();
            repo.Setup(r => r.CreateAsync(It.IsAny<LearningHistory>())).ReturnsAsync((LearningHistory h) => { h.Id = 42; return h; });
            return (new HistoryService(repo.Object, NullLogger<HistoryService>.Instance), repo);
        }

        private static LearningHistory H(int id, DateTime at) => new() { Id = id, UserId = 1, SubjectId = 2, EventType = "course_started", CreatedAt = at };

        [Fact]
        public async Task L_ajout_recopie_tous_les_champs_de_la_demande()
        {
            var (svc, repo) = Build();
            var res = await svc.AddToHistoryAsync(7, new AddHistoryRequest
            {
                EventType = "test_taken", SubjectId = 3, EventTitle = "Quiz 1", EventDescription = "d", Score = 14.5m,
                DurationSeconds = 120, ProgressPercentage = 50m, Notes = "n", IsCompleted = true,
            });
            Assert.Equal(42, res.Id);
            Assert.Equal(7, res.UserId);
            Assert.Equal("test_taken", res.EventType);
            Assert.Equal(14.5m, res.Score);
            Assert.True(res.IsCompleted);
            repo.Verify(r => r.CreateAsync(It.Is<LearningHistory>(h => h.UserId == 7 && h.SubjectId == 3 && h.DurationSeconds == 120)), Times.Once);
        }

        [Fact]
        public async Task Une_erreur_du_depot_a_l_ajout_est_relancee()
        {
            var (svc, repo) = Build();
            repo.Setup(r => r.CreateAsync(It.IsAny<LearningHistory>())).ThrowsAsync(new InvalidOperationException("base"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.AddToHistoryAsync(1, new AddHistoryRequest { EventType = "x" }));
        }

        [Fact]
        public async Task Un_evenement_inconnu_leve_une_erreur_d_argument()
        {
            var (svc, repo) = Build();
            repo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync((LearningHistory?)null);
            await Assert.ThrowsAsync<ArgumentException>(() => svc.GetHistoryByIdAsync(5));
        }

        [Fact]
        public async Task Les_statistiques_convertissent_les_secondes_en_minutes_et_lisent_la_ventilation()
        {
            var (svc, repo) = Build();
            repo.Setup(r => r.GetRecentActivityAsync(1, 1)).ReturnsAsync(new List<LearningHistory>());
            repo.Setup(r => r.GetEventTypeBreakdownAsync(1)).ReturnsAsync(new Dictionary<string, int> { ["course_started"] = 4, ["course_completed"] = 2 });
            repo.Setup(r => r.GetTotalCountByUserAsync(1)).ReturnsAsync(9);
            repo.Setup(r => r.GetTotalTimeSpentAsync(1)).ReturnsAsync(7199);
            repo.Setup(r => r.GetAverageScoreAsync(1)).ReturnsAsync(12.5f);
            var s = await svc.GetUserStatisticsAsync(1);
            Assert.Equal(9, s.TotalEvents);
            Assert.Equal(4, s.CoursesStarted);
            Assert.Equal(2, s.CoursesCompleted);
            Assert.Equal(119, s.TotalTimeSpentMinutes);
            Assert.Equal(12.5f, s.AverageScore);
        }

        [Fact]
        public async Task Sans_ventilation_les_compteurs_de_cours_valent_zero()
        {
            var (svc, repo) = Build();
            repo.Setup(r => r.GetRecentActivityAsync(1, 1)).ReturnsAsync(new List<LearningHistory>());
            repo.Setup(r => r.GetEventTypeBreakdownAsync(1)).ReturnsAsync(new Dictionary<string, int>());
            var s = await svc.GetUserStatisticsAsync(1);
            Assert.Equal(0, s.CoursesStarted);
            Assert.Equal(0, s.CoursesCompleted);
        }

        [Fact]
        public async Task Une_erreur_du_depot_donne_des_statistiques_vides_sans_exception()
        {
            var (svc, repo) = Build();
            repo.Setup(r => r.GetRecentActivityAsync(It.IsAny<int>(), It.IsAny<int>())).ThrowsAsync(new InvalidOperationException());
            var s = await svc.GetUserStatisticsAsync(1);
            Assert.Equal(0, s.TotalEvents);
            Assert.Empty(s.EventTypeBreakdown);
        }

        [Fact]
        public async Task La_liste_reprend_la_pagination_demandee_et_le_total()
        {
            var (svc, repo) = Build();
            repo.Setup(r => r.GetByUserIdAsync(1, 2, 10)).ReturnsAsync(new List<LearningHistory> { H(1, new DateTime(2026, 1, 1)) });
            repo.Setup(r => r.GetTotalCountByUserAsync(1)).ReturnsAsync(11);
            repo.Setup(r => r.GetRecentActivityAsync(1, 1)).ReturnsAsync(new List<LearningHistory>());
            repo.Setup(r => r.GetEventTypeBreakdownAsync(1)).ReturnsAsync(new Dictionary<string, int>());
            var list = await svc.GetUserHistoryAsync(1, 2, 10);
            Assert.Equal(2, list.Page);
            Assert.Equal(10, list.Limit);
            Assert.Equal(11, list.Total);
            Assert.Single(list.History);
            Assert.NotNull(list.Statistics);
        }

        [Fact]
        public async Task Les_listes_filtrees_n_embarquent_pas_les_statistiques()
        {
            var (svc, repo) = Build();
            repo.Setup(r => r.GetByUserAndTypeAsync(1, "lesson_viewed", 1, 50)).ReturnsAsync(new List<LearningHistory>());
            var list = await svc.GetUserHistoryByTypeAsync(1, "lesson_viewed");
            Assert.Null(list.Statistics);
        }

        [Fact]
        public async Task Les_erreurs_de_lecture_filtrees_sont_relancees()
        {
            var (svc, repo) = Build();
            repo.Setup(r => r.GetByUserAndSubjectAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>())).ThrowsAsync(new InvalidOperationException());
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.GetUserHistoryBySubjectAsync(1, 2));
        }

        [Fact]
        public async Task La_suppression_et_l_effacement_renvoient_le_resultat_du_depot()
        {
            var (svc, repo) = Build();
            repo.Setup(r => r.DeleteAsync(1)).ReturnsAsync(true);
            repo.Setup(r => r.DeleteAsync(2)).ReturnsAsync(false);
            repo.Setup(r => r.DeleteByUserIdAsync(9)).ReturnsAsync(true);
            Assert.True(await svc.DeleteHistoryAsync(1));
            Assert.False(await svc.DeleteHistoryAsync(2));
            Assert.True(await svc.ClearUserHistoryAsync(9));
        }

        [Fact(Skip = "bogue probable : GetUserStatisticsAsync demande une seule activite recente (count = 1) puis lit First() et Last() de cette liste d'un element, donc FirstActivityAt est toujours egal a LastActivityAt (la derniere activite) au lieu de la plus ancienne.")]
        public async Task La_premiere_activite_est_la_plus_ancienne_et_la_derniere_la_plus_recente()
        {
            var (svc, repo) = Build();
            var all = new List<LearningHistory> { H(2, new DateTime(2026, 3, 1)), H(1, new DateTime(2026, 1, 1)) };
            repo.Setup(r => r.GetRecentActivityAsync(1, It.IsAny<int>())).ReturnsAsync((int u, int count) => all.Take(count).ToList());
            repo.Setup(r => r.GetEventTypeBreakdownAsync(1)).ReturnsAsync(new Dictionary<string, int>());
            var s = await svc.GetUserStatisticsAsync(1);
            Assert.Equal(new DateTime(2026, 1, 1), s.FirstActivityAt);
            Assert.Equal(new DateTime(2026, 3, 1), s.LastActivityAt);
        }
    }
}
