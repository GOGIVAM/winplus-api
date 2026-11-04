using System;
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
    /// Forum : pagination bornée, épinglés en tête, création avec numéros de
    /// téléphone masqués, compteur de réponses, vote unique pondéré par le rôle
    /// (professeur et établissement valent 3), réponse acceptée réservée à
    /// l'auteur du fil (épinglage si professeur vérifié), suppression par
    /// l'auteur ou un administrateur, suivi idempotent, tris du fil d'actualité.
    /// La recherche par texte (ILike) n'est pas testée : propre à PostgreSQL.
    /// </summary>
    public class ForumServiceTests
    {
        private const int Author = 1, Replier = 2, Teacher = 3, Stranger = 4;

        private static (TestApplicationDbContext Db, ForumService Svc) Build()
        {
            var db = TestApplicationDbContext.Create();
            db.Users.AddRange(
                new User { Id = Author, Email = "a@t.l", Role = "student", FirstName = "Awa", LastName = "N" },
                new User { Id = Replier, Email = "r@t.l", Role = "student" },
                new User { Id = Teacher, Email = "t@t.l", Role = "teacher" },
                new User { Id = Stranger, Email = "s@t.l", Role = "student" });
            db.SaveChanges();
            return (db, new ForumService(db, NullLogger<ForumService>.Instance));
        }

        private static ForumThread T(int id, int user = Author, string cat = "maths", DateTime? at = null, bool pinned = false, bool deleted = false, int replies = 0, int upvotes = 0, DateTime? updated = null) =>
            new() { Id = id, UserId = user, Title = $"T{id}", Content = "c", Category = cat, CreatedAt = at ?? new DateTime(2026, 1, id), UpdatedAt = updated ?? at ?? new DateTime(2026, 1, id), IsPinned = pinned, IsDeleted = deleted, RepliesCount = replies, Upvotes = upvotes };

        [Fact]
        public async Task La_liste_met_les_epingles_en_tete_exclut_les_supprimes_et_filtre_par_categorie()
        {
            var (db, svc) = Build();
            db.ForumThreads.AddRange(T(1), T(2, pinned: true), T(3, deleted: true), T(4, cat: "physique"), T(5));
            db.SaveChanges();
            var all = await svc.GetThreadsAsync(null, 1, 20);
            Assert.Equal(new[] { 2, 5, 4, 1 }, all.Threads.Select(t => t.Id));
            Assert.Equal(4, all.Total);
            Assert.Equal(new[] { 2, 5, 1 }, (await svc.GetThreadsAsync("maths", 1, 20)).Threads.Select(t => t.Id));
            Assert.Equal(4, (await svc.GetThreadsAsync("all", 1, 20)).Total);
        }

        [Theory]
        [InlineData(0, 0, 1, 20)]
        [InlineData(-5, 500, 1, 100)]
        public async Task La_pagination_est_corrigee(int page, int size, int expectedPage, int expectedSize)
        {
            var (_, svc) = Build();
            var r = await svc.GetThreadsAsync(null, page, size);
            Assert.Equal(expectedPage, r.Page);
            Assert.Equal(expectedSize, r.PageSize);
        }

        [Fact]
        public async Task Le_nombre_de_pages_est_arrondi_au_superieur()
        {
            var (db, svc) = Build();
            for (var i = 1; i <= 5; i++) db.ForumThreads.Add(T(i));
            db.SaveChanges();
            var r = await svc.GetThreadsAsync(null, 2, 2);
            Assert.Equal(3, r.TotalPages);
            Assert.Equal(2, r.Threads.Count);
        }

        [Fact]
        public async Task Un_fil_supprime_ou_inconnu_est_introuvable()
        {
            var (db, svc) = Build();
            db.ForumThreads.Add(T(1, deleted: true));
            db.SaveChanges();
            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.GetThreadByIdAsync(1));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.GetThreadByIdAsync(9));
        }

        [Fact]
        public async Task La_creation_d_un_fil_masque_les_numeros_de_telephone()
        {
            var (_, svc) = Build();
            var r = await svc.CreateThreadAsync(Author, new CreateThreadRequest { Title = "Aide", Content = "Appelle-moi au +33 6 12 34 56 78 svp", Category = "maths" });
            Assert.DoesNotContain("12 34 56 78", r.Content);
            Assert.Equal("Awa N", r.AuthorName);
            Assert.False(r.IsPinned);
        }

        [Fact]
        public async Task Repondre_incremente_le_compteur_et_refuse_un_fil_supprime_ou_inconnu()
        {
            var (db, svc) = Build();
            db.ForumThreads.Add(T(1));
            db.ForumThreads.Add(T(2, deleted: true));
            db.SaveChanges();
            var post = await svc.CreatePostAsync(1, Replier, new CreatePostRequest { Content = "Réponse" });
            Assert.Equal(1, db.ForumThreads.Single(t => t.Id == 1).RepliesCount);
            Assert.Equal(1, post.ThreadId);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.CreatePostAsync(2, Replier, new CreatePostRequest { Content = "x" }));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.CreatePostAsync(9, Replier, new CreatePostRequest { Content = "x" }));
        }

        [Fact]
        public async Task Lire_les_reponses_compte_une_vue_et_masque_les_reponses_supprimees_ou_cachees()
        {
            var (db, svc) = Build();
            db.ForumThreads.Add(T(1));
            db.ForumPosts.AddRange(
                new ForumPost { Id = 1, ThreadId = 1, UserId = Replier, Content = "a", CreatedAt = new DateTime(2026, 1, 2) },
                new ForumPost { Id = 2, ThreadId = 1, UserId = Replier, Content = "b", CreatedAt = new DateTime(2026, 1, 1) },
                new ForumPost { Id = 3, ThreadId = 1, UserId = Replier, Content = "c", IsDeleted = true },
                new ForumPost { Id = 4, ThreadId = 1, UserId = Replier, Content = "d", IsHidden = true });
            db.SaveChanges();
            var r = await svc.GetPostsAsync(1);
            Assert.Equal(new[] { 2, 1 }, r.Posts.Select(p => p.Id));
            Assert.Equal(2, r.Total);
            Assert.Equal(1, db.ForumThreads.Single().ViewsCount);
        }

        [Fact]
        public async Task Un_vote_positif_ajoute_le_poids_du_role_et_un_second_vote_est_refuse()
        {
            var (db, svc) = Build();
            db.ForumThreads.Add(T(1));
            db.ForumPosts.Add(new ForumPost { Id = 1, ThreadId = 1, UserId = Author, Content = "a" });
            db.SaveChanges();
            await svc.VoteOnPostAsync(1, Teacher, "up");
            await svc.VoteOnPostAsync(1, Replier, "up");
            Assert.Equal(4, db.ForumPosts.Single().Upvotes);
            await Assert.ThrowsAsync<ForumConflictException>(() => svc.VoteOnPostAsync(1, Teacher, "up"));
            Assert.Equal(3, db.ForumVotes.Single(v => v.UserId == Teacher).Weight);
        }

        [Fact]
        public async Task Un_vote_negatif_ne_descend_pas_sous_zero_et_une_reponse_inconnue_est_refusee()
        {
            var (db, svc) = Build();
            db.ForumThreads.Add(T(1));
            db.ForumPosts.Add(new ForumPost { Id = 1, ThreadId = 1, UserId = Author, Content = "a", Upvotes = 1 });
            db.SaveChanges();
            await svc.VoteOnPostAsync(1, Teacher, "down");
            Assert.Equal(0, db.ForumPosts.Single().Upvotes);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.VoteOnPostAsync(99, Replier, "up"));
        }

        [Fact]
        public async Task Seul_l_auteur_du_fil_accepte_une_reponse_et_le_fil_devient_resolu()
        {
            var (db, svc) = Build();
            db.ForumThreads.Add(T(1));
            db.ForumPosts.Add(new ForumPost { Id = 1, ThreadId = 1, UserId = Replier, Content = "a" });
            db.SaveChanges();
            await Assert.ThrowsAsync<ForumForbiddenException>(() => svc.AcceptPostAsync(1, Stranger));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.AcceptPostAsync(99, Author));
            await svc.AcceptPostAsync(1, Author);
            Assert.True(db.ForumPosts.Single().IsAccepted);
            var thread = db.ForumThreads.Single();
            Assert.True(thread.IsSolved);
            Assert.False(thread.IsPinned);
        }

        [Fact]
        public async Task La_reponse_d_un_professeur_au_diplome_verifie_epingle_le_fil()
        {
            var (db, svc) = Build();
            db.TutorProfiles.Add(new TutorProfile { Id = 1, UserId = Teacher, IsDiplomaVerified = true });
            db.ForumThreads.Add(T(1));
            db.ForumPosts.Add(new ForumPost { Id = 1, ThreadId = 1, UserId = Teacher, Content = "a" });
            db.SaveChanges();
            await svc.AcceptPostAsync(1, Author);
            Assert.True(db.ForumThreads.Single().IsPinned);
        }

        [Fact]
        public async Task La_suppression_est_reservee_a_l_auteur_ou_a_un_administrateur()
        {
            var (db, svc) = Build();
            db.ForumThreads.AddRange(T(1), T(2));
            db.SaveChanges();
            await Assert.ThrowsAsync<ForumForbiddenException>(() => svc.DeleteThreadAsync(1, Stranger, "student"));
            await svc.DeleteThreadAsync(1, Author, "student");
            await svc.DeleteThreadAsync(2, Stranger, "ADMIN");
            Assert.All(db.ForumThreads, t => Assert.True(t.IsDeleted));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.DeleteThreadAsync(1, Author, "student"));
            Assert.Null(await svc.GetThreadAuthorIdAsync(1));
        }

        [Fact]
        public async Task Suivre_est_idempotent_et_ne_plus_suivre_sans_suivi_ne_fait_rien()
        {
            var (db, svc) = Build();
            db.ForumThreads.Add(T(1));
            db.SaveChanges();
            await svc.FollowThreadAsync(Replier, 1);
            await svc.FollowThreadAsync(Replier, 1);
            Assert.Single(db.ForumThreadFollows);
            Assert.Equal(new[] { Replier }, await svc.GetThreadFollowerIdsAsync(1));
            Assert.Equal(new[] { 1 }, await svc.GetFollowedThreadIdsAsync(Replier));
            await svc.UnfollowThreadAsync(Stranger, 1);
            await svc.UnfollowThreadAsync(Replier, 1);
            Assert.Empty(db.ForumThreadFollows);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.FollowThreadAsync(Replier, 99));
        }

        [Fact]
        public async Task Le_fil_d_actualite_garde_les_epingles_en_tete_et_applique_le_tri()
        {
            var (db, svc) = Build();
            db.ForumThreads.AddRange(
                T(1, replies: 5, upvotes: 1), T(2, replies: 0, upvotes: 9), T(3, pinned: true, replies: 9), T(4, replies: 2, upvotes: 5, updated: new DateTime(2026, 6, 1)));
            db.SaveChanges();
            Assert.Equal(new[] { 3, 4, 2, 1 }, (await svc.GetFeedAsync(null, null, 1, 20, "recent", false)).Threads.Select(t => t.Id));
            Assert.Equal(new[] { 3, 2, 4, 1 }, (await svc.GetFeedAsync(null, null, 1, 20, "unanswered", false)).Threads.Select(t => t.Id));
            Assert.Equal(new[] { 3, 2, 4, 1 }, (await svc.GetFeedAsync(null, null, 1, 20, "popular", false)).Threads.Select(t => t.Id));
            Assert.Equal(new[] { 3, 1, 2, 4 }, (await svc.GetFeedAsync(null, null, 1, 20, "oldest", false)).Threads.Select(t => t.Id));
            Assert.Equal(3, (await svc.GetFeedAsync(null, null, 1, 20, "active", false)).Threads.First().Id);
        }

        [Fact]
        public async Task Le_fil_suivi_marque_les_fils_suivis_et_ignore_le_filtre_pour_un_anonyme()
        {
            var (db, svc) = Build();
            db.ForumThreads.AddRange(T(1), T(2));
            db.ForumThreadFollows.Add(new ForumThreadFollow { UserId = Replier, ThreadId = 2 });
            db.SaveChanges();
            var mine = await svc.GetFeedAsync(Replier, null, 1, 20, "recent", true);
            Assert.Equal(new[] { 2 }, mine.Threads.Select(t => t.Id));
            Assert.True(mine.Threads.Single().IsFollowed);
            var anon = await svc.GetFeedAsync(null, null, 1, 20, "recent", true);
            Assert.Equal(2, anon.Total);
        }
    }
}
