using System;
using System.Linq;
using System.Threading.Tasks;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Pagination partagée (PaginationParams, PaginationResponse et l'extension
    /// ToPaginatedListAsync) et reconnaissance des conflits de transaction
    /// Serializable (DbConcurrency, Module 19). Base en mémoire pour la lecture
    /// paginée, aucune base pour le reste.
    /// </summary>
    public class PaginationAndConcurrencyTests
    {
        private static TestApplicationDbContext SeedUsers(int count)
        {
            var db = TestApplicationDbContext.Create();
            for (var i = 1; i <= count; i++)
                db.Users.Add(new User { Id = i, Email = $"u{i}@test.local", Role = "student", IsActive = true });
            db.SaveChanges();
            return db;
        }

        // ── PaginationParams ────────────────────────────────────────────

        [Fact]
        public void Les_parametres_de_pagination_ont_des_valeurs_par_defaut()
        {
            var p = new PaginationParams();
            Assert.Equal(1, p.Page);
            Assert.Equal(20, p.PageSize);
            Assert.Equal(0, p.Skip);
            Assert.Equal(20, p.Take);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(-7, 1)]
        [InlineData(1, 1)]
        [InlineData(5, 5)]
        public void Le_numero_de_page_est_ramene_a_un_minimum_de_un(int input, int expected)
        {
            Assert.Equal(expected, new PaginationParams { Page = input }.Page);
        }

        [Theory]
        [InlineData(0, 20)]
        [InlineData(-3, 20)]
        [InlineData(1, 1)]
        [InlineData(50, 50)]
        [InlineData(100, 100)]
        [InlineData(101, 100)]
        [InlineData(5000, 100)]
        public void La_taille_de_page_est_bornee_entre_un_et_cent_et_une_valeur_invalide_redonne_vingt(int input, int expected)
        {
            Assert.Equal(expected, new PaginationParams { PageSize = input }.PageSize);
        }

        [Fact]
        public void Skip_et_Take_decoulent_de_la_page_et_de_la_taille()
        {
            var p = new PaginationParams { Page = 4, PageSize = 25 };
            Assert.Equal(75, p.Skip);
            Assert.Equal(25, p.Take);
        }

        // ── PaginationResponse ──────────────────────────────────────────

        [Fact]
        public void La_reponse_calcule_le_nombre_de_pages_et_les_pages_voisines()
        {
            var middle = new PaginationResponse<int>(new[] { 1, 2 }, 25, 2, 10);
            Assert.Equal(3, middle.TotalPages);
            Assert.True(middle.HasNextPage);
            Assert.True(middle.HasPreviousPage);

            var first = new PaginationResponse<int>(new[] { 1 }, 25, 1, 10);
            Assert.True(first.HasNextPage);
            Assert.False(first.HasPreviousPage);

            var last = new PaginationResponse<int>(new[] { 1 }, 25, 3, 10);
            Assert.False(last.HasNextPage);
            Assert.True(last.HasPreviousPage);
        }

        [Fact]
        public void Un_total_exactement_multiple_de_la_taille_ne_cree_pas_de_page_vide()
        {
            Assert.Equal(2, new PaginationResponse<int>(Array.Empty<int>(), 20, 1, 10).TotalPages);
        }

        [Fact]
        public void Une_reponse_sans_element_n_a_aucune_page()
        {
            var r = new PaginationResponse<int>(Array.Empty<int>(), 0, 1, 10);
            Assert.Equal(0, r.TotalPages);
            Assert.False(r.HasNextPage);
            Assert.False(r.HasPreviousPage);
        }

        // ── ToPaginatedListAsync ────────────────────────────────────────

        [Fact]
        public async Task La_page_du_milieu_renvoie_la_bonne_tranche_et_le_total()
        {
            using var db = SeedUsers(25);
            var r = await db.Users.OrderBy(u => u.Id).ToPaginatedListAsync(2, 10);
            Assert.Equal(25, r.TotalCount);
            Assert.Equal(3, r.TotalPages);
            Assert.Equal(Enumerable.Range(11, 10), r.Items.Select(u => u.Id));
            Assert.True(r.HasNextPage);
            Assert.True(r.HasPreviousPage);
        }

        [Fact]
        public async Task La_derniere_page_est_partielle_et_sans_page_suivante()
        {
            using var db = SeedUsers(25);
            var r = await db.Users.OrderBy(u => u.Id).ToPaginatedListAsync(3, 10);
            Assert.Equal(Enumerable.Range(21, 5), r.Items.Select(u => u.Id));
            Assert.False(r.HasNextPage);
        }

        [Fact]
        public async Task Une_page_au_dela_de_la_fin_est_vide_mais_le_total_reste_exact()
        {
            using var db = SeedUsers(5);
            var r = await db.Users.OrderBy(u => u.Id).ToPaginatedListAsync(9, 10);
            Assert.Empty(r.Items);
            Assert.Equal(5, r.TotalCount);
        }

        [Fact]
        public async Task Une_page_nulle_ou_negative_est_ramenee_a_la_premiere()
        {
            using var db = SeedUsers(12);
            var r = await db.Users.OrderBy(u => u.Id).ToPaginatedListAsync(0, 5);
            Assert.Equal(1, r.Page);
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, r.Items.Select(u => u.Id));
        }

        [Fact]
        public async Task La_taille_est_bornee_entre_un_et_cent()
        {
            using var db = SeedUsers(150);
            var tooBig = await db.Users.OrderBy(u => u.Id).ToPaginatedListAsync(1, 500);
            Assert.Equal(100, tooBig.PageSize);
            Assert.Equal(100, tooBig.Items.Count());

            var tooSmall = await db.Users.OrderBy(u => u.Id).ToPaginatedListAsync(1, 0);
            Assert.Equal(1, tooSmall.PageSize);
            Assert.Single(tooSmall.Items);
            Assert.Equal(150, tooSmall.TotalPages);
        }

        [Fact]
        public async Task La_surcharge_a_parametres_de_pagination_applique_les_memes_regles()
        {
            using var db = SeedUsers(30);
            var r = await db.Users.OrderBy(u => u.Id).ToPaginatedListAsync(new PaginationParams { Page = 2, PageSize = 20 });
            Assert.Equal(10, r.Items.Count());
            Assert.Equal(2, r.Page);
            Assert.Equal(20, r.PageSize);
            Assert.Equal(2, r.TotalPages);
        }

        [Fact]
        public async Task Le_total_tient_compte_du_filtre_applique_avant_la_pagination()
        {
            using var db = SeedUsers(20);
            var r = await db.Users.Where(u => u.Id % 2 == 0).OrderBy(u => u.Id).ToPaginatedListAsync(1, 4);
            Assert.Equal(10, r.TotalCount);
            Assert.Equal(new[] { 2, 4, 6, 8 }, r.Items.Select(u => u.Id));
        }

        // ── DbConcurrency ───────────────────────────────────────────────

        private static PostgresException Pg(string sqlState) =>
            new("erreur de test", "ERROR", "ERROR", sqlState);

        [Fact]
        public void Un_echec_de_serialisation_Postgres_est_reconnu()
        {
            Assert.True(DbConcurrency.IsSerializationFailure(Pg(PostgresErrorCodes.SerializationFailure)));
        }

        [Fact]
        public void Un_interblocage_Postgres_est_reconnu()
        {
            Assert.True(DbConcurrency.IsSerializationFailure(Pg(PostgresErrorCodes.DeadlockDetected)));
        }

        [Fact]
        public void L_erreur_enveloppee_dans_une_DbUpdateException_est_reconnue()
        {
            var wrapped = new DbUpdateException("echec", new InvalidOperationException("couche", Pg(PostgresErrorCodes.SerializationFailure)));
            Assert.True(DbConcurrency.IsSerializationFailure(wrapped));
        }

        [Fact]
        public void Une_autre_erreur_Postgres_n_est_pas_un_conflit_de_transaction()
        {
            Assert.False(DbConcurrency.IsSerializationFailure(Pg(PostgresErrorCodes.UniqueViolation)));
        }

        [Fact]
        public void Une_exception_ordinaire_n_est_pas_un_conflit_de_transaction()
        {
            Assert.False(DbConcurrency.IsSerializationFailure(new InvalidOperationException("autre")));
            Assert.False(DbConcurrency.IsSerializationFailure(new DbUpdateException("sans cause")));
        }
    }
}
