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
    /// Données de la page d'accueil : compteurs par rôle (comptes actifs non
    /// supprimés, rôles en minuscules), taux de réussite arrondi, fonctionnalités
    /// actives triées, page « À propos » avec repli, pied de page, nombre
    /// d'épreuves publiées par catégorie.
    /// </summary>
    public class HomeServiceTests
    {
        private static (TestApplicationDbContext Db, HomeService Svc) Build()
        {
            var db = TestApplicationDbContext.Create();
            return (db, new HomeService(db, NullLogger<HomeService>.Instance));
        }

        [Fact]
        public void Le_constructeur_refuse_les_arguments_nuls()
        {
            Assert.Throws<ArgumentNullException>(() => new HomeService(null!, NullLogger<HomeService>.Instance));
            Assert.Throws<ArgumentNullException>(() => new HomeService(TestApplicationDbContext.Create(), null!));
        }

        [Fact]
        public async Task Sans_donnee_toutes_les_statistiques_valent_zero()
        {
            var (_, svc) = Build();
            var s = await svc.GetHomeStatsAsync();
            Assert.Equal(0, s.TotalUsers);
            Assert.Equal(0, s.TotalEnrollments);
            Assert.Equal(0d, s.SuccessRate);
        }

        [Fact]
        public async Task Les_compteurs_par_role_ignorent_les_comptes_inactifs_et_supprimes()
        {
            var (db, svc) = Build();
            db.Users.AddRange(
                new User { Id = 1, Email = "a@t.l", Role = "student" },
                new User { Id = 2, Email = "b@t.l", Role = "student", IsActive = false },
                new User { Id = 3, Email = "c@t.l", Role = "student", IsDeleted = true },
                new User { Id = 4, Email = "d@t.l", Role = "teacher" },
                new User { Id = 5, Email = "e@t.l", Role = "parent" },
                new User { Id = 6, Email = "f@t.l", Role = "Student" });
            db.SaveChanges();
            var s = await svc.GetHomeStatsAsync();
            Assert.Equal(4, s.TotalUsers);
            Assert.Equal(1, s.StudentCount);
            Assert.Equal(1, s.TeacherCount);
            Assert.Equal(1, s.ParentCount);
        }

        [Fact]
        public async Task Le_taux_de_reussite_est_arrondi_a_deux_decimales()
        {
            var (db, svc) = Build();
            db.Enrollments.AddRange(
                new Enrollment { Id = 1, UserId = 1, SubjectId = 1, IsCompleted = true, User = null!, Subject = null! },
                new Enrollment { Id = 2, UserId = 2, SubjectId = 1, User = null!, Subject = null! },
                new Enrollment { Id = 3, UserId = 3, SubjectId = 1, User = null!, Subject = null! });
            db.SaveChanges();
            var s = await svc.GetHomeStatsAsync();
            Assert.Equal(33.33, s.SuccessRate);
            Assert.Equal(1, s.CompletedEnrollments);
            Assert.Equal(3, s.TotalEnrollments);
        }

        [Fact]
        public async Task Les_fonctionnalites_sont_actives_et_triees_par_ordre()
        {
            var (db, svc) = Build();
            db.HomePageFeatures.AddRange(
                new HomePageFeature { Id = 1, Title = "B", Order = 2 },
                new HomePageFeature { Id = 2, Title = "A", Order = 1 },
                new HomePageFeature { Id = 3, Title = "Off", Order = 0, IsActive = false });
            db.SaveChanges();
            Assert.Equal(new[] { "A", "B" }, (await svc.GetHomeFeaturesAsync()).Select(f => f.Title));
        }

        [Fact]
        public async Task La_page_a_propos_absente_ou_non_publiee_donne_le_texte_de_repli()
        {
            var (db, svc) = Build();
            var fallback = await svc.GetAboutContentAsync();
            Assert.Equal("À propos de WinPlus", fallback.Title);
            Assert.False(fallback.IsPublished);
            db.Pages.Add(new Page { Id = 1, Slug = "about", Title = "Brouillon", IsPublished = false });
            db.SaveChanges();
            Assert.False((await svc.GetAboutContentAsync()).IsPublished);
        }

        [Fact]
        public async Task La_page_a_propos_publiee_est_renvoyee()
        {
            var (db, svc) = Build();
            db.Pages.Add(new Page { Id = 1, Slug = "about", Title = "Qui sommes-nous", Content = "Texte", IsPublished = true });
            db.SaveChanges();
            var about = await svc.GetAboutContentAsync();
            Assert.Equal("Qui sommes-nous", about.Title);
            Assert.Equal("Texte", about.Content);
            Assert.True(about.IsPublished);
        }

        [Fact]
        public async Task Le_pied_de_page_contient_l_annee_courante_et_les_liens_principaux()
        {
            var (_, svc) = Build();
            var footer = await svc.GetFooterAsync();
            Assert.Contains(DateTime.UtcNow.Year.ToString(), footer.Copyright);
            Assert.Equal("/pricing", footer.Links["Tarifs"]);
            Assert.Equal("WinPlus", footer.CompanyName);
        }

        [Fact]
        public async Task Les_epreuves_comptees_sont_les_matieres_publiees_non_supprimees_par_categorie()
        {
            var (db, svc) = Build();
            db.Subjects.AddRange(
                new Subject { Id = 1, Title = "a", Category = "BAC", IsPublished = true },
                new Subject { Id = 2, Title = "b", Category = "BAC", IsPublished = true },
                new Subject { Id = 3, Title = "c", Category = "BAC", IsPublished = false },
                new Subject { Id = 4, Title = "d", Category = "BEPC", IsPublished = true, IsDeleted = true },
                new Subject { Id = 5, Title = "e", Category = null, IsPublished = true });
            db.SaveChanges();
            var counts = (await svc.GetExamCountsAsync()).ToList();
            var bac = Assert.Single(counts);
            Assert.Equal("BAC", bac.ExamId);
            Assert.Equal(2, bac.Count);
        }

        [Fact]
        public async Task Les_contacts_ne_listent_que_les_etablissements_actifs_tries_par_pays()
        {
            var (db, svc) = Build();
            db.Institutions.AddRange(
                new Institution { Id = 1, Name = "Z", Country = "Sénégal" },
                new Institution { Id = 2, Name = "A", Country = "Cameroun" },
                new Institution { Id = 3, Name = "Off", Country = "Gabon", IsActive = false },
                new Institution { Id = 4, Name = "Del", Country = "Mali", IsDeleted = true });
            db.SaveChanges();
            Assert.Equal(new[] { "A", "Z" }, (await svc.GetContactInfoAsync()).Select(c => c.Name));
        }
    }
}
