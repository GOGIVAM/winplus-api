using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Backend.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Limitation de débit des routes d'authentification (Module 20) : limites de
    /// production par action, compteur partagé entre les alias d'une même action,
    /// compteurs indépendants entre actions, et décompte par adresse de
    /// connexion (jamais par un en-tête que le client contrôle).
    ///
    /// Le registre du middleware est statique : chaque test utilise sa propre
    /// adresse IP (plage 10.201.x.x) pour ne jamais partager de compteur.
    /// </summary>
    public class RateLimitingMiddlewareTests
    {
        private sealed class Harness
        {
            public int NextCalls;
            public RateLimitingMiddleware Middleware { get; }

            public Harness()
            {
                Middleware = new RateLimitingMiddleware(_ => { NextCalls++; return Task.CompletedTask; },
                    NullLogger<RateLimitingMiddleware>.Instance);
            }

            public async Task<DefaultHttpContext> Call(string path, string ip)
            {
                var ctx = new DefaultHttpContext();
                ctx.Request.Path = path;
                ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
                ctx.Response.Body = new MemoryStream();
                await Middleware.InvokeAsync(ctx);
                return ctx;
            }

            public async Task<int> CallMany(string path, string ip, int times)
            {
                var statusOfLast = 0;
                for (var i = 0; i < times; i++) statusOfLast = (await Call(path, ip)).Response.StatusCode;
                return statusOfLast;
            }
        }

        private static JsonElement Body(DefaultHttpContext ctx)
        {
            ctx.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(ctx.Response.Body);
            return JsonDocument.Parse(reader.ReadToEnd()).RootElement.Clone();
        }

        [Fact]
        public async Task La_connexion_est_limitee_a_huit_tentatives_puis_refusee_en_429()
        {
            var h = new Harness();
            const string ip = "10.201.0.1";

            Assert.Equal(200, await h.CallMany("/auth/signin", ip, 8));
            Assert.Equal(8, h.NextCalls);

            var ninth = await h.Call("/auth/signin", ip);
            Assert.Equal(StatusCodes.Status429TooManyRequests, ninth.Response.StatusCode);
            Assert.Equal(8, h.NextCalls); // la 9e n'a pas ete transmise
            var body = Body(ninth);
            Assert.Equal("Too many login attempts. Please try again later.", body.GetProperty("error").GetString());
            Assert.Equal(15, body.GetProperty("retryAfter").GetInt32());
        }

        [Fact]
        public async Task Une_autre_adresse_a_son_propre_compteur()
        {
            var h = new Harness();
            await h.CallMany("/auth/signin", "10.201.0.2", 9);   // la 9e est refusee
            var other = await h.Call("/auth/signin", "10.201.0.3");
            Assert.Equal(200, other.Response.StatusCode);
        }

        [Fact]
        public async Task L_inscription_est_limitee_a_dix_tentatives_et_les_alias_partagent_le_meme_compteur()
        {
            var h = new Harness();
            const string ip = "10.201.0.4";

            Assert.Equal(200, await h.CallMany("/auth/signup", ip, 5));
            Assert.Equal(200, await h.CallMany("/auth/register", ip, 5));  // 10e au total, encore permise

            var eleventh = await h.Call("/auth/register", ip);
            Assert.Equal(StatusCodes.Status429TooManyRequests, eleventh.Response.StatusCode);
            var body = Body(eleventh);
            Assert.Equal("Too many signup attempts. Please try again later.", body.GetProperty("error").GetString());
            Assert.Equal(1, body.GetProperty("retryAfter").GetInt32());

            // L'alias d'origine est refuse aussi : meme compteur.
            Assert.Equal(StatusCodes.Status429TooManyRequests, (await h.Call("/auth/signup", ip)).Response.StatusCode);
        }

        [Fact]
        public async Task La_reinitialisation_du_mot_de_passe_est_limitee_a_six_demandes()
        {
            var h = new Harness();
            const string ip = "10.201.0.5";
            Assert.Equal(200, await h.CallMany("/auth/forgot-password", ip, 6));

            var seventh = await h.Call("/auth/forgot-password", ip);
            Assert.Equal(StatusCodes.Status429TooManyRequests, seventh.Response.StatusCode);
            Assert.Equal("Too many password reset attempts. Please try again later.", Body(seventh).GetProperty("error").GetString());
        }

        private static readonly string[] SensitivePaths =
        {
            "/auth/reset-password", "/auth/verify-reset-token", "/auth/verify-email", "/auth/resend-verification",
            "/auth/refresh", "/auth/send-confirmation-code", "/auth/verify-confirmation",
        };

        [Theory]
        [InlineData("/auth/reset-password")]
        [InlineData("/auth/verify-reset-token")]
        [InlineData("/auth/verify-email")]
        [InlineData("/auth/resend-verification")]
        [InlineData("/auth/refresh")]
        [InlineData("/auth/send-confirmation-code")]
        [InlineData("/auth/verify-confirmation")]
        public async Task Les_routes_sensibles_sont_limitees_a_douze_tentatives_sur_un_compteur_commun(string path)
        {
            var h = new Harness();
            // Une adresse distincte par route de la theorie : aucun compteur partage entre les cas.
            var ip = "10.201.1." + (Array.IndexOf(SensitivePaths, path) + 1);
            Assert.Equal(200, await h.CallMany(path, ip, 12));

            var thirteenth = await h.Call(path, ip);
            Assert.Equal(StatusCodes.Status429TooManyRequests, thirteenth.Response.StatusCode);
            var body = Body(thirteenth);
            Assert.Equal("Too many authentication attempts. Please try again later.", body.GetProperty("error").GetString());
            Assert.Equal(15, body.GetProperty("retryAfter").GetInt32());
        }

        [Fact]
        public async Task Les_routes_sensibles_partagent_un_meme_compteur_entre_elles()
        {
            var h = new Harness();
            const string ip = "10.201.0.6";
            Assert.Equal(200, await h.CallMany("/auth/refresh", ip, 7));
            Assert.Equal(200, await h.CallMany("/auth/verify-email", ip, 5));
            Assert.Equal(StatusCodes.Status429TooManyRequests, (await h.Call("/auth/reset-password", ip)).Response.StatusCode);
        }

        [Fact]
        public async Task Epuiser_une_action_ne_bloque_pas_les_autres_actions_de_la_meme_adresse()
        {
            var h = new Harness();
            const string ip = "10.201.0.7";
            await h.CallMany("/auth/refresh", ip, 13);   // compteur « authentication » epuise
            Assert.Equal(StatusCodes.Status429TooManyRequests, (await h.Call("/auth/refresh", ip)).Response.StatusCode);

            // La connexion garde son propre quota (rafraichir un jeton ne doit pas bloquer la connexion).
            Assert.Equal(200, (await h.Call("/auth/signin", ip)).Response.StatusCode);
        }

        [Fact]
        public async Task Les_routes_hors_authentification_ne_sont_jamais_limitees()
        {
            var h = new Harness();
            Assert.Equal(200, await h.CallMany("/api/courses", "10.201.0.8", 60));
            Assert.Equal(60, h.NextCalls);
        }

        [Fact]
        public async Task La_correspondance_de_route_ignore_la_casse_et_accepte_un_prefixe()
        {
            var h = new Harness();
            const string ip = "10.201.0.9";
            Assert.Equal(200, await h.CallMany("/AUTH/SIGNIN", ip, 4));
            Assert.Equal(200, await h.CallMany("/api/auth/signin", ip, 4));
            Assert.Equal(StatusCodes.Status429TooManyRequests, (await h.Call("/auth/signin", ip)).Response.StatusCode);
        }

        [Fact]
        public async Task L_en_tete_X_Forwarded_For_n_est_pas_lu_par_le_middleware()
        {
            var h = new Harness();
            const string ip = "10.201.0.10";

            // Changer l'en-tete a chaque requete ne donne pas un compteur neuf (faille d'origine).
            for (var i = 0; i < 8; i++)
            {
                var ctx = new DefaultHttpContext();
                ctx.Request.Path = "/auth/signin";
                ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
                ctx.Request.Headers["X-Forwarded-For"] = $"203.0.113.{i + 1}";
                ctx.Response.Body = new MemoryStream();
                await h.Middleware.InvokeAsync(ctx);
                Assert.Equal(200, ctx.Response.StatusCode);
            }

            var spoofed = new DefaultHttpContext();
            spoofed.Request.Path = "/auth/signin";
            spoofed.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            spoofed.Request.Headers["X-Forwarded-For"] = "198.51.100.77";
            spoofed.Response.Body = new MemoryStream();
            await h.Middleware.InvokeAsync(spoofed);
            Assert.Equal(StatusCodes.Status429TooManyRequests, spoofed.Response.StatusCode);
        }

        [Fact]
        public async Task Sans_adresse_de_connexion_les_requetes_partagent_le_compteur_Unknown()
        {
            var h = new Harness();
            // Pas d'adresse distante (DefaultHttpContext) : cle « Unknown ».
            async Task<int> Once()
            {
                var ctx = new DefaultHttpContext();
                ctx.Request.Path = "/auth/forgot-password";
                ctx.Response.Body = new MemoryStream();
                await h.Middleware.InvokeAsync(ctx);
                return ctx.Response.StatusCode;
            }

            // Le compteur « Unknown » est statique : on constate seulement qu'il finit par bloquer.
            var last = 200;
            for (var i = 0; i < 7; i++) last = await Once();
            Assert.Equal(StatusCodes.Status429TooManyRequests, last);
        }
    }
}
