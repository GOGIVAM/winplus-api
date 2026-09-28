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
        public async Task GetRecommendations_WithValidResponse_ParsesBody()
        {
            var handler = new ScriptedHandler().Then(HttpStatusCode.OK,
                "{\"userId\":7,\"recommendations\":[{\"subjectId\":3,\"subjectName\":\"Maths\",\"matchScore\":0.9}]}");
            var client = CreateClient(handler);

            var result = await client.GetRecommendationsAsync(7, "beginner", "math");

            Assert.Equal(7, result.UserId);
            Assert.Single(result.Recommendations);
            Assert.Equal("Maths", result.Recommendations[0].SubjectName);
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
