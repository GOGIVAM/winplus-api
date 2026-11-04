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
    /// Annuaire des établissements : seuls les actifs et non supprimés sont
    /// visibles, tri par nom, filtre de pays insensible à la casse, création qui
    /// force l'état actif, suppression logique, pannes absorbées en lecture.
    /// </summary>
    public class InstitutionServiceTests
    {
        private static Institution I(int id, string name, string country = "Cameroun", bool active = true, bool deleted = false) =>
            new() { Id = id, Name = name, Country = country, IsActive = active, IsDeleted = deleted };

        private static (InstitutionService Svc, Mock<IRepository<Institution>> Repo) Build(params Institution[] items)
        {
            var repo = new Mock<IRepository<Institution>>();
            repo.Setup(r => r.GetAllAsync()).ReturnsAsync(items);
            repo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => items.FirstOrDefault(i => i.Id == id));
            repo.Setup(r => r.CreateAsync(It.IsAny<Institution>())).ReturnsAsync((Institution i) => i);
            repo.Setup(r => r.UpdateAsync(It.IsAny<Institution>())).ReturnsAsync((Institution i) => i);
            return (new InstitutionService(repo.Object, NullLogger<InstitutionService>.Instance), repo);
        }

        [Fact]
        public void Le_constructeur_refuse_les_arguments_nuls()
        {
            Assert.Throws<ArgumentNullException>(() => new InstitutionService(null!, NullLogger<InstitutionService>.Instance));
            Assert.Throws<ArgumentNullException>(() => new InstitutionService(new Mock<IRepository<Institution>>().Object, null!));
        }

        [Fact]
        public async Task Seuls_les_etablissements_actifs_non_supprimes_sont_listes_par_nom()
        {
            var (svc, _) = Build(I(1, "Zeta"), I(2, "Alpha"), I(3, "Inactif", active: false), I(4, "Supprimé", deleted: true));
            Assert.Equal(new[] { "Alpha", "Zeta" }, (await svc.GetAllInstitutionsAsync()).Select(i => i.Name));
        }

        [Fact]
        public async Task Le_filtre_par_pays_ignore_la_casse_et_un_pays_vide_renvoie_tout()
        {
            var (svc, _) = Build(I(1, "A", "Cameroun"), I(2, "B", "Sénégal"), I(3, "C", "CAMEROUN", active: false));
            Assert.Equal(new[] { "A" }, (await svc.GetInstitutionsByCountryAsync("cameroun")).Select(i => i.Name));
            Assert.Equal(2, (await svc.GetInstitutionsByCountryAsync("  ")).Count());
            Assert.Empty(await svc.GetInstitutionsByCountryAsync("Gabon"));
        }

        [Fact]
        public async Task Un_etablissement_inactif_ou_supprime_est_introuvable_par_identifiant()
        {
            var (svc, _) = Build(I(1, "A"), I(2, "B", active: false), I(3, "C", deleted: true));
            Assert.NotNull(await svc.GetInstitutionByIdAsync(1));
            Assert.Null(await svc.GetInstitutionByIdAsync(2));
            Assert.Null(await svc.GetInstitutionByIdAsync(3));
            Assert.Null(await svc.GetInstitutionByIdAsync(4));
        }

        [Fact]
        public async Task Une_panne_du_depot_donne_une_liste_vide_ou_rien()
        {
            var repo = new Mock<IRepository<Institution>>();
            repo.Setup(r => r.GetAllAsync()).ThrowsAsync(new InvalidOperationException());
            repo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ThrowsAsync(new InvalidOperationException());
            var svc = new InstitutionService(repo.Object, NullLogger<InstitutionService>.Instance);
            Assert.Empty(await svc.GetAllInstitutionsAsync());
            Assert.Empty(await svc.GetInstitutionsByCountryAsync("Cameroun"));
            Assert.Null(await svc.GetInstitutionByIdAsync(1));
        }

        [Fact]
        public async Task La_creation_force_actif_et_non_supprime()
        {
            var (svc, _) = Build();
            var created = await svc.CreateInstitutionAsync(new Institution { Name = "N", IsActive = false, IsDeleted = true });
            Assert.True(created.IsActive);
            Assert.False(created.IsDeleted);
        }

        [Fact]
        public async Task La_creation_et_la_mise_a_jour_refusent_un_etablissement_nul()
        {
            var (svc, _) = Build();
            await Assert.ThrowsAsync<ArgumentNullException>(() => svc.CreateInstitutionAsync(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() => svc.UpdateInstitutionAsync(null!));
        }

        [Fact]
        public async Task La_mise_a_jour_horodate_la_modification()
        {
            var (svc, _) = Build();
            var updated = await svc.UpdateInstitutionAsync(I(1, "A"));
            Assert.NotNull(updated.UpdatedAt);
        }

        [Fact]
        public async Task La_suppression_est_logique_et_renvoie_faux_si_inconnue_ou_en_panne()
        {
            var inst = I(1, "A");
            var (svc, _) = Build(inst);
            Assert.True(await svc.DeleteInstitutionAsync(1));
            Assert.True(inst.IsDeleted);
            Assert.False(await svc.DeleteInstitutionAsync(9));

            var repo = new Mock<IRepository<Institution>>();
            repo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ThrowsAsync(new InvalidOperationException());
            Assert.False(await new InstitutionService(repo.Object, NullLogger<InstitutionService>.Instance).DeleteInstitutionAsync(1));
        }
    }
}
