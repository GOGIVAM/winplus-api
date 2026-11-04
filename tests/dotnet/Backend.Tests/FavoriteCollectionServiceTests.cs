using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Collections de favoris : unicité du nom par utilisateur, valeurs par
    /// défaut, mise à jour partielle, suppression qui détache les favoris sans
    /// les supprimer, isolation entre utilisateurs, réordonnancement strict.
    /// </summary>
    public class FavoriteCollectionServiceTests
    {
        private const int U = 5001, V = 5002;

        private static (TestApplicationDbContext Db, FavoriteCollectionService Svc) Build()
        {
            var db = TestApplicationDbContext.Create();
            db.Users.Add(new User { Id = U, Email = "u@t.local", Role = "student" });
            db.Users.Add(new User { Id = V, Email = "v@t.local", Role = "student" });
            db.SaveChanges();
            return (db, new FavoriteCollectionService(db, NullLogger<FavoriteCollectionService>.Instance));
        }

        private static CreateFavoriteCollectionRequest Create(string name) => new() { Name = name, Description = null!, Color = null!, Icon = null! };

        [Fact]
        public async Task La_creation_applique_la_couleur_et_l_icone_par_defaut()
        {
            var (_, svc) = Build();
            var dto = await svc.CreateCollectionAsync(U, Create("Maths"));
            Assert.Equal("#3B82F6", dto.Color);
            Assert.Equal("folder", dto.Icon);
            Assert.Equal(0, dto.Order);
        }

        [Fact]
        public async Task Un_nom_deja_utilise_par_le_meme_utilisateur_est_refuse_mais_pas_par_un_autre()
        {
            var (_, svc) = Build();
            await svc.CreateCollectionAsync(U, Create("Maths"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateCollectionAsync(U, Create("Maths")));
            await svc.CreateCollectionAsync(V, Create("Maths"));
        }

        [Fact]
        public async Task La_creation_pour_un_utilisateur_inconnu_est_refusee()
        {
            var (_, svc) = Build();
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateCollectionAsync(999, Create("X")));
        }

        [Fact]
        public async Task La_mise_a_jour_est_partielle_et_ignore_les_champs_absents()
        {
            var (_, svc) = Build();
            var c = await svc.CreateCollectionAsync(U, new CreateFavoriteCollectionRequest { Name = "Maths", Description = "d", Color = "#111111", Icon = "star" });
            var upd = await svc.UpdateCollectionAsync(U, c.Id, new UpdateFavoriteCollectionRequest { Name = "  ", Description = null!, Color = "#222222", Icon = null! });
            Assert.Equal("Maths", upd.Name);
            Assert.Equal("d", upd.Description);
            Assert.Equal("#222222", upd.Color);
            Assert.Equal("star", upd.Icon);
            Assert.NotNull(upd.UpdatedAt);
        }

        [Fact]
        public async Task Renommer_vers_un_nom_deja_pris_est_refuse_mais_garder_son_nom_est_permis()
        {
            var (_, svc) = Build();
            var a = await svc.CreateCollectionAsync(U, Create("A"));
            await svc.CreateCollectionAsync(U, Create("B"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.UpdateCollectionAsync(U, a.Id, new UpdateFavoriteCollectionRequest { Name = "B" }));
            var same = await svc.UpdateCollectionAsync(U, a.Id, new UpdateFavoriteCollectionRequest { Name = "A" });
            Assert.Equal("A", same.Name);
        }

        [Fact]
        public async Task Modifier_la_collection_d_un_autre_ou_inexistante_est_introuvable()
        {
            var (_, svc) = Build();
            var a = await svc.CreateCollectionAsync(U, Create("A"));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.UpdateCollectionAsync(V, a.Id, new UpdateFavoriteCollectionRequest { Name = "Z" }));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.GetCollectionWithFavoritesAsync(V, a.Id));
        }

        [Fact]
        public async Task La_suppression_detache_les_favoris_sans_les_supprimer()
        {
            var (db, svc) = Build();
            var c = await svc.CreateCollectionAsync(U, Create("A"));
            db.Favorites.Add(new Favorite { Id = 1, UserId = U, SubjectId = 1, User = null!, Subject = null!, CollectionId = c.Id });
            db.SaveChanges();
            Assert.True(await svc.DeleteCollectionAsync(U, c.Id));
            Assert.Empty(db.FavoriteCollections);
            Assert.Null(db.Favorites.Single().CollectionId);
        }

        [Fact]
        public async Task La_suppression_d_une_collection_d_autrui_ou_inconnue_renvoie_faux()
        {
            var (db, svc) = Build();
            var c = await svc.CreateCollectionAsync(U, Create("A"));
            Assert.False(await svc.DeleteCollectionAsync(V, c.Id));
            Assert.False(await svc.DeleteCollectionAsync(U, 999));
            Assert.Single(db.FavoriteCollections);
        }

        [Fact]
        public async Task Les_collections_sont_triees_par_ordre_puis_nom_et_isolees_par_utilisateur()
        {
            var (_, svc) = Build();
            await svc.CreateCollectionAsync(U, Create("Zèbre"));
            await svc.CreateCollectionAsync(U, Create("Abeille"));
            await svc.CreateCollectionAsync(V, Create("Autre"));
            var list = await svc.GetUserCollectionsAsync(U);
            Assert.Equal(new[] { "Abeille", "Zèbre" }, list.Select(c => c.Name));
        }

        [Fact]
        public async Task Ajouter_un_favori_exige_que_favori_et_collection_appartiennent_a_l_utilisateur()
        {
            var (db, svc) = Build();
            var mine = await svc.CreateCollectionAsync(U, Create("A"));
            var theirs = await svc.CreateCollectionAsync(V, Create("B"));
            db.Favorites.Add(new Favorite { Id = 1, UserId = U, SubjectId = 1, User = null!, Subject = null! });
            db.SaveChanges();
            Assert.False(await svc.AddFavoriteToCollectionAsync(U, 1, theirs.Id));
            Assert.False(await svc.AddFavoriteToCollectionAsync(V, 1, theirs.Id));
            Assert.False(await svc.AddFavoriteToCollectionAsync(U, 99, mine.Id));
            Assert.True(await svc.AddFavoriteToCollectionAsync(U, 1, mine.Id));
            Assert.Equal(mine.Id, db.Favorites.Single().CollectionId);
            Assert.True(await svc.RemoveFavoriteFromCollectionAsync(U, 1));
            Assert.Null(db.Favorites.Single().CollectionId);
            Assert.False(await svc.RemoveFavoriteFromCollectionAsync(V, 1));
        }

        [Fact]
        public async Task Le_reordonnancement_applique_les_positions_et_refuse_une_liste_etrangere()
        {
            var (db, svc) = Build();
            var a = await svc.CreateCollectionAsync(U, Create("A"));
            var b = await svc.CreateCollectionAsync(U, Create("B"));
            var other = await svc.CreateCollectionAsync(V, Create("C"));
            Assert.True(await svc.ReorderCollectionsAsync(U, new List<int> { b.Id, a.Id }));
            Assert.Equal(0, db.FavoriteCollections.Single(c => c.Id == b.Id).Order);
            Assert.Equal(1, db.FavoriteCollections.Single(c => c.Id == a.Id).Order);
            Assert.False(await svc.ReorderCollectionsAsync(U, new List<int> { a.Id, other.Id }));
        }
    }
}
