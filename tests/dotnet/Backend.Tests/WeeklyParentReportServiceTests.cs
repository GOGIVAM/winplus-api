using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Backend.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Rapport hebdomadaire aux parents : activation lue dans la biographie JSON,
    /// délai jusqu'au prochain lundi 8 h UTC, synthèse WinAI avec repli quand
    /// l'IA échoue. La classe est une tâche de fond dont les méthodes sont
    /// privées : elles sont appelées par réflexion (le code de production n'est
    /// pas modifié). IA simulée, jeton de service simulé.
    /// </summary>
    public class WeeklyParentReportServiceTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpResponseMessage> _reply;
            public string? Body;
            public HttpRequestMessage? Last;
            public StubHandler(Func<HttpResponseMessage> reply) => _reply = reply;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Last = request;
                Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
                return _reply();
            }
        }

        private static (WeeklyParentReportService Svc, StubHandler Handler) Build(Func<HttpResponseMessage> reply)
        {
            var handler = new StubHandler(reply);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler) { BaseAddress = new Uri("http://ia.test") });
            var token = new Mock<IServiceTokenProvider>();
            token.Setup(t => t.CreateAuthorizationHeader(ServiceScopes.ParentReport)).Returns("Bearer service");
            var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
            return (new WeeklyParentReportService(scopes, NullLogger<WeeklyParentReportService>.Instance, factory.Object, token.Object), handler);
        }

        private static T Static<T>(string name, params object?[] args) =>
            (T)typeof(WeeklyParentReportService).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;

        private static async Task<T> Instance<T>(WeeklyParentReportService svc, string name, params object?[] args)
        {
            var task = (Task<T>)typeof(WeeklyParentReportService).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(svc, args)!;
            return await task;
        }

        private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

        [Theory]
        [InlineData("{\"weeklyReport\":true}", true)]
        [InlineData("  {\"weeklyReport\":true,\"autre\":1}", true)]
        [InlineData("{\"weeklyReport\":false}", false)]
        [InlineData("{\"autre\":true}", false)]
        [InlineData("{\"weeklyReport\":\"oui\"}", false)]
        [InlineData("{ json invalide", false)]
        [InlineData("texte libre", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void L_activation_est_lue_dans_la_biographie_json(string? bio, bool expected)
        {
            Assert.Equal(expected, Static<bool>("IsWeeklyReportEnabled", bio));
        }

        [Fact]
        public void Le_delai_jusqu_au_prochain_lundi_est_positif_et_au_plus_sept_jours()
        {
            var delay = Static<TimeSpan>("ComputeDelayUntilNextMonday");
            Assert.True(delay > TimeSpan.Zero);
            Assert.True(delay <= TimeSpan.FromDays(7));
        }

        [Fact]
        public void La_synthese_de_repli_depend_de_la_presence_d_activite()
        {
            Assert.Contains("ont été actifs", Static<string>("FallbackSynthesis", new List<string> { "Awa a fait 3 quiz" }));
            Assert.Contains("Aucune activité", Static<string>("FallbackSynthesis", new List<string>()));
        }

        [Fact]
        public async Task La_synthese_reprend_le_texte_de_l_IA_avec_le_jeton_de_service()
        {
            var (svc, handler) = Build(() => Ok("{\"content\":\"Bravo cette semaine.\"}"));
            var text = await Instance<string>(svc, "GenerateSynthesisAsync", new List<string> { "Awa : 3 quiz" }, "Mme Mbarga", CancellationToken.None);
            Assert.Equal("Bravo cette semaine.", text);
            Assert.Equal("Bearer service", handler.Last!.Headers.Authorization!.ToString());
            Assert.Equal("http://ia.test/api/chatbot/chat", handler.Last.RequestUri!.ToString());
            Assert.Contains("Mme Mbarga", handler.Body);
            Assert.Contains("Awa : 3 quiz", handler.Body);
        }

        [Fact]
        public async Task Une_erreur_http_ou_un_contenu_vide_donnent_la_synthese_de_repli()
        {
            var (failing, _) = Build(() => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("refus") });
            var t1 = await Instance<string>(failing, "GenerateSynthesisAsync", new List<string> { "x" }, "P", CancellationToken.None);
            Assert.Contains("ont été actifs", t1);

            var (empty, _) = Build(() => Ok("{\"content\":\"  \"}"));
            var t2 = await Instance<string>(empty, "GenerateSynthesisAsync", new List<string>(), "P", CancellationToken.None);
            Assert.Contains("Aucune activité", t2);
        }

        [Fact]
        public async Task Une_panne_reseau_donne_la_synthese_de_repli_sans_exception()
        {
            var (svc, _) = Build(() => throw new HttpRequestException("injoignable"));
            var t = await Instance<string>(svc, "GenerateSynthesisAsync", new List<string> { "x" }, "P", CancellationToken.None);
            Assert.Contains("ont été actifs", t);
        }

        [Fact]
        public async Task L_appel_IA_renvoie_null_quand_la_reponse_n_a_pas_de_contenu_ou_est_en_erreur()
        {
            var (noContent, _) = Build(() => Ok("{\"autre\":1}"));
            Assert.Null(await Instance<string?>(noContent, "CallWinAiAsync", new { messages = new object[0] }, "test", CancellationToken.None));
            var (err, _) = Build(() => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("oups") });
            Assert.Null(await Instance<string?>(err, "CallWinAiAsync", new { messages = new object[0] }, "test", CancellationToken.None));
        }
    }
}
