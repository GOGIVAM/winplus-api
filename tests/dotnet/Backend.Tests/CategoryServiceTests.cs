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
    /// Catégories du catalogue calculées depuis la base : types d'examens
    /// regroupés par école (« IUT/... » et « IUT - ... » dans « IUT »), niveaux
    /// actifs triés avec repli sur les valeurs par défaut quand la table est
    /// vide, années décroissantes, détail d'un niveau, filtres fixes.
    /// </summary>
    public class CategoryServiceTests
    {
        private static (TestApplicationDbContext Db, CategoryService Svc) Build()
        {
            var db = TestApplicationDbContext.Create();
            return (db, new CategoryService(db, NullLogger<CategoryService>.Instance));
        }

        private static Exam E(int id, string type, int year = 2024, string? level = null, bool published = true, bool deleted = false) =>
            new() { Id = id, Title = $"E{id}", ExamType = type, Year = year, Level = level, IsPublished = published, IsDeleted = deleted };

        [Fact]
        public void Le_constructeur_refuse_les_arguments_nuls()
        {
            Assert.Throws<ArgumentNullException>(() => new CategoryService(null!, NullLogger<CategoryService>.Instance));
            Assert.Throws<ArgumentNullException>(() => new CategoryService(TestApplicationDbContext.Create(), null!));
        }

        [Fact]
        public async Task Les_types_d_examens_d_une_meme_ecole_sont_regroupes_sous_son_nom()
        {
            var (db, svc) = Build();
            db.Exams.AddRange(E(1, "IUT/Concours d'entrée"), E(2, "IUT - Épreuve de concours"), E(3, "BAC"), E(4, "IUT", published: false));
            db.SaveChanges();
            var list = await svc.GetExamCategoriesAsync();
            Assert.Equal(new[] { "BAC", "IUT" }, list.Select(c => c.DisplayName));
            Assert.Equal(new[] { "bac", "iut" }, list.Select(c => c.Name));
            Assert.Equal(new[] { 1, 2 }, list.Select(c => c.Id));
            Assert.Equal(3, list.Single(c => c.Name == "iut").Count);
        }

        [Fact]
        public async Task Les_examens_supprimes_ou_non_publies_ne_creent_pas_de_categorie()
        {
            var (db, svc) = Build();
            db.Exams.AddRange(E(1, "BEPC", published: false), E(2, "PROBATOIRE", deleted: true));
            db.SaveChanges();
            Assert.Empty(await svc.GetExamCategoriesAsync());
        }

        [Fact]
        public async Task Les_matieres_publiees_donnent_une_categorie_avec_compteur_et_nom_normalise()
        {
            var (db, svc) = Build();
            db.Subjects.AddRange(
                new Subject { Id = 1, Title = "a", Category = "Sciences Physiques", IsPublished = true },
                new Subject { Id = 2, Title = "b", Category = "Sciences Physiques", IsPublished = true },
                new Subject { Id = 3, Title = "c", Category = "Sciences Physiques", IsPublished = false },
                new Subject { Id = 4, Title = "d", Category = "Anglais", IsPublished = true, IsDeleted = true });
            db.SaveChanges();
            var item = Assert.Single(await svc.GetSubjectsAsync());
            Assert.Equal("sciences-physiques", item.Name);
            Assert.Equal(2, item.Count);
        }

        [Fact]
        public async Task Sans_niveau_en_base_les_difficultes_par_defaut_sont_renvoyees()
        {
            var (_, svc) = Build();
            var list = await svc.GetDifficultiesAsync();
            Assert.Equal(new[] { "debutant", "intermediaire", "avance", "expert" }, list.Select(c => c.Name));
        }

        [Fact]
        public async Task Les_niveaux_actifs_sont_tries_par_ordre_et_comptent_leurs_examens()
        {
            var (db, svc) = Build();
            db.Levels.AddRange(
                new Level { Id = 1, Name = "Terminale C", DisplayName = "Terminale C", Order = 2 },
                new Level { Id = 2, Name = "Seconde", DisplayName = "Seconde", Order = 1 },
                new Level { Id = 3, Name = "Archive", DisplayName = "Archive", Order = 0, IsActive = false });
            db.Exams.AddRange(E(1, "BAC", level: "Seconde"), E(2, "BAC", level: "Seconde", deleted: true), E(3, "BAC", level: "Terminale C"));
            db.SaveChanges();
            var list = await svc.GetDifficultiesAsync();
            Assert.Equal(new[] { "seconde", "terminale-c" }, list.Select(c => c.Name));
            Assert.Equal(new int?[] { 1, 1 }, list.Select(c => (int?)c.Count));
        }

        [Fact]
        public async Task Les_annees_sont_decroissantes_distinctes_et_comptent_les_examens_non_supprimes()
        {
            var (db, svc) = Build();
            db.Exams.AddRange(E(1, "BAC", 2022), E(2, "BAC", 2024), E(3, "BEPC", 2024), E(4, "BAC", 2023, published: false));
            db.SaveChanges();
            var list = await svc.GetYearsAsync();
            Assert.Equal(new[] { "2024", "2022" }, list.Select(c => c.Name));
            Assert.Equal(2, list[0].Count);
            Assert.Equal("Année 2024", list[0].DisplayName);
        }

        [Fact]
        public async Task Le_detail_d_un_niveau_inactif_ou_inconnu_est_absent()
        {
            var (db, svc) = Build();
            db.Levels.Add(new Level { Id = 1, Name = "Seconde", DisplayName = "Seconde" });
            db.Levels.Add(new Level { Id = 2, Name = "Vieux", DisplayName = "Vieux", IsActive = false });
            db.Exams.Add(E(1, "BAC", level: "Seconde"));
            db.SaveChanges();
            var detail = await svc.GetCategoryByIdAsync(1);
            Assert.NotNull(detail);
            Assert.Equal(1, detail!.CourseCount);
            Assert.Equal("Tous les cours et épreuves pour Seconde", detail.Description);
            Assert.Null(await svc.GetCategoryByIdAsync(2));
            Assert.Null(await svc.GetCategoryByIdAsync(99));
        }

        [Fact]
        public async Task Les_filtres_exposent_les_tranches_de_prix_et_de_notes_fixes()
        {
            var (_, svc) = Build();
            var f = await svc.GetFiltersAsync();
            Assert.Equal("Gratuit", f.PriceRanges.First().Label);
            Assert.Equal(5, f.PriceRanges.Count);
            Assert.Equal(new[] { 4, 3, 2 }, f.Ratings.Select(r => r.MinStars));
            var all = await svc.GetAllCategoriesAsync();
            Assert.Equal(4, all.Difficulties.Count);
        }
    }
}
