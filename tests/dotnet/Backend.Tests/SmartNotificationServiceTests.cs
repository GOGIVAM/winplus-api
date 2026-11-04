using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Notification personnalisée par WinAI : le texte de l'IA remplace le texte
    /// de repli quand la réponse est un succès avec titre et corps non vides ; dans
    /// tous les autres cas (erreur HTTP, panne réseau, succès faux, JSON invalide)
    /// la notification part quand même avec le texte de repli. L'appel est
    /// authentifié et porte la langue du compte. Aucun appel réseau réel.
    /// </summary>
    public class SmartNotificationServiceTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _reply;
            public HttpRequestMessage? Last;
            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) => _reply = reply;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Last = request;
                return Task.FromResult(_reply(request));
            }
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
            new(code) { Content = new StringContent(body) };

        private static (SmartNotificationService Svc, Mock<INtfyService> Ntfy, StubHandler Handler) Build(
            Func<HttpRequestMessage, HttpResponseMessage> reply, TestApplicationDbContext? db = null)
        {
            var handler = new StubHandler(reply);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler));
            var token = new Mock<IServiceTokenProvider>();
            token.Setup(t => t.CreateAuthorizationHeader(ServiceScopes.Notification)).Returns("Bearer jeton-test");
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new[] { new KeyValuePair<string, string?>("AIService:BaseUrl", "http://ia.test") }).Build();
            var ntfy = new Mock<INtfyService>();
            return (new SmartNotificationService(ntfy.Object, factory.Object, NullLogger<SmartNotificationService>.Instance, cfg, token.Object, db), ntfy, handler);
        }

        private static void VerifySent(Mock<INtfyService> ntfy, string title, string body) =>
            ntfy.Verify(n => n.PublishAsync("topic", title, body, "default", null, 5, "General", null, null), Times.Once);

        [Fact]
        public async Task Une_reponse_reussie_remplace_le_titre_et_le_corps()
        {
            var (svc, ntfy, _) = Build(_ => Json("{\"success\":true,\"title\":\"Bravo Awa\",\"body\":\"Tu as réussi\"}"));
            await svc.PublishSmartAsync("topic", "Titre", "Corps", "quiz", 5);
            VerifySent(ntfy, "Bravo Awa", "Tu as réussi");
        }

        [Fact]
        public async Task Un_titre_vide_garde_le_titre_de_repli_mais_le_corps_de_l_ia_est_pris()
        {
            var (svc, ntfy, _) = Build(_ => Json("{\"success\":true,\"title\":\"  \",\"body\":\"Corps IA\"}"));
            await svc.PublishSmartAsync("topic", "Titre", "Corps", "quiz", 5);
            VerifySent(ntfy, "Titre", "Corps IA");
        }

        [Fact]
        public async Task Un_succes_faux_ou_absent_utilise_le_texte_de_repli()
        {
            var (svc, ntfy, _) = Build(_ => Json("{\"success\":false,\"title\":\"X\",\"body\":\"Y\"}"));
            await svc.PublishSmartAsync("topic", "Titre", "Corps", "quiz", 5);
            VerifySent(ntfy, "Titre", "Corps");
            var (svc2, ntfy2, _) = Build(_ => Json("{\"title\":\"X\",\"body\":\"Y\"}"));
            await svc2.PublishSmartAsync("topic", "Titre", "Corps", "quiz", 5);
            VerifySent(ntfy2, "Titre", "Corps");
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.TooManyRequests)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task Une_erreur_http_n_empeche_pas_l_envoi_du_texte_de_repli(HttpStatusCode code)
        {
            var (svc, ntfy, _) = Build(_ => Json("{\"success\":true,\"title\":\"X\",\"body\":\"Y\"}", code));
            await svc.PublishSmartAsync("topic", "Titre", "Corps", "quiz", 5);
            VerifySent(ntfy, "Titre", "Corps");
        }

        [Fact]
        public async Task Une_panne_reseau_ou_un_json_invalide_envoie_quand_meme_le_texte_de_repli()
        {
            var (svc, ntfy, _) = Build(_ => throw new HttpRequestException("réseau"));
            await svc.PublishSmartAsync("topic", "Titre", "Corps", "quiz", 5);
            VerifySent(ntfy, "Titre", "Corps");
            var (svc2, ntfy2, _) = Build(_ => Json("pas du json"));
            await svc2.PublishSmartAsync("topic", "Titre", "Corps", "quiz", 5);
            VerifySent(ntfy2, "Titre", "Corps");
        }

        [Fact]
        public async Task L_appel_vise_la_route_de_generation_avec_le_jeton_de_service()
        {
            var (svc, _, handler) = Build(_ => Json("{\"success\":false}"));
            await svc.PublishSmartAsync("topic", "T", "C", "quiz", 5, new Dictionary<string, object> { ["score"] = 90 });
            Assert.Equal("http://ia.test/api/ai/generate-notification", handler.Last!.RequestUri!.ToString());
            Assert.Equal("Bearer jeton-test", handler.Last.Headers.Authorization!.ToString());
        }

        [Fact]
        public async Task La_langue_du_compte_est_transmise_a_l_ia_et_absente_sans_base()
        {
            using var db = TestApplicationDbContext.Create();
            db.Users.Add(new User { Id = 5, Email = "e@t.l", Role = "student", Locale = "en-GB" });
            db.SaveChanges();
            var (svc, _, handler) = Build(_ => Json("{\"success\":false}"), db);
            await svc.PublishSmartAsync("topic", "T", "C", "quiz", 5);
            Assert.Equal("en", handler.Last!.Headers.GetValues(AiLocaleHeaderHandler.HeaderName).Single());

            var (svc2, _, handler2) = Build(_ => Json("{\"success\":false}"));
            await svc2.PublishSmartAsync("topic", "T", "C", "quiz", 5);
            Assert.False(handler2.Last!.Headers.Contains(AiLocaleHeaderHandler.HeaderName));
        }
    }
}
