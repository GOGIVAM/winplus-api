using System;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Backend.Middlewares;
using Backend.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Revalidation de l'état du compte à chaque requête authentifiée (Module 20,
    /// lot 9.7a 18.1) : un compte suspendu ou supprimé est refusé même avec un
    /// jeton encore valide, un changement de rôle invalide les anciens jetons, et
    /// l'état est mis en cache 30 secondes (invalidable par l'administration).
    /// Base en mémoire, cache en mémoire : aucune horloge réelle n'est attendue.
    /// </summary>
    public class AccountStatusMiddlewareTests
    {
        private sealed class Harness
        {
            public int NextCalls;
            public TestApplicationDbContext Db { get; } = TestApplicationDbContext.Create();
            public IMemoryCache Cache { get; } = new MemoryCache(new MemoryCacheOptions());
            public AccountStatusMiddleware Middleware { get; }

            public Harness()
            {
                Middleware = new AccountStatusMiddleware(_ => { NextCalls++; return Task.CompletedTask; },
                    NullLogger<AccountStatusMiddleware>.Instance);
            }

            public void AddUser(int id, string role = "student", bool active = true, bool deleted = false)
            {
                Db.Users.Add(new User { Id = id, Email = $"u{id}@test.local", Role = role, IsActive = active, IsDeleted = deleted });
                Db.SaveChanges();
            }

            public async Task<DefaultHttpContext> Call(string? sub, string? role = null, string subClaimType = "sub", bool authenticated = true)
            {
                var ctx = new DefaultHttpContext();
                ctx.Response.Body = new MemoryStream();
                if (authenticated)
                {
                    var claims = new System.Collections.Generic.List<Claim>();
                    if (sub != null) claims.Add(new Claim(subClaimType, sub));
                    if (role != null) claims.Add(new Claim("role", role));
                    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
                }
                await Middleware.InvokeAsync(ctx, Db, Cache);
                return ctx;
            }
        }

        private static JsonElement Body(DefaultHttpContext ctx)
        {
            ctx.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(ctx.Response.Body);
            return JsonDocument.Parse(reader.ReadToEnd()).RootElement.Clone();
        }

        [Fact]
        public async Task Un_compte_actif_dont_le_role_correspond_passe()
        {
            var h = new Harness();
            h.AddUser(5, "teacher");
            var ctx = await h.Call("5", "teacher");
            Assert.Equal(1, h.NextCalls);
            Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
        }

        [Fact]
        public async Task Un_compte_suspendu_est_refuse_en_401_avec_le_code_account_inactive()
        {
            var h = new Harness();
            h.AddUser(6, active: false);
            var ctx = await h.Call("6", "student");
            Assert.Equal(0, h.NextCalls);
            Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
            Assert.Equal("account_inactive", Body(ctx).GetProperty("code").GetString());
        }

        [Fact]
        public async Task Un_compte_supprime_est_refuse_meme_s_il_est_invisible_des_requetes_ordinaires()
        {
            var h = new Harness();
            h.AddUser(7, deleted: true);
            var ctx = await h.Call("7", "student");
            Assert.Equal(0, h.NextCalls);
            Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
            Assert.Equal("account_inactive", Body(ctx).GetProperty("code").GetString());
        }

        [Fact]
        public async Task Un_identifiant_inconnu_n_est_pas_traite_comme_une_suspension()
        {
            var h = new Harness();
            var ctx = await h.Call("999", "student");
            Assert.Equal(1, h.NextCalls);
            Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
        }

        [Fact]
        public async Task Un_role_modifie_en_base_invalide_le_jeton_avec_le_code_role_changed()
        {
            var h = new Harness();
            h.AddUser(8, "teacher");
            var ctx = await h.Call("8", "student");
            Assert.Equal(0, h.NextCalls);
            Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
            var body = Body(ctx);
            Assert.Equal("role_changed", body.GetProperty("code").GetString());
            Assert.Contains("reconnecter", body.GetProperty("error").GetString());
        }

        [Fact]
        public async Task La_comparaison_du_role_ignore_la_casse()
        {
            var h = new Harness();
            h.AddUser(9, "Admin");
            await h.Call("9", "admin");
            Assert.Equal(1, h.NextCalls);
        }

        [Fact]
        public async Task Un_jeton_sans_role_n_est_pas_compare_au_role_en_base()
        {
            var h = new Harness();
            h.AddUser(10, "teacher");
            await h.Call("10", role: null);
            Assert.Equal(1, h.NextCalls);
        }

        [Fact]
        public async Task Le_role_est_lu_aussi_depuis_la_claim_standard_ClaimTypes_Role()
        {
            var h = new Harness();
            h.AddUser(11, "teacher");
            var ctx = new DefaultHttpContext();
            ctx.Response.Body = new MemoryStream();
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("sub", "11"), new Claim(ClaimTypes.Role, "student"),
            }, "test"));
            await h.Middleware.InvokeAsync(ctx, h.Db, h.Cache);
            Assert.Equal(401, ctx.Response.StatusCode);
            Assert.Equal("role_changed", Body(ctx).GetProperty("code").GetString());
        }

        [Fact]
        public async Task L_identifiant_peut_venir_de_la_claim_NameIdentifier()
        {
            var h = new Harness();
            h.AddUser(12, active: false);
            var ctx = await h.Call("12", "student", subClaimType: ClaimTypes.NameIdentifier);
            Assert.Equal(401, ctx.Response.StatusCode);
        }

        [Fact]
        public async Task Une_requete_anonyme_passe_sans_lire_la_base()
        {
            var h = new Harness();
            h.AddUser(13, active: false);
            var ctx = await h.Call(null, authenticated: false);
            Assert.Equal(1, h.NextCalls);
            Assert.Equal(200, ctx.Response.StatusCode);
            Assert.False(h.Cache.TryGetValue(AccountStatusMiddleware.CacheKey(13), out _));
        }

        [Fact]
        public async Task Un_identifiant_non_numerique_est_ignore()
        {
            var h = new Harness();
            await h.Call("pas-un-nombre", "student");
            Assert.Equal(1, h.NextCalls);
        }

        [Fact]
        public async Task L_etat_est_mis_en_cache_et_une_suspension_n_agit_qu_apres_invalidation()
        {
            var h = new Harness();
            h.AddUser(14);
            await h.Call("14", "student");
            Assert.Equal(1, h.NextCalls);
            Assert.True(h.Cache.TryGetValue(AccountStatusMiddleware.CacheKey(14), out _));

            // Suspension en base : le cache de 30 s masque encore le changement.
            var user = h.Db.Users.Find(14)!;
            user.IsActive = false;
            h.Db.SaveChanges();
            await h.Call("14", "student");
            Assert.Equal(2, h.NextCalls);

            // L'administration invalide l'entree : la decision prend effet a la requete suivante.
            AccountStatusMiddleware.Invalidate(h.Cache, 14);
            var ctx = await h.Call("14", "student");
            Assert.Equal(2, h.NextCalls);
            Assert.Equal(401, ctx.Response.StatusCode);
        }

        [Fact]
        public void La_cle_de_cache_est_propre_a_chaque_compte()
        {
            Assert.Equal("account-status:7", AccountStatusMiddleware.CacheKey(7));
            Assert.NotEqual(AccountStatusMiddleware.CacheKey(7), AccountStatusMiddleware.CacheKey(8));
        }

        [Fact]
        public void Invalidate_d_une_entree_absente_ne_leve_aucune_erreur()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            AccountStatusMiddleware.Invalidate(cache, 404);
            Assert.False(cache.TryGetValue(AccountStatusMiddleware.CacheKey(404), out _));
        }
    }
}
