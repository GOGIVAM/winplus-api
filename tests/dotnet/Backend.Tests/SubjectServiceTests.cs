using System;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Repositories;
using Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Matières du catalogue : pagination bornée, création en brouillon avec
    /// compteurs remis à zéro, publication, matières similaires (publiées,
    /// non supprimées, mieux notées d'abord), catégories filtrées par niveau
    /// (matière sans niveau visible partout), absorption des pannes en lecture.
    /// Dépôt simulé par Moq.
    /// </summary>
    public class SubjectServiceTests
    {
        private static Subject S(int id, string? cat = "Maths", string? level = null, bool published = true, bool deleted = false, decimal rating = 0m) =>
            new() { Id = id, Title = $"S{id}", Category = cat, Level = level, IsPublished = published, IsDeleted = deleted, AverageRating = rating };

        private static (SubjectService Svc, Mock<ISubjectRepository> Repo) Build(params Subject[] all)
        {
            var repo = new Mock<ISubjectRepository>();
            repo.Setup(r => r.GetAllAsync()).ReturnsAsync(all);
            repo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => all.FirstOrDefault(s => s.Id == id));
            repo.Setup(r => r.GetByCategoryAsync(It.IsAny<string>())).ReturnsAsync((string c) => all.Where(s => s.Category == c).ToList());
            repo.Setup(r => r.UpdateAsync(It.IsAny<Subject>())).ReturnsAsync((Subject s) => s);
            repo.Setup(r => r.CreateAsync(It.IsAny<Subject>())).ReturnsAsync((Subject s) => s);
            return (new SubjectService(repo.Object, NullLogger<SubjectService>.Instance), repo);
        }

        [Theory]
        [InlineData(0, 10, 1, 10)]
        [InlineData(-3, 0, 1, 20)]
        [InlineData(2, 101, 2, 20)]
        [InlineData(3, 100, 3, 100)]
        public async Task La_pagination_est_corrigee_avant_l_appel_au_depot(int page, int limit, int expectedPage, int expectedLimit)
        {
            var (svc, repo) = Build();
            await svc.GetAllSubjectsAsync(page, limit);
            repo.Verify(r => r.GetAllAsync(expectedPage, expectedLimit), Times.Once);
        }

        [Fact]
        public async Task La_categorie_vide_et_la_recherche_vide_ne_sollicitent_pas_le_depot()
        {
            var (svc, repo) = Build();
            Assert.Empty(await svc.GetSubjectsByCategoryAsync(""));
            Assert.Empty(await svc.SearchSubjectsAsync("   "));
            repo.Verify(r => r.GetByCategoryAsync(It.IsAny<string>()), Times.Never);
            repo.Verify(r => r.SearchAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task La_creation_exige_un_titre_et_remet_l_etat_a_zero()
        {
            var (svc, _) = Build();
            await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateSubjectAsync(new Subject { Title = "" }));
            var created = await svc.CreateSubjectAsync(new Subject { Title = "Neuf", IsPublished = true, EnrollmentCount = 9, AverageRating = 5m, TotalRatings = 7 });
            Assert.False(created.IsPublished);
            Assert.Equal(0, created.EnrollmentCount);
            Assert.Equal(0m, created.AverageRating);
            Assert.Equal(0, created.TotalRatings);
        }

        [Fact]
        public async Task Publier_depublier_et_noter_modifient_la_matiere_ou_levent_une_erreur_si_inconnue()
        {
            var (svc, _) = Build(S(1, published: false));
            Assert.True((await svc.PublishSubjectAsync(1)).IsPublished);
            Assert.False((await svc.UnpublishSubjectAsync(1)).IsPublished);
            var rated = await svc.UpdateRatingAsync(1, 4.5m, 12);
            Assert.Equal(4.5m, rated.AverageRating);
            Assert.Equal(12, rated.TotalRatings);
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PublishSubjectAsync(99));
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.UnpublishSubjectAsync(99));
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.UpdateRatingAsync(99, 1m, 1));
        }

        [Fact]
        public async Task Les_matieres_similaires_excluent_la_courante_les_brouillons_et_supprimees_et_sont_limitees()
        {
            var (svc, _) = Build(S(1), S(2, rating: 3m), S(3, rating: 5m), S(4, published: false, rating: 9m), S(5, deleted: true, rating: 9m), S(6, cat: "Autre", rating: 9m), S(7, rating: 1m));
            var ids = (await svc.GetSimilarSubjectsAsync(1, 2)).Select(s => s.Id).ToArray();
            Assert.Equal(new[] { 3, 2 }, ids);
            Assert.Empty(await svc.GetSimilarSubjectsAsync(99));
        }

        [Fact]
        public async Task Les_categories_sont_distinctes_triees_et_filtrees_par_niveau()
        {
            var (svc, _) = Build(
                S(1, "Maths", "3eme, 2nde"), S(2, "Physique", "Tle"), S(3, "Anglais", null), S(4, "Maths", "Tle"), S(5, null, "3eme"));
            Assert.Equal(new[] { "Anglais", "Maths", "Physique" }, await svc.GetCategoriesAsync());
            Assert.Equal(new[] { "Anglais", "Maths" }, await svc.GetCategoriesAsync("2NDE"));
            Assert.Equal(new[] { "Anglais" }, await svc.GetCategoriesAsync("1ere"));
        }

        [Fact]
        public async Task Les_filtres_exposent_les_categories_et_les_listes_fixes()
        {
            var (svc, _) = Build(S(1, "Maths"), S(2, "Maths"), S(3, "Anglais"));
            var f = await svc.GetFiltersAsync();
            Assert.Equal(new[] { "Anglais", "Maths" }, f["categories"]);
            Assert.Equal(new[] { "Free", "Paid" }, f["price_range"]);
            Assert.Equal(4, f["rating"].Count());
        }

        [Fact]
        public async Task Les_pannes_de_lecture_sont_absorbees_mais_pas_celles_d_ecriture()
        {
            var repo = new Mock<ISubjectRepository>();
            repo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ThrowsAsync(new InvalidOperationException());
            repo.Setup(r => r.GetAllAsync()).ThrowsAsync(new InvalidOperationException());
            repo.Setup(r => r.CountAsync()).ThrowsAsync(new InvalidOperationException());
            repo.Setup(r => r.DeleteAsync(It.IsAny<int>())).ThrowsAsync(new InvalidOperationException());
            var svc = new SubjectService(repo.Object, NullLogger<SubjectService>.Instance);
            Assert.Null(await svc.GetSubjectByIdAsync(1));
            Assert.Empty(await svc.GetAllSubjectsAsync());
            Assert.Empty(await svc.GetCategoriesAsync());
            Assert.Empty(await svc.GetFiltersAsync());
            Assert.Equal(0, await svc.GetTotalSubjectsCountAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteSubjectAsync(1));
        }
    }
}
