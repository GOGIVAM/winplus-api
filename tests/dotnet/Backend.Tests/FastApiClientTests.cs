using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Remplace l'ancien FlaskIntegrationTests.cs (Module 26) : celui-ci
    /// redéfinissait localement une classe FlaskClient et testait donc sa propre
    /// copie, jamais le client réellement livré. Ces tests reprennent les mêmes
    /// scénarios (réponse valide, service en erreur, reprise après échec
    /// transitoire, santé du service) sur le vrai FastApiClient de production,
    /// avec un HttpMessageHandler simulé : aucun appel réseau réel.
    /// </summary>
    public class FastApiClientTests
    {
        /// <summary>Handler HTTP scripté : renvoie les réponses dans l'ordre, puis la dernière.</summary>
        internal sealed class ScriptedHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
            private Func<HttpRequestMessage, HttpResponseMessage>? _last;
            public List<HttpRequestMessage> Requests { get; } = new();
            public List<string?> Bodies { get; } = new();

            public ScriptedHandler Then(HttpStatusCode status, string? json = null)
            {
                _responses.Enqueue(_ => new HttpResponseMessage(status)
                {
                    Content = new StringContent(json ?? "{}", System.Text.Encoding.UTF8, "application/json")
                });
                return this;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                Bodies.Add(request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
                if (_responses.Count > 0) _last = _responses.Dequeue();
                if (_last == null) throw new InvalidOperationException("Aucune réponse scriptée");
                return _last(request);
            }
        }

        internal static FastApiClient CreateClient(ScriptedHandler handler, IHttpContextAccessor? accessor = null, bool circuitBreaker = false)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AIService:BaseUrl"] = "http://fastapi.test",
                    ["AIService:TimeoutSeconds"] = "5",
                    ["AIService:EnableCircuitBreaker"] = circuitBreaker ? "true" : "false",
                })
                .Build();

            return new FastApiClient(
                new HttpClient(handler),
                new Mock<ILogger<FastApiClient>>().Object,
                config,
                new Mock<INtfyService>().Object,
                accessor);
        }

        [Fact]
        public async Task GenerateLearningPath_WithSnakeCaseResponse_ParsesPhases()
        {
            // Forme réelle de GET /api/learning-path/{user_id} (schemas.py).
            var handler = new ScriptedHandler().Then(HttpStatusCode.OK,
                "{\"success\":true,\"user_id\":7,\"learning_velocity\":1.5,\"total_duration_days\":42," +
                "\"estimated_end_date\":\"2026-11-10\",\"generated_at\":\"2026-09-28T10:00:00\"," +
                "\"phases\":[{\"phase\":1,\"name\":\"Fondations\",\"duration_days\":14,\"focus_areas\":[\"Algèbre\"]," +
                "\"difficulty\":\"facile\",\"target_completion\":30.0,\"actions\":[\"Réviser\"]}]," +
                "\"recommendations\":{\"daily_study_time\":\"45 min\",\"focus_areas\":[],\"growth_areas\":[]}}");
            var client = CreateClient(handler);

            var result = await client.GenerateLearningPathAsync(7);

            Assert.NotNull(result);
            Assert.Equal(7, result!.UserId);
            Assert.Equal(42, result.TotalDurationDays);
            var phase = Assert.Single(result.Phases);
            Assert.Equal(14, phase.DurationDays);
            Assert.Equal("Algèbre", Assert.Single(phase.FocusAreas));
            Assert.Equal("45 min", result.Recommendations!.DailyStudyTime);
            Assert.Equal("/api/learning-path/7", handler.Requests[0].RequestUri!.AbsolutePath);
        }

        [Fact]
        public async Task GenerateLearningPath_WhenPythonReturns404_ReturnsNullWithoutRetry()
        {
            var handler = new ScriptedHandler().Then(HttpStatusCode.NotFound, "{\"detail\":\"Données insuffisantes\"}");
            var client = CreateClient(handler);

            Assert.Null(await client.GenerateLearningPathAsync(7));
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task GetAsync_WithSnakeCaseOptions_BindsSnakeCaseFields()
        {
            var handler = new ScriptedHandler().Then(HttpStatusCode.OK, "{\"user_id\":3,\"weak_areas\":[\"Physique\"]}");
            var client = CreateClient(handler);

            var withDefault = await client.GetAsync<SnakeProbe>("/x");
            handler.Then(HttpStatusCode.OK, "{\"user_id\":3,\"weak_areas\":[\"Physique\"]}");
            var withSnake = await client.GetAsync<SnakeProbe>("/x", FastApiClient.SnakeCaseJson);

            // Défaut historique : insensible à la casse seulement, user_id ne se lie pas.
            Assert.Equal(0, withDefault!.UserId);
            Assert.Equal(3, withSnake!.UserId);
            Assert.Equal("Physique", Assert.Single(withSnake.WeakAreas));
        }

        public class SnakeProbe
        {
            public int UserId { get; set; }
            public List<string> WeakAreas { get; set; } = new();
        }

        [Fact]
        public async Task PostRawJson_ReturnsStatusAndBody_ForNonSuccess()
        {
            var handler = new ScriptedHandler().Then(HttpStatusCode.NotFound, "{\"detail\":\"Aucun enrollment\"}");
            var client = CreateClient(handler);

            var (status, body) = await client.PostRawJsonAsync("/api/analyze-progress?user_id=1", null);

            Assert.Equal(404, status);
            Assert.Contains("Aucun enrollment", body);
            Assert.Equal("user_id=1", handler.Requests[0].RequestUri!.Query.TrimStart('?'));
        }

        [Fact]
        public async Task PostAsync_WithDefinitiveError_IsNotRetried_AndReturnsNull()
        {
            // 404 est définitif : FastApiClient ne réessaie que sur les échecs transitoires.
            var handler = new ScriptedHandler().Then(HttpStatusCode.NotFound, "{\"detail\":\"Not Found\"}");
            var client = CreateClient(handler);

            var result = await client.PostAsync<object>("/api/does-not-exist", new { a = 1 });

            Assert.Null(result);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task PostAsync_WithTransientFailure_RetriesAndSucceeds()
        {
            // Premier appel en 500 (transitoire), le second réussit. Délai de
            // reprise réel de 2 s (politique de production, non paramétrable).
            var handler = new ScriptedHandler()
                .Then(HttpStatusCode.InternalServerError)
                .Then(HttpStatusCode.OK, "{\"value\":\"ok\"}");
            var client = CreateClient(handler);

            var result = await client.PostAsync<Dictionary<string, string>>("/api/anything", new { a = 1 });

            Assert.NotNull(result);
            Assert.Equal("ok", result!["value"]);
            Assert.Equal(2, handler.Requests.Count);
        }

        [Fact]
        public async Task Requests_RelayIncomingAuthorizationHeader()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["Authorization"] = "Bearer user-token";
            var accessor = new Mock<IHttpContextAccessor>();
            accessor.Setup(a => a.HttpContext).Returns(httpContext);

            var handler = new ScriptedHandler().Then(HttpStatusCode.OK, "{}");
            var client = CreateClient(handler, accessor.Object);

            await client.GetAsync<Dictionary<string, object>>("/api/anything");

            Assert.Equal("Bearer user-token", handler.Requests[0].Headers.Authorization?.ToString());
        }

        [Theory]
        [InlineData(HttpStatusCode.OK, true)]
        [InlineData(HttpStatusCode.ServiceUnavailable, false)]
        public async Task HealthCheck_ReflectsServiceStatus(HttpStatusCode status, bool expected)
        {
            var handler = new ScriptedHandler().Then(status, "{\"status\":\"healthy\"}");
            var client = CreateClient(handler);

            Assert.Equal(expected, await client.HealthCheckAsync());
        }
    }
}
