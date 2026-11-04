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
    /// Lecture et gestion des plans tarifaires : les plans supprimés (suppression
    /// logique) sont invisibles, le tri se fait par prix croissant, la catégorie
    /// se compare sans tenir compte de la casse, et toute erreur du dépôt est
    /// absorbée en lecture mais relancée en écriture. Dépôt simulé par Moq.
    /// </summary>
    public class PricingServiceTests
    {
        private static PricingPlan Plan(int id, string category, decimal price, bool deleted = false) =>
            new() { Id = id, Name = $"Plan {id}", Category = category, Price = price, IsDeleted = deleted };

        private static (PricingService Service, Mock<IRepository<PricingPlan>> Repo) Build(params PricingPlan[] plans)
        {
            var repo = new Mock<IRepository<PricingPlan>>();
            repo.Setup(r => r.GetAllAsync()).ReturnsAsync(plans);
            repo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => plans.FirstOrDefault(p => p.Id == id));
            repo.Setup(r => r.CreateAsync(It.IsAny<PricingPlan>())).ReturnsAsync((PricingPlan p) => p);
            repo.Setup(r => r.UpdateAsync(It.IsAny<PricingPlan>())).ReturnsAsync((PricingPlan p) => p);
            return (new PricingService(repo.Object, NullLogger<PricingService>.Instance), repo);
        }

        [Fact]
        public void Le_constructeur_refuse_un_depot_ou_un_journal_nul()
        {
            Assert.Throws<ArgumentNullException>(() => new PricingService(null!, NullLogger<PricingService>.Instance));
            Assert.Throws<ArgumentNullException>(() => new PricingService(new Mock<IRepository<PricingPlan>>().Object, null!));
        }

        [Fact]
        public async Task Tous_les_plans_actifs_sont_tries_par_prix_croissant_et_les_supprimes_sont_exclus()
        {
            var (svc, _) = Build(Plan(1, "students", 5000), Plan(2, "students", 1000), Plan(3, "teachers", 3000, deleted: true), Plan(4, "parents", 2000));
            var result = (await svc.GetAllPlansAsync()).ToList();
            Assert.Equal(new[] { 2, 4, 1 }, result.Select(p => p.Id));
        }

        [Fact]
        public async Task Une_erreur_du_depot_donne_une_liste_vide_en_lecture()
        {
            var repo = new Mock<IRepository<PricingPlan>>();
            repo.Setup(r => r.GetAllAsync()).ThrowsAsync(new InvalidOperationException("base indisponible"));
            var svc = new PricingService(repo.Object, NullLogger<PricingService>.Instance);
            Assert.Empty(await svc.GetAllPlansAsync());
            Assert.Empty(await svc.GetPlansByCategoryAsync("students"));
        }

        [Fact]
        public async Task Les_plans_d_une_categorie_ignorent_la_casse_et_les_plans_supprimes()
        {
            var (svc, _) = Build(Plan(1, "Students", 5000), Plan(2, "students", 1000), Plan(3, "students", 100, deleted: true), Plan(4, "parents", 2000));
            var result = (await svc.GetPlansByCategoryAsync("STUDENTS")).ToList();
            Assert.Equal(new[] { 2, 1 }, result.Select(p => p.Id));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public async Task Une_categorie_vide_renvoie_tous_les_plans_actifs(string? category)
        {
            var (svc, _) = Build(Plan(1, "students", 5000), Plan(2, "parents", 1000), Plan(3, "students", 1, deleted: true));
            var result = (await svc.GetPlansByCategoryAsync(category!)).ToList();
            Assert.Equal(new[] { 2, 1 }, result.Select(p => p.Id));
        }

        [Fact]
        public async Task Une_categorie_inconnue_ne_renvoie_aucun_plan()
        {
            var (svc, _) = Build(Plan(1, "students", 5000));
            Assert.Empty(await svc.GetPlansByCategoryAsync("institutions"));
        }

        [Fact]
        public async Task Un_plan_est_retrouve_par_son_identifiant_sauf_s_il_est_supprime_ou_inconnu()
        {
            var (svc, _) = Build(Plan(1, "students", 5000), Plan(2, "students", 1000, deleted: true));
            Assert.Equal(1, (await svc.GetPlanByIdAsync(1))!.Id);
            Assert.Null(await svc.GetPlanByIdAsync(2));
            Assert.Null(await svc.GetPlanByIdAsync(99));
        }

        [Fact]
        public async Task Une_erreur_du_depot_donne_null_pour_la_lecture_par_identifiant()
        {
            var repo = new Mock<IRepository<PricingPlan>>();
            repo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ThrowsAsync(new Exception("panne"));
            var svc = new PricingService(repo.Object, NullLogger<PricingService>.Instance);
            Assert.Null(await svc.GetPlanByIdAsync(1));
        }

        [Fact]
        public async Task La_creation_horodate_le_plan_et_force_son_etat_non_supprime()
        {
            var (svc, repo) = Build();
            var plan = Plan(0, "students", 2500, deleted: true);

            var created = await svc.CreatePlanAsync(plan);

            Assert.False(created.IsDeleted);
            Assert.True(created.CreatedAt > DateTime.UtcNow.AddMinutes(-1));
            repo.Verify(r => r.CreateAsync(plan), Times.Once);
        }

        [Fact]
        public async Task La_creation_d_un_plan_nul_est_refusee()
        {
            var (svc, _) = Build();
            await Assert.ThrowsAsync<ArgumentNullException>(() => svc.CreatePlanAsync(null!));
        }

        [Fact]
        public async Task Une_erreur_du_depot_a_la_creation_est_relancee()
        {
            var repo = new Mock<IRepository<PricingPlan>>();
            repo.Setup(r => r.CreateAsync(It.IsAny<PricingPlan>())).ThrowsAsync(new InvalidOperationException("doublon"));
            var svc = new PricingService(repo.Object, NullLogger<PricingService>.Instance);
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreatePlanAsync(Plan(1, "students", 1)));
        }

        [Fact]
        public async Task La_mise_a_jour_horodate_le_plan()
        {
            var (svc, repo) = Build();
            var plan = Plan(1, "students", 2500);
            var updated = await svc.UpdatePlanAsync(plan);
            Assert.NotNull(updated.UpdatedAt);
            repo.Verify(r => r.UpdateAsync(plan), Times.Once);
        }

        [Fact]
        public async Task La_mise_a_jour_d_un_plan_nul_est_refusee()
        {
            var (svc, _) = Build();
            await Assert.ThrowsAsync<ArgumentNullException>(() => svc.UpdatePlanAsync(null!));
        }

        [Fact]
        public async Task La_suppression_est_logique_et_renvoie_vrai()
        {
            var plan = Plan(1, "students", 5000);
            var (svc, repo) = Build(plan);

            Assert.True(await svc.DeletePlanAsync(1));

            Assert.True(plan.IsDeleted);
            Assert.NotNull(plan.UpdatedAt);
            repo.Verify(r => r.UpdateAsync(plan), Times.Once);
            Assert.Null(await svc.GetPlanByIdAsync(1));
        }

        [Fact]
        public async Task La_suppression_d_un_plan_inconnu_renvoie_faux_sans_ecriture()
        {
            var (svc, repo) = Build(Plan(1, "students", 5000));
            Assert.False(await svc.DeletePlanAsync(99));
            repo.Verify(r => r.UpdateAsync(It.IsAny<PricingPlan>()), Times.Never);
        }

        [Fact]
        public async Task Une_erreur_du_depot_a_la_suppression_renvoie_faux()
        {
            var repo = new Mock<IRepository<PricingPlan>>();
            repo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ThrowsAsync(new Exception("panne"));
            var svc = new PricingService(repo.Object, NullLogger<PricingService>.Instance);
            Assert.False(await svc.DeletePlanAsync(1));
        }
    }
}
