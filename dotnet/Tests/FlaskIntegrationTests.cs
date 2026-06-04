using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Backend.Models.DTOs;
using Backend.Services;
using Backend.Utilities;

namespace AITests;

/// <summary>
/// Tests d'intégration pour la communication entre .NET et Flask
/// Teste les scénarios:
/// - Communication réussie avec Flask
/// - Gestion des erreurs quand Flask est down
/// - Retry logic avec exponential backoff
/// - Circuit breaker pour éviter les cascading failures
/// </summary>
public class FlaskIntegrationTests : IAsyncLifetime
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<FlaskClient>> _loggerMock;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly RetryPolicy _retryPolicy;
    private FlaskClient _fastapiClient;
    private string _testFlaskUrl;

    public FlaskIntegrationTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<FlaskClient>>();
        _testFlaskUrl = "http://localhost:5000";
        
        _circuitBreaker = new CircuitBreaker(
            failureThreshold: 3,
            timeout: TimeSpan.FromSeconds(30),
            resetTimeout: TimeSpan.FromSeconds(60));
        
        _retryPolicy = new RetryPolicy(
            maxRetries: 3,
            initialDelay: TimeSpan.FromMilliseconds(50),
            backoffMultiplier: 2.0);
    }

    public async Task InitializeAsync()
    {
        // Setup
        var httpClientHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(httpClientHandler.Object)
        {
            BaseAddress = new Uri(_testFlaskUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };

        _httpClientFactoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        _fastapiClient = new FlaskClient(_httpClientFactoryMock.Object, _loggerMock.Object, _circuitBreaker, _retryPolicy);
    }

    public Task DisposeAsync()
    {
        _fastapiClient?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Test 1: Communication réussie avec Flask
    /// Scénario: Flask répond avec 200 OK et données valides
    /// Résultat attendu: Les données sont correctement parsées et retournées
    /// </summary>
    [Fact]
    public async Task AnalyzeContent_WithValidResponse_ReturnsSuccessResult()
    {
        // Arrange
        var content = "Analyze this educational content";
        var fastapiResponse = new
        {
            success = true,
            analysis = new
            {
                difficulty = "intermediate",
                topics = new[] { "mathematics", "algebra" },
                summary = "Content summary",
                recommendations = new[] { "Study prerequisites" }
            }
        };

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(fastapiResponse))
            });

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri(_testFlaskUrl) };
        _httpClientFactoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var fastapiClient = new FlaskClient(_httpClientFactoryMock.Object, _loggerMock.Object, _circuitBreaker, _retryPolicy);

        // Act
        var result = await fastapiClient.AnalyzeContentAsync(content);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("intermediate", result.Analysis?.Difficulty);
        Assert.Contains("mathematics", result.Analysis?.Topics ?? new string[] { });
        Assert.Equal(CircuitState.Closed, _circuitBreaker.State);
    }

    /// <summary>
    /// Test 2: Gestion des erreurs quand Flask est down (503 Service Unavailable)
    /// Scénario: Flask retourne 503
    /// Résultat attendu: CircuitBreaker passe à Open après 3 échecs
    /// </summary>
    [Fact]
    public async Task AnalyzeContent_WithFlaskDown_OpensCircuitBreaker()
    {
        // Arrange
        var content = "Test content";
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.ServiceUnavailable
            });

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri(_testFlaskUrl) };
        _httpClientFactoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var circuitBreaker = new CircuitBreaker(failureThreshold: 2);
        var fastapiClient = new FlaskClient(_httpClientFactoryMock.Object, _loggerMock.Object, circuitBreaker, _retryPolicy);

        // Act & Assert - Envoyer 2 requêtes qui échouent
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await fastapiClient.AnalyzeContentAsync(content);
            }
            catch (HttpRequestException)
            {
                // Expected
            }
        }

        // Le circuit breaker doit être ouvert après 2 échecs
        Assert.Equal(CircuitState.Open, circuitBreaker.State);

        // La 3ème requête ne devrait pas être envoyée
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fastapiClient.AnalyzeContentAsync(content));
        Assert.Contains("Circuit breaker is open", ex.Message);
    }

    /// <summary>
    /// Test 3: Retry logic avec exponential backoff
    /// Scénario: Flask échoue une fois, puis réussit
    /// Résultat attendu: La requête est renvoyée automatiquement et réussit
    /// </summary>
    [Fact]
    public async Task AnalyzeContent_WithTemporaryFailure_RetriesAndSucceeds()
    {
        // Arrange
        var content = "Test content";
        var successResponse = new { success = true, analysis = new { difficulty = "easy" } };
        
        var mockHandler = new Mock<HttpMessageHandler>();
        var callCount = 0;
        
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, ct) =>
            {
                callCount++;
                // Première requête échoue, les suivantes réussissent
                if (callCount == 1)
                {
                    return Task.FromResult(new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.InternalServerError
                    });
                }
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(successResponse))
                });
            });

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri(_testFlaskUrl) };
        _httpClientFactoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var retryPolicy = new RetryPolicy(maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(10));
        var fastapiClient = new FlaskClient(_httpClientFactoryMock.Object, _loggerMock.Object, _circuitBreaker, retryPolicy);

        // Act
        var result = await fastapiClient.AnalyzeContentAsync(content);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.True(callCount > 1, "Should have retried at least once");
    }

    /// <summary>
    /// Test 4: Circuit breaker - transition de Open à HalfOpen
    /// Scénario: Circuit est ouvert, reset timeout s'écoule, circuit passe à HalfOpen
    /// Résultat attendu: Une requête test peut être envoyée
    /// </summary>
    [Fact]
    public async Task CircuitBreaker_TransitionsFromOpenToHalfOpen_AfterResetTimeout()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(
            failureThreshold: 1,
            resetTimeout: TimeSpan.FromMilliseconds(100));

        // Ouvrir le circuit
        circuitBreaker.RecordFailure();
        Assert.Equal(CircuitState.Open, circuitBreaker.State);

        // Act - Attendre le reset timeout
        await Task.Delay(150);
        bool canAttemptReset = circuitBreaker.CanAttemptReset();

        // Assert
        Assert.True(canAttemptReset);
        Assert.Equal(CircuitState.HalfOpen, circuitBreaker.State);
    }

    /// <summary>
    /// Test 5: Response parsing - Vérifier que les réponses Flask sont correctement parsées
    /// Scénario: Flask retourne une réponse JSON complexe
    /// Résultat attendu: Les données sont correctement désérialisées
    /// </summary>
    [Fact]
    public async Task AnalyzeContent_WithComplexResponse_ParsesCorrectly()
    {
        // Arrange
        var fastapiResponse = new
        {
            success = true,
            analysis = new
            {
                difficulty = "advanced",
                topics = new[] { "calculus", "differential-equations" },
                summary = "Complex analysis content",
                recommendations = new[] { 
                    "Study limits and derivatives", 
                    "Practice integration techniques" 
                },
                estimatedTime = "2 hours",
                prerequisites = new[] { "algebra", "trigonometry" }
            }
        };

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(fastapiResponse))
            });

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri(_testFlaskUrl) };
        _httpClientFactoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var fastapiClient = new FlaskClient(_httpClientFactoryMock.Object, _loggerMock.Object, _circuitBreaker, _retryPolicy);

        // Act
        var result = await fastapiClient.AnalyzeContentAsync("Complex content");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("advanced", result.Analysis?.Difficulty);
        Assert.Equal(2, result.Analysis?.Topics?.Count ?? 0);
        Assert.Contains("calculus", result.Analysis?.Topics ?? new List<string>());
        Assert.True(result.Analysis?.Recommendations?.Count > 1);
    }

    /// <summary>
    /// Test 6: Health check - Vérifier la disponibilité de Flask
    /// Scénario: Appel du endpoint /health de Flask
    /// Résultat attendu: Le statut est reçu correctement
    /// </summary>
    [Fact]
    public async Task HealthCheck_WithFlaskAvailable_ReturnsHealthyStatus()
    {
        // Arrange
        var healthResponse = new { status = "healthy", service = "fastapi", timestamp = DateTime.UtcNow };
        
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(healthResponse))
            });

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri(_testFlaskUrl) };
        _httpClientFactoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var fastapiClient = new FlaskClient(_httpClientFactoryMock.Object, _loggerMock.Object, _circuitBreaker, _retryPolicy);

        // Act
        var result = await fastapiClient.HealthCheckAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("healthy", result.Status);
    }

    /// <summary>
    /// Test 7: Cascading failure prevention - Circuit breaker empêche les surcharges
    /// Scénario: Plusieurs clients appellent un service Flask down
    /// Résultat attendu: Circuit breaker rejette les appels rapidement sans surcharger Flask
    /// </summary>
    [Fact]
    public async Task CircuitBreaker_PreventsCascadingFailures_WhenFlaskDown()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        var requestCount = 0;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, ct) =>
            {
                requestCount++;
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.ServiceUnavailable
                });
            });

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri(_testFlaskUrl) };
        _httpClientFactoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var circuitBreaker = new CircuitBreaker(failureThreshold: 3);
        var retryPolicy = new RetryPolicy(maxRetries: 1); // Retry minimal
        var fastapiClient = new FlaskClient(_httpClientFactoryMock.Object, _loggerMock.Object, circuitBreaker, retryPolicy);

        // Act - Essayer 10 requêtes
        for (int i = 0; i < 10; i++)
        {
            try
            {
                await fastapiClient.AnalyzeContentAsync("test");
            }
            catch
            {
                // Expected
            }
        }

        // Assert - Après ouverture du circuit, les requêtes ne doivent pas être envoyées
        // requestCount devrait être beaucoup plus petit que 10*2 (retries)
        Assert.Equal(CircuitState.Open, circuitBreaker.State);
        Assert.True(requestCount < 20, $"Too many requests sent ({requestCount}). Circuit breaker should prevent cascading failures.");
    }
}

/// <summary>
/// Mock FlaskClient pour les tests
/// </summary>
public class FlaskClient : IFlaskClient, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FlaskClient> _logger;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly RetryPolicy _retryPolicy;
    private HttpClient _httpClient;

    public FlaskClient(
        IHttpClientFactory httpClientFactory,
        ILogger<FlaskClient> logger,
        CircuitBreaker? circuitBreaker = null,
        RetryPolicy? retryPolicy = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _circuitBreaker = circuitBreaker ?? new CircuitBreaker();
        _retryPolicy = retryPolicy ?? new RetryPolicy();
        _httpClient = _httpClientFactory.CreateClient("fastapi");
    }

    public async Task<FlaskAnalysisResponse> AnalyzeContentAsync(string content)
    {
        if (!_circuitBreaker.IsCallAllowed())
        {
            _logger.LogError("Circuit breaker is open, rejecting request");
            throw new InvalidOperationException("Circuit breaker is open, rejecting request");
        }

        try
        {
            var result = await _retryPolicy.ExecuteAsync(async () =>
            {
                var response = await _httpClient.PostAsJsonAsync("/api/analyze", new { content });
                
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Flask returned {response.StatusCode}");
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                return System.Text.Json.JsonSerializer.Deserialize<FlaskAnalysisResponse>(
                    jsonString,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new FlaskAnalysisResponse { Success = false };
            }, "AnalyzeContent");

            _circuitBreaker.RecordSuccess();
            return result;
        }
        catch (Exception ex)
        {
            _circuitBreaker.RecordFailure();
            _logger.LogError(ex, "Flask request failed");
            throw;
        }
    }

    public async Task<FlaskHealthResponse> HealthCheckAsync()
    {
        var response = await _httpClient.GetAsync("/health");
        var jsonString = await response.Content.ReadAsStringAsync();
        return System.Text.Json.JsonSerializer.Deserialize<FlaskHealthResponse>(
            jsonString,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new FlaskHealthResponse { Status = "unknown" };
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

/// <summary>
/// Réponse du service Flask pour l'analyse de contenu
/// </summary>
public class FlaskAnalysisResponse
{
    public bool Success { get; set; }
    public FlaskAnalysis? Analysis { get; set; }
    public string? Error { get; set; }
}

public class FlaskAnalysis
{
    public string? Difficulty { get; set; }
    public List<string>? Topics { get; set; }
    public string? Summary { get; set; }
    public List<string>? Recommendations { get; set; }
    public string? EstimatedTime { get; set; }
    public List<string>? Prerequisites { get; set; }
}

/// <summary>
/// Réponse du health check Flask
/// </summary>
public class FlaskHealthResponse
{
    public string? Status { get; set; }
    public string? Service { get; set; }
    public DateTime Timestamp { get; set; }
}
