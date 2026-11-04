using System.Linq;
using Backend.Extensions;
using Backend.Models.Entities;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Règles pures des modèles : périodes de facturation (durée en mois, libellé),
    /// publics des plans (par rôle, catégorie historique), publics d'une annonce
    /// d'établissement, motifs de signalement d'un tuteur, statuts d'entretien
    /// ouverts, et filtres de suppression logique.
    /// </summary>
    public class CatalogConstantsAndFiltersTests
    {
        [Theory]
        [InlineData("monthly", 1, "mois", true)]
        [InlineData("quarterly", 3, "trimestre", true)]
        [InlineData("yearly", 12, "an", true)]
        [InlineData("weekly", 1, "mois", false)]
        [InlineData(null, 1, "mois", false)]
        public void Les_periodes_de_facturation_ont_une_duree_et_un_libelle(string? period, int months, string label, bool valid)
        {
            Assert.Equal(months, BillingPeriods.Months(period));
            Assert.Equal(label, BillingPeriods.Label(period));
            Assert.Equal(valid, BillingPeriods.IsValid(period));
        }

        [Fact]
        public void Toutes_les_periodes_listees_sont_valides()
        {
            Assert.All(BillingPeriods.All, p => Assert.True(BillingPeriods.IsValid(p)));
            Assert.Equal(3, BillingPeriods.All.Length);
        }

        [Theory]
        [InlineData("student", "student")]
        [InlineData("  PARENT ", "parent")]
        [InlineData("Teacher", "teacher")]
        [InlineData("institution", null)]
        [InlineData("admin", null)]
        [InlineData(null, null)]
        public void Le_public_du_plan_se_deduit_du_role_et_l_institution_n_en_a_pas(string? role, string? expected)
        {
            Assert.Equal(expected, PlanAudiences.ForRole(role));
        }

        [Fact]
        public void Les_publics_de_plan_ont_une_categorie_historique_au_pluriel_et_un_libelle()
        {
            Assert.Equal("students", PlanAudiences.LegacyCategory(PlanAudiences.Student));
            Assert.Equal("teachers", PlanAudiences.LegacyCategory(PlanAudiences.Teacher));
            Assert.Equal("Parent", PlanAudiences.Label("parent"));
            Assert.Equal("Professeur", PlanAudiences.Label("teacher"));
            Assert.Equal("Élève", PlanAudiences.Label("inconnu"));
            Assert.True(PlanAudiences.IsValid("parent"));
            Assert.False(PlanAudiences.IsValid("institution"));
            Assert.False(PlanAudiences.IsValid(null));
        }

        [Theory]
        [InlineData("students", true, true, false)]
        [InlineData("parents", true, false, true)]
        [InlineData("all", true, true, true)]
        [InlineData("teachers", false, false, false)]
        [InlineData(null, false, false, false)]
        public void Le_public_d_une_annonce_d_etablissement_determine_ses_destinataires(string? audience, bool valid, bool students, bool parents)
        {
            Assert.Equal(valid, InstitutionAnnouncementAudiences.IsValid(audience));
            Assert.Equal(students, InstitutionAnnouncementAudiences.ForStudents(audience ?? ""));
            Assert.Equal(parents, InstitutionAnnouncementAudiences.ForParents(audience ?? ""));
        }

        [Fact]
        public void Chaque_motif_de_signalement_a_un_libelle_non_vide_et_les_entretiens_ouverts_sont_deux()
        {
            Assert.Equal(8, TutorReportReasons.Labels.Count);
            Assert.All(TutorReportReasons.Labels.Values, l => Assert.False(string.IsNullOrWhiteSpace(l)));
            Assert.True(TutorReportReasons.Labels.ContainsKey(TutorReportReasons.OffPlatformPayment));
            Assert.Equal(new[] { TutorInterviewStatuses.Requested, TutorInterviewStatuses.Scheduled }, TutorInterviewStatuses.Open);
        }

        [Fact]
        public void Les_filtres_de_suppression_logique_separent_actifs_et_supprimes()
        {
            var users = new[]
            {
                new User { Id = 1, Email = "a@t.l", Role = "student" },
                new User { Id = 2, Email = "b@t.l", Role = "student", IsDeleted = true },
            }.AsQueryable();
            Assert.Equal(new[] { 1 }, users.WhereNotDeleted().Select(u => u.Id));
            Assert.Equal(new[] { 2 }, users.WhereDeleted().Select(u => u.Id));

            var subjects = new[] { new Subject { Id = 1, Title = "a" }, new Subject { Id = 2, Title = "b", IsDeleted = true } }.AsQueryable();
            Assert.Equal(new[] { 1 }, subjects.WhereNotDeleted().Select(s => s.Id));
            Assert.Equal(new[] { 2 }, subjects.WhereDeleted().Select(s => s.Id));

            var orders = new[] { new Order { Id = 1, OrderNumber = "A" }, new Order { Id = 2, OrderNumber = "B", IsDeleted = true } }.AsQueryable();
            Assert.Equal(new[] { 1 }, orders.WhereNotDeleted().Select(o => o.Id));
            Assert.Equal(new[] { 2 }, orders.WhereDeleted().Select(o => o.Id));
        }
    }
}
