using System;
using System.Collections.Generic;
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
    /// Annonces : seules les annonces publiées, non supprimées, déjà parues et non
    /// expirées sont visibles ; suppression logique ; publication ; une panne du
    /// dépôt est absorbée en lecture et relancée à la création. Dépôt simulé.
    /// </summary>
    public class AnnouncementServiceTests
    {
        private static readonly DateTime Past = DateTime.UtcNow.AddYears(-1);
        private static readonly DateTime Future = DateTime.UtcNow.AddYears(1);

        private static Announcement A(int id, bool published = true, DateTime? publishedAt = null, DateTime? expires = null, bool deleted = false) =>
            new() { Id = id, Title = $"A{id}", IsPublished = published, PublishedAt = publishedAt ?? Past.AddDays(id), ExpiresAt = expires, IsDeleted = deleted, CreatedAt = Past.AddDays(id) };

        private static (AnnouncementService Svc, Mock<IRepository<Announcement>> Repo) Build(params Announcement[] items)
        {
            var repo = new Mock<IRepository<Announcement>>();
            repo.Setup(r => r.GetAllAsync()).ReturnsAsync(items);
            repo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => items.FirstOrDefault(a => a.Id == id));
            repo.Setup(r => r.CreateAsync(It.IsAny<Announcement>())).ReturnsAsync((Announcement a) => a);
            repo.Setup(r => r.UpdateAsync(It.IsAny<Announcement>())).ReturnsAsync((Announcement a) => a);
            return (new AnnouncementService(repo.Object, NullLogger<AnnouncementService>.Instance), repo);
        }

        [Fact]
        public void Le_constructeur_refuse_les_arguments_nuls()
        {
            Assert.Throws<ArgumentNullException>(() => new AnnouncementService(null!, NullLogger<AnnouncementService>.Instance));
            Assert.Throws<ArgumentNullException>(() => new AnnouncementService(new Mock<IRepository<Announcement>>().Object, null!));
        }

        [Fact]
        public async Task Les_annonces_publiees_filtrent_non_publiees_supprimees_futures_et_expirees()
        {
            var (svc, _) = Build(
                A(1), A(2, published: false), A(3, deleted: true), A(4, publishedAt: Future),
                A(5, expires: Past.AddDays(10)), A(6, expires: Future));
            var ids = (await svc.GetPublishedAnnouncementsAsync()).Select(a => a.Id).ToArray();
            Assert.Equal(new[] { 6, 1 }, ids);
        }

        [Fact]
        public async Task La_limite_et_le_tri_par_date_de_publication_decroissante_s_appliquent()
        {
            var (svc, _) = Build(A(1), A(2), A(3));
            Assert.Equal(new[] { 3, 2 }, (await svc.GetPublishedAnnouncementsAsync(2)).Select(a => a.Id).ToArray());
        }

        [Fact]
        public async Task Une_panne_du_depot_donne_une_liste_vide_ou_rien_en_lecture()
        {
            var repo = new Mock<IRepository<Announcement>>();
            repo.Setup(r => r.GetAllAsync()).ThrowsAsync(new InvalidOperationException());
            repo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ThrowsAsync(new InvalidOperationException());
            var svc = new AnnouncementService(repo.Object, NullLogger<AnnouncementService>.Instance);
            Assert.Empty(await svc.GetPublishedAnnouncementsAsync());
            Assert.Empty(await svc.GetAllAnnouncementsAsync());
            Assert.Null(await svc.GetAnnouncementByIdAsync(1));
        }

        [Fact]
        public async Task Toutes_les_annonces_excluent_les_supprimees_et_sont_triees_par_creation_decroissante()
        {
            var (svc, _) = Build(A(1), A(2, deleted: true), A(3, published: false));
            Assert.Equal(new[] { 3, 1 }, (await svc.GetAllAnnouncementsAsync()).Select(a => a.Id).ToArray());
        }

        [Fact]
        public async Task Une_annonce_supprimee_est_introuvable_par_identifiant()
        {
            var (svc, _) = Build(A(1), A(2, deleted: true));
            Assert.NotNull(await svc.GetAnnouncementByIdAsync(1));
            Assert.Null(await svc.GetAnnouncementByIdAsync(2));
            Assert.Null(await svc.GetAnnouncementByIdAsync(3));
        }

        [Fact]
        public async Task La_creation_force_la_date_et_l_etat_non_supprime()
        {
            var (svc, repo) = Build();
            var created = await svc.CreateAnnouncementAsync(new Announcement { Title = "Neuf", IsDeleted = true });
            Assert.False(created.IsDeleted);
            Assert.True((DateTime.UtcNow - created.CreatedAt).TotalMinutes < 5);
            repo.Verify(r => r.CreateAsync(created), Times.Once);
        }

        [Fact]
        public async Task La_creation_et_la_mise_a_jour_refusent_une_annonce_nulle()
        {
            var (svc, _) = Build();
            await Assert.ThrowsAsync<ArgumentNullException>(() => svc.CreateAnnouncementAsync(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() => svc.UpdateAnnouncementAsync(null!));
        }

        [Fact]
        public async Task La_suppression_est_logique_et_renvoie_faux_si_inconnue()
        {
            var a = A(1);
            var (svc, repo) = Build(a);
            Assert.True(await svc.DeleteAnnouncementAsync(1));
            Assert.True(a.IsDeleted);
            Assert.NotNull(a.UpdatedAt);
            Assert.False(await svc.DeleteAnnouncementAsync(99));
            repo.Verify(r => r.UpdateAsync(It.IsAny<Announcement>()), Times.Once);
        }

        [Fact]
        public async Task La_publication_marque_l_annonce_publiee_et_renvoie_faux_si_inconnue()
        {
            var a = A(1, published: false);
            var (svc, _) = Build(a);
            Assert.True(await svc.PublishAnnouncementAsync(1));
            Assert.True(a.IsPublished);
            Assert.False(await svc.PublishAnnouncementAsync(2));
        }

        [Fact]
        public async Task Une_panne_a_la_suppression_ou_publication_renvoie_faux()
        {
            var repo = new Mock<IRepository<Announcement>>();
            repo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ThrowsAsync(new InvalidOperationException());
            var svc = new AnnouncementService(repo.Object, NullLogger<AnnouncementService>.Instance);
            Assert.False(await svc.DeleteAnnouncementAsync(1));
            Assert.False(await svc.PublishAnnouncementAsync(1));
        }

        [Fact]
        public async Task Une_panne_a_la_creation_est_relancee()
        {
            var repo = new Mock<IRepository<Announcement>>();
            repo.Setup(r => r.CreateAsync(It.IsAny<Announcement>())).ThrowsAsync(new InvalidOperationException("base"));
            var svc = new AnnouncementService(repo.Object, NullLogger<AnnouncementService>.Instance);
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAnnouncementAsync(new Announcement()));
        }
    }
}
