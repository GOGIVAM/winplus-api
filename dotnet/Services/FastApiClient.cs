using System.Net.Http;
using System.Text;
using System.Text.Json;
using Backend.Models.DTOs;
using Microsoft.AspNetCore.Http;
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
public interface IFastApiClient
{
    // Méthodes génériques
    Task<T?> GetAsync<T>(string endpoint) where T : class;
    Task<T?> PostAsync<T>(string endpoint, object data) where T : class;

    /// <summary>
    /// GET brut : renvoie le code HTTP et le corps JSON tels quels, sans
    /// désérialiser dans un DTO C# ni transformer les échecs en 500. FastAPI
    /// répond parfois un 404 légitime avec un message utile (ex: "pas assez
    /// de données pour ce parcours") — l'écraser perdrait ce message.
    /// Voir GetLearningPath dans AIController.
    /// </summary>
    Task<(int StatusCode, string? Body)> GetRawJsonAsync(string endpoint);
    Task<bool> HealthCheckAsync();
    
    // Méthodes métier
    Task<RecommendationResponse> GetRecommendationsAsync(int userId, string preferenceLevel, string category);
    Task<ProgressAnalysisResponse> AnalyzeProgressAsync(int userId, int subjectId, string depth);
    Task<QuizGenerationResponse> GenerateQuizAsync(int userId, int subjectId, int questionCount, string difficulty);
    Task<PerformanceMetricsResponse> GetPerformanceAsync(int userId, string timePeriod);
    Task<LearningPathResponse> GenerateLearningPathAsync(int userId, string goalSubject, int weeks, int hoursPerWeek);

    /// <summary>
    /// Génère les questions du "mode évaluation" à partir du contenu réel du
    /// PDF de l'épreuve (extraction de texte + LLM côté Python). Renvoie
    /// null si le service est indisponible ou si le PDF n'a pas pu être lu.
    /// </summary>
    Task<List<QuizQuestionDto>?> GenerateExamQuizAsync(int examId, string documentUrl, string title, string? category);
}

public class FastApiClient : IFastApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FastApiClient> _logger;
    private readonly IConfiguration _configuration;
    private readonly INtfyService _ntfy;

    /// <summary>
    /// Sert à relayer le jeton de l'utilisateur courant vers FastAPI, dont
    /// tous les endpoints IA sont protégés par Depends(verify_token).
    /// Sans ce relais, chaque appel recevait 401.
    /// </summary>
    private readonly IHttpContextAccessor? _httpContextAccessor;

    private readonly string _baseUrl;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly IAsyncPolicy _circuitBreakerPolicy;

    public FastApiClient(
        HttpClient httpClient,
        ILogger<FastApiClient> logger,
        IConfiguration configuration,
        INtfyService ntfy,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _ntfy = ntfy;
        _httpClient = httpClient;
        _logger = logger;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;

        //  Configuration dynamique depuis appsettings
        _baseUrl = _configuration["AIService:BaseUrl"] ?? "http://localhost:8000";
        var timeoutSeconds = _configuration.GetValue<int>("AIService:TimeoutSeconds", 60);

        _httpClient.BaseAddress = new Uri(_baseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        _logger.LogInformation("FastApiClient configuré avec BaseUrl: {BaseUrl}, Timeout: {Timeout}s", 
            _baseUrl, timeoutSeconds);

        //  Retry Policy: 3 tentatives avec backoff exponentiel
        // ⚠ On ne réessaie que sur les échecs transitoires. Auparavant, toute
        // HttpRequestException relançait 3 tentatives à 2, 4 puis 8 secondes 
        // y compris sur 401 et 404, qui sont définitifs. Un endpoint absent
        // coûtait ainsi 14 secondes par appel, répétées à chaque chargement.
        static bool IsTransient(HttpRequestException ex) =>
            ex.StatusCode is null                                  // panne réseau
            or System.Net.HttpStatusCode.RequestTimeout            // 408
            or System.Net.HttpStatusCode.TooManyRequests           // 429
            or System.Net.HttpStatusCode.InternalServerError       // 500
            or System.Net.HttpStatusCode.BadGateway                // 502
            or System.Net.HttpStatusCode.ServiceUnavailable        // 503
            or System.Net.HttpStatusCode.GatewayTimeout;           // 504

        _retryPolicy = Policy
            .Handle<HttpRequestException>(IsTransient)
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        exception,
                        "Tentative {RetryCount}/3 vers FastApi API après {Delay}s",
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
                .Handle<HttpRequestException>(IsTransient)
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: failureThreshold,
                    durationOfBreak: breakDuration,
                    onBreak: (exception, duration) =>
                    {
                        _logger.LogError(
                            exception,
                            " Circuit Breaker OUVERT pour FastApi API. Durée: {Duration}s",
                            duration.TotalSeconds);
                        _ = _ntfy.PublishAdminAsync(
                            "Service IA indisponible",
                            $"Le circuit-breaker s'est ouvert. Le service IA sera indisponible pendant {duration.TotalSeconds}s.",
                            "urgent",
                            new[] { "rotating_light", "robot_face" });
                    },
                    onReset: () =>
                    {
                        _logger.LogInformation(" Circuit Breaker FERMÉ. FastApi API disponible");
                    });
        }
        else
        {
            // Circuit breaker désactivé: policy no-op
            _circuitBreakerPolicy = Policy.NoOpAsync();
        }
    }

    /// <summary>
    /// Recopie l'en-tête Authorization de la requête entrante sur la requête
    /// sortante. Les endpoints IA de FastAPI exigent un Bearer valide ; le
    /// client ne l'envoyait pas, d'où des 401 systématiques.
    /// </summary>
    private void AttachAuthorization(HttpRequestMessage request)
    {
        var incoming = _httpContextAccessor?.HttpContext?.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrWhiteSpace(incoming))
            request.Headers.TryAddWithoutValidation("Authorization", incoming);
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

                    using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                    AttachAuthorization(request);

                    var response = await _httpClient.SendAsync(request);
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
            _logger.LogError(ex, " Circuit ouvert: FastApi API indisponible pour {Endpoint}", endpoint);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, " Erreur GET FastApi API {Endpoint}", endpoint);
            return null;
        }
    }

    /// <summary>
    /// GET brut, sans désérialisation typée  voir IFastApiClient.GetRawJsonAsync.
    /// Pas de EnsureSuccessStatusCode() ici : un 4xx de FastAPI (ex: 404 « pas
    /// encore de données ») est une réponse valide à relayer, pas une panne à
    /// masquer derrière un 500. Seule une vraie erreur réseau/circuit ouvert
    /// tombe dans les catch ci-dessous.
    /// </summary>
    public async Task<(int StatusCode, string? Body)> GetRawJsonAsync(string endpoint)
    {
        try
        {
            return await _circuitBreakerPolicy.ExecuteAsync(async () =>
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                    AttachAuthorization(request);

                    var response = await _httpClient.SendAsync(request);
                    var body = await response.Content.ReadAsStringAsync();
                    return ((int)response.StatusCode, (string?)body);
                }));
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, " Circuit ouvert: FastApi API indisponible pour {Endpoint}", endpoint);
            return (503, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, " Erreur GET brut FastApi API {Endpoint}", endpoint);
            return (502, null);
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

                    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };
                    AttachAuthorization(request);

                    var response = await _httpClient.SendAsync(request);
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
            _logger.LogError(ex, " Circuit ouvert: FastApi API indisponible pour {Endpoint}", endpoint);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, " Erreur POST FastApi API {Endpoint}", endpoint);
            return null;
        }
    }

    /// <summary>
    /// Vérifier si FastApi API est disponible
    /// </summary>
    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            _logger.LogDebug("Health check FastApi API...");
            var response = await _httpClient.GetAsync("/health");
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(" FastApi API healthy");
                return true;
            }
            
            _logger.LogWarning(" FastApi API unhealthy: {StatusCode}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, " FastApi API inaccessible");
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

            // ⚠ Route corrigée : le POST /api/generate-learning-path n'existe
            // pas côté FastAPI, qui expose GET /api/learning-path/{user_id}
            // (app.py). Chaque appel enchaînait 3 tentatives à 2, 4 puis 8
            // secondes avant de retomber sur le fallback  14 secondes perdues,
            // répétées à chaque chargement du tableau de bord.
            //
            // Le service Python calcule le parcours depuis les performances
            // réelles en base : il n'attend ni matière ni volume horaire, ces
            // paramètres ne sont donc plus transmis.
            var response = await GetAsync<LearningPathResponse>($"/api/learning-path/{userId}");
            
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
