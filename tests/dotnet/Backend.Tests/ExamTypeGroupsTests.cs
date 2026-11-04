using Backend.Utils;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Regroupement des types d'examen saisis librement à l'upload (« IUT/Concours
    /// d'entrée - épreuve de Mathématique(QCM) », « IUT- Épreuve de concours »…)
    /// sous le nom de l'école ou de l'examen, et correspondance avec les
    /// identifiants courts des cartes de l'accueil (lots 10.4 et 10.7). Fonctions
    /// pures, aucune base requise.
    /// </summary>
    public class ExamTypeGroupsTests
    {
        // ── Head : début du libellé avant le premier séparateur ─────────

        [Theory]
        [InlineData("IUT/Concours d'entrée – épreuve de Mathématique(QCM)", "IUT")]
        [InlineData("IUT- Épreuve de concours", "IUT")]
        [InlineData("FMSB/Examen national d'aptitude", "FMSB")]
        [InlineData("Mathématique(QCM)", "Mathématique")]
        [InlineData("ENS – Concours", "ENS")]
        [InlineData("  Baccalauréat série C  ", "Baccalauréat série C")]
        [InlineData("Bac", "Bac")]
        public void Head_garde_ce_qui_precede_le_premier_separateur(string input, string expected)
        {
            Assert.Equal(expected, ExamTypeGroups.Head(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Head_d_un_texte_vide_est_vide(string? input)
        {
            Assert.Equal(string.Empty, ExamTypeGroups.Head(input));
        }

        [Fact]
        public void Head_ne_coupe_pas_un_tiret_colle_aux_mots()
        {
            // Le tiret n'est un separateur que suivi d'une espace.
            Assert.Equal("Probatoire F-Series", ExamTypeGroups.Head("Probatoire F-Series"));
        }

        [Fact]
        public void Head_d_un_libelle_commencant_par_un_separateur_renvoie_le_libelle_entier()
        {
            Assert.Equal("/Concours", ExamTypeGroups.Head("/Concours"));
        }

        // ── Key : clé de comparaison ────────────────────────────────────

        [Theory]
        [InlineData("Épreuve  Générale", "epreuve-generale")]
        [InlineData("  Baccalauréat  ", "baccalaureat")]
        [InlineData("D’Aptitude", "d'aptitude")]
        [InlineData("GCE Ordinary Level", "gce-ordinary-level")]
        [InlineData("IUT", "iut")]
        public void Key_met_en_minuscules_retire_les_accents_et_remplace_les_espaces_par_des_tirets(string input, string expected)
        {
            Assert.Equal(expected, ExamTypeGroups.Key(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void Key_d_un_texte_vide_est_vide(string? input)
        {
            Assert.Equal(string.Empty, ExamTypeGroups.Key(input));
        }

        [Fact]
        public void GroupKey_est_la_cle_du_debut_du_libelle()
        {
            Assert.Equal("iut", ExamTypeGroups.GroupKey("IUT/Concours d'entrée – QCM"));
            Assert.Equal("fmsb", ExamTypeGroups.GroupKey("FMSB- Examen national"));
            Assert.Equal(string.Empty, ExamTypeGroups.GroupKey(null));
        }

        // ── Matches : appartenance à un groupe ──────────────────────────

        [Theory]
        [InlineData("IUT/Concours d'entrée – épreuve de Mathématique(QCM)", "iut")]
        [InlineData("IUT- Épreuve de concours", "IUT")]
        [InlineData("iut", "IUT")]
        [InlineData("Bac", "BAC")]
        public void Un_libelle_appartient_au_groupe_de_son_ecole(string examType, string requested)
        {
            Assert.True(ExamTypeGroups.Matches(examType, requested));
        }

        [Theory]
        [InlineData("Baccalauréat série C", "bac")]
        [InlineData("Probatoire", "proba")]
        [InlineData("Épreuve de Français – Probatoire séries F", "probatoire")]
        [InlineData("Épreuve de Français – Probatoire séries F", "proba")]
        [InlineData("QCM – Examen national d'aptitude à la formation médicale", "fmsb")]
        [InlineData("QCM – Examen national d’aptitude à la formation médicale", "fmsb")]
        [InlineData("GCE Ordinary Level", "gce-o-level")]
        [InlineData("O Level Mathematics", "gce-o-level")]
        [InlineData("GCE Advanced Level", "gce-a-level")]
        [InlineData("A Level Physics", "gce-a-level")]
        public void Les_alias_et_fragments_rattachent_les_libelles_ou_le_nom_n_est_pas_en_tete(string examType, string requested)
        {
            Assert.True(ExamTypeGroups.Matches(examType, requested));
        }

        [Theory]
        [InlineData("IUT/Concours", "fmsb")]
        [InlineData("IUT", "bac")]
        [InlineData("Baccalauréat", "probatoire")]
        [InlineData("GCE Ordinary Level", "gce-a-level")]
        public void Un_libelle_d_un_autre_groupe_ne_correspond_pas(string examType, string requested)
        {
            Assert.False(ExamTypeGroups.Matches(examType, requested));
        }

        [Theory]
        [InlineData(null, "iut")]
        [InlineData("", "iut")]
        [InlineData("IUT", null)]
        [InlineData("IUT", "")]
        [InlineData("IUT", "   ")]
        public void Un_type_ou_un_groupe_vide_ne_correspond_jamais(string? examType, string? requested)
        {
            Assert.False(ExamTypeGroups.Matches(examType, requested));
        }
    }
}
