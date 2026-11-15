using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Écritures « une seule fois » de la file d'attente hors ligne du mobile
    /// (lot 3) : une action rejouée avec la même Idempotency-Key reçoit la
    /// première réponse sans être exécutée une seconde fois.
    /// </summary>
    public class ClientIdempotencyMiddlewareTests
    {
        private sealed class Harness
        {
            private readonly string _name = $"idem-{Guid.NewGuid()}";
            private readonly InMemoryDatabaseRoot _root = new();
            public int Executions;
            public int Status = StatusCodes.Status201Created;
            public IServiceScopeFactory Scopes { get; }
            public ClientIdempotencyMiddleware Middleware { get; }

            public Harness()
            {
                var services = new ServiceCollection();
                services.AddScoped<ApplicationDbContext>(_ => TestApplicationDbContext.Create(_name, _root));
                Scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
                Middleware = new ClientIdempotencyMiddleware(async ctx =>
                {
                    Executions++;
                    ctx.Response.StatusCode = Status;
                    await ctx.Response.WriteAsync($"{{\"n\":{Executions}}}");
                }, NullLogger<ClientIdempotencyMiddleware>.Instance);
            }

            public async Task<(int status, string body, string replayed)> Call(string method, string? key, int? user = 7)
            {
                var ctx = new DefaultHttpContext();
                ctx.Request.Method = method;
                ctx.Request.Path = "/favorites";
                if (key != null) ctx.Request.Headers[ClientIdempotencyMiddleware.HeaderName] = key;
                if (user != null)
                    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", user.ToString()!) }, "test"));
                var body = new MemoryStream();
                ctx.Response.Body = body;
                await Middleware.InvokeAsync(ctx, Scopes);
                return (ctx.Response.StatusCode, Encoding.UTF8.GetString(body.ToArray()),
                    ctx.Response.Headers[ClientIdempotencyMiddleware.ReplayedHeader].ToString());
            }

            public int StoredKeys()
            {
                using var scope = Scopes.CreateScope();
                return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ClientRequestKeys.Count();
            }
        }

        [Fact]
        public async Task Meme_cle_rejouee_la_premiere_reponse_revient_sans_seconde_execution()
        {
            var h = new Harness();
            var premier = await h.Call("POST", "k-1");
            var second = await h.Call("POST", "k-1");
            Assert.Equal(1, h.Executions);
            Assert.Equal(StatusCodes.Status201Created, second.status);
            Assert.Equal(premier.body, second.body);
            Assert.Equal("true", second.replayed);
            Assert.Equal("", premier.replayed);
        }

        [Fact]
        public async Task Deux_cles_differentes_deux_executions()
        {
            var h = new Harness();
            await h.Call("POST", "k-1");
            await h.Call("POST", "k-2");
            Assert.Equal(2, h.Executions);
        }

        [Fact]
        public async Task La_meme_cle_pour_deux_comptes_reste_independante()
        {
            var h = new Harness();
            await h.Call("PUT", "k-1", user: 7);
            await h.Call("PUT", "k-1", user: 8);
            Assert.Equal(2, h.Executions);
        }

        [Fact]
        public async Task Une_erreur_serveur_laisse_l_action_rejouable()
        {
            var h = new Harness { Status = StatusCodes.Status500InternalServerError };
            await h.Call("POST", "k-1");
            h.Status = StatusCodes.Status200OK;
            var reprise = await h.Call("POST", "k-1");
            Assert.Equal(2, h.Executions);
            Assert.Equal(StatusCodes.Status200OK, reprise.status);
        }

        [Fact]
        public async Task Un_refus_definitif_est_garde_comme_la_reponse()
        {
            var h = new Harness { Status = StatusCodes.Status400BadRequest };
            await h.Call("POST", "k-1");
            var reprise = await h.Call("POST", "k-1");
            Assert.Equal(1, h.Executions);
            Assert.Equal(StatusCodes.Status400BadRequest, reprise.status);
        }

        [Fact]
        public async Task Sans_en_tete_sans_session_ou_en_lecture_rien_ne_change()
        {
            var h = new Harness();
            await h.Call("POST", null);
            await h.Call("POST", null);
            await h.Call("POST", "k-1", user: null);
            await h.Call("POST", "k-1", user: null);
            await h.Call("GET", "k-1");
            await h.Call("GET", "k-1");
            Assert.Equal(6, h.Executions);
            Assert.Equal(0, h.StoredKeys());
        }

        [Fact]
        public async Task Cle_trop_longue_ignoree()
        {
            var h = new Harness();
            var longue = new string('x', 101);
            await h.Call("POST", longue);
            await h.Call("POST", longue);
            Assert.Equal(2, h.Executions);
        }
    }
}
