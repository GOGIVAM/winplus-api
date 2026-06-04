using System.Net.Http;
using System.Text;
using System.Text.Json;
using Backend.Models.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Backend.Services;

/// <summary>
/// Client pour communiquer avec le service FastAPI (IA/Recommandations)
///  CORRIGÉ: Configuration dynamique + Circuit Breaker + Retry
/// </summary>
public interface IFlaskClient
{
    // Méthodes génériques
    Task<T?> GetAsync<T>(string endpoint) where T : class;
    Task<T?> PostAsync<T>(string endpoint, object data) where T : class;
    Task<bool> HealthCheckAsync();
    
    // Méthodes métier
    Task<RecommendationResponse> GetRecommendationsAsync(int userId, string preferenceLevel, string category);
    Task<ProgressAnalysisResponse> AnalyzeProgressAsync(int userId, int subjectId, string depth);
    Task<QuizGenerationResponse> GenerateQuizAsync(int userId, int subjectId, int questionCount, string difficulty);
    Task<PerformanceMetricsResponse> GetPerformanceAsync(int userId, string timePeriod);
    Task<LearningPathResponse> GenerateLearningPathAsync(int userId, string goalSubject, int weeks, int hoursPerWeek);
}

public class FlaskClient : IFlaskClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FlaskClient> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _baseUrl;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly IAsyncPolicy _circuitBreakerPolicy;

    public FlaskClient(
        HttpClient httpClient,
        ILogger<FlaskClient> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _configuration = configuration;

        //  Configuration dynamique depuis appsettings
        _baseUrl = _configuration["AIService:BaseUrl"] ?? "http://localhost:8000";
        var timeoutSeconds = _configuration.GetValue<int>("AIService:TimeoutSeconds", 60);

        _httpClient.BaseAddress = new Uri(_baseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        _logger.LogInformation("FlaskClient configuré avec BaseUrl: {BaseUrl}, Timeout: {Timeout}s", 
            _baseUrl, timeoutSeconds);

        //  Retry Policy: 3 tentatives avec backoff exponentiel
        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        exception,
                        "Tentative {RetryCount}/3 vers Flask API après {Delay}s",
                        retryCount,
                        timeSpan.TotalSeconds);
                });

        //  Circuit Breaker: s''ouvre après N échecs consécutifs
        var enableCircuitBreaker = _configuration.GetValue<bool>("AIService:EnableCircuitBreaker", true);
        
        if (enableCircuitBreaker)
        {
            var failureThreshold = _configuration.GetValue<int>("AIService:CircuitBreakerFailureThreshold", 5);
            var breakDuration = _configuration.GetValue<TimeSpan>("AIService:CircuitBreakerBreakDuration", TimeSpan.FromSeconds(30));

            _circuitBreakerPolicy = Policy
                .Handle<HttpRequestException>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: failureThreshold,
                    durationOfBreak: breakDuration,
                    onBreak: (exception, duration) =>
                    {
                        _logger.LogError(
                            exception,
                            " Circuit Breaker OUVERT pour Flask API. Durée: {Duration}s",
                            duration.TotalSeconds);
                    },
                    onReset: () =>
                    {
                        _logger.LogInformation(" Circuit Breaker FERMÉ. Flask API disponible");
                    });
        }
        else
        {
            // Circuit breaker désactivé: policy no-op
            _circuitBreakerPolicy = Policy.NoOpAsync();
        }
    }

    /// <summary>
    /// GET request avec retry + circuit breaker
    /// </summary>
    public async Task<T?> GetAsync<T>(string endpoint) where T : class
    {
        try
        {
            return await _circuitBreakerPolicy.ExecuteAsync(async () =>
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    _logger.LogDebug("GET {BaseUrl}{Endpoint}", _baseUrl, endpoint);

                    var response = await _httpClient.GetAsync(endpoint);
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    _logger.LogDebug(" GET {Endpoint} réussi", endpoint);
                    return result;
                }));
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, " Circuit ouvert: Flask API indisponible pour {Endpoint}", endpoint);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, " Erreur GET Flask API {Endpoint}", endpoint);
            return null;
        }
    }

    /// <summary>
    /// POST request avec retry + circuit breaker
    /// </summary>
    public async Task<T?> PostAsync<T>(string endpoint, object data) where T : class
    {
        try
        {
            return await _circuitBreakerPolicy.ExecuteAsync(async () =>
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    _logger.LogDebug("POST {BaseUrl}{Endpoint}", _baseUrl, endpoint);

                    var json = JsonSerializer.Serialize(data);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await _httpClient.PostAsync(endpoint, content);
                    response.EnsureSuccessStatusCode();

                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<T>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    _logger.LogDebug(" POST {Endpoint} réussi", endpoint);
                    return result;
                }));
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, " Circuit ouvert: Flask API indisponible pour {Endpoint}", endpoint);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, " Erreur POST Flask API {Endpoint}", endpoint);
            return null;
        }
    }

    /// <summary>
    /// Vérifier si Flask API est disponible
    /// </summary>
    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            _logger.LogDebug("Health check Flask API...");
            var response = await _httpClient.GetAsync("/health");
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(" Flask API healthy");
                return true;
            }
            
            _logger.LogWarning(" Flask API unhealthy: {StatusCode}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, " Flask API inaccessible");
            return false;
        }
    }

    #region Méthodes Métier

    /// <summary>
    /// Obtenir les recommandations de cours
    /// </summary>
    public async Task<RecommendationResponse> GetRecommendationsAsync(
        int userId,
        string preferenceLevel,
        string category)
    {
        try
        {
            _logger.LogInformation("Récupération des recommandations pour l'utilisateur {UserId}", userId);

            var request = new
            {
                user_id = userId,
                preference_level = preferenceLevel,
                category = category
            };

            var response = await PostAsync<RecommendationResponse>("/api/recommend", request);
            
            if (response == null)
            {
                _logger.LogWarning("Aucune recommandation retournée, utilisation fallback");
                return GetDefaultRecommendationResponse(userId);
            }

            _logger.LogInformation("✓ {Count} recommandations récupérées", response.Recommendations?.Count ?? 0);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des recommandations");
            return GetDefaultRecommendationResponse(userId);
        }
    }

    /// <summary>
    /// Analyser la progression de l'étudiant
    /// </summary>
    public async Task<ProgressAnalysisResponse> AnalyzeProgressAsync(
        int userId,
        int subjectId,
        string depth)
    {
        try
        {
            _logger.LogInformation("Analyse de la progression pour l'utilisateur {UserId}, sujet {SubjectId}", userId, subjectId);

            var request = new
            {
                user_id = userId,
                subject_id = subjectId,
                analysis_depth = depth
            };

            var response = await PostAsync<ProgressAnalysisResponse>("/api/analyze-progress", request);
            
            if (response == null)
            {
                _logger.LogWarning("Analyse non retournée, utilisation fallback");
                return GetDefaultProgressAnalysis(userId, subjectId);
            }

            _logger.LogInformation("✓ Analyse complétée pour l'utilisateur {UserId}", userId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'analyse de la progression");
            return GetDefaultProgressAnalysis(userId, subjectId);
        }
    }

    /// <summary>
    /// Générer un quiz personnalisé
    /// </summary>
    public async Task<QuizGenerationResponse> GenerateQuizAsync(
        int userId,
        int subjectId,
        int questionCount,
        string difficulty)
    {
        try
        {
            _logger.LogInformation("Génération de quiz pour l'utilisateur {UserId}, sujet {SubjectId}", userId, subjectId);

            var request = new
            {
                user_id = userId,
                subject_id = subjectId,
                number_of_questions = questionCount,
                difficulty = difficulty
            };

            var response = await PostAsync<QuizGenerationResponse>("/api/generate-quiz", request);
            
            if (response == null)
            {
                _logger.LogWarning("Quiz non généré, utilisation fallback");
                return GetDefaultQuizResponse(userId, subjectId);
            }

            _logger.LogInformation("✓ Quiz généré avec {Count} questions", response.Questions?.Count ?? 0);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la génération du quiz");
            return GetDefaultQuizResponse(userId, subjectId);
        }
    }

    /// <summary>
    /// Obtenir les métriques de performance
    /// </summary>
    public async Task<PerformanceMetricsResponse> GetPerformanceAsync(
        int userId,
        string timePeriod)
    {
        try
        {
            _logger.LogInformation("Récupération des métriques de performance pour l'utilisateur {UserId}", userId);

            var endpoint = $"/api/get-performance?user_id={userId}&time_period={timePeriod}";
            var response = await GetAsync<PerformanceMetricsResponse>(endpoint);
            
            if (response == null)
            {
                _logger.LogWarning("Métriques non retournées, utilisation fallback");
                return GetDefaultPerformanceMetrics(userId);
            }

            _logger.LogInformation("✓ Métriques récupérées pour l'utilisateur {UserId}", userId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des métriques");
            return GetDefaultPerformanceMetrics(userId);
        }
    }

    /// <summary>
    /// Générer un parcours d'apprentissage personnalisé
    /// </summary>
    public async Task<LearningPathResponse> GenerateLearningPathAsync(
        int userId,
        string goalSubject,
        int weeks,
        int hoursPerWeek)
    {
        try
        {
            _logger.LogInformation("Génération du parcours d'apprentissage pour l'utilisateur {UserId}", userId);

            var request = new
            {
                user_id = userId,
                goal_subject = goalSubject,
                timeframe_weeks = weeks,
                available_hours_per_week = hoursPerWeek
            };

            var response = await PostAsync<LearningPathResponse>("/api/generate-learning-path", request);
            
            if (response == null)
            {
                _logger.LogWarning("Parcours non généré, utilisation fallback");
                return GetDefaultLearningPath(userId, goalSubject, weeks);
            }

            _logger.LogInformation("✓ Parcours généré avec {Count} semaines", response.Weeks?.Count ?? 0);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la génération du parcours");
            return GetDefaultLearningPath(userId, goalSubject, weeks);
        }
    }

    #endregion

    #region Méthodes Fallback (par défaut)

    private RecommendationResponse GetDefaultRecommendationResponse(int userId)
    {
        return new RecommendationResponse
        {
            UserId = userId,
            Recommendations = new List<RecommendationItem>(),
            GeneratedAt = DateTime.UtcNow
        };
    }

    private ProgressAnalysisResponse GetDefaultProgressAnalysis(int userId, int subjectId)
    {
        return new ProgressAnalysisResponse
        {
            UserId = userId,
            SubjectId = subjectId,
            CompletionPercentage = 0,
            ProgressTrend = "unknown",
            EstimatedCompletionDate = DateTime.UtcNow.AddMonths(3),
            WeakAreas = new List<string>(),
            Strengths = new List<string>(),
            Recommendations = new List<string> { "Veuillez réessayer plus tard" }
        };
    }

    private QuizGenerationResponse GetDefaultQuizResponse(int userId, int subjectId)
    {
        return new QuizGenerationResponse
        {
            QuizId = 0,
            UserId = userId,
            SubjectId = subjectId,
            Questions = new List<QuizQuestion>(),
            EstimatedDurationMinutes = 0,
            CreatedAt = DateTime.UtcNow
        };
    }

    private PerformanceMetricsResponse GetDefaultPerformanceMetrics(int userId)
    {
        return new PerformanceMetricsResponse
        {
            UserId = userId,
            PerformanceScore = 0,
            LearningRate = 0,
            CompletionRate = 0,
            EngagementScore = 0,
            CompareToAverage = new ClassComparison
            {
                YourScore = 0,
                ClassAverage = 0,
                Percentile = 0
            },
            CalculatedAt = DateTime.UtcNow
        };
    }

    private LearningPathResponse GetDefaultLearningPath(int userId, string goalSubject, int weeks)
    {
        return new LearningPathResponse
        {
            UserId = userId,
            PathId = 0,
            GoalSubject = goalSubject,
            Weeks = new List<LearningPathWeek>(),
            CompletionEstimate = DateTime.UtcNow.AddDays(weeks * 7),
            CreatedAt = DateTime.UtcNow
        };
    }

    #endregion
}
