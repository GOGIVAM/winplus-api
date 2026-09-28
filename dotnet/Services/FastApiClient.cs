using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    // Module 23 : `jsonOptions` choisit explicitement la convention de nommage
    // de la réponse Python. FastAPI n'est pas homogène : certaines routes
    // répondent en snake_case (user_id, weak_areas…), d'autres en camelCase
    // (correctAnswer des quiz). Par défaut : insensible à la casse seulement,
    // comportement historique. Pour une route snake_case, passer
    // FastApiClient.SnakeCaseJson (patron de ChatbotService).
    Task<T?> GetAsync<T>(string endpoint, JsonSerializerOptions? jsonOptions = null) where T : class;
    Task<T?> PostAsync<T>(string endpoint, object data, JsonSerializerOptions? jsonOptions = null) where T : class;

    /// <summary>
    /// POST brut : code HTTP et corps tels quels (pas d'EnsureSuccessStatusCode).
    /// Sert aux proxys qui relaient la réponse Python sans la retyper
    /// (voir AIController : recommend, analyze-progress).
    /// </summary>
    Task<(int StatusCode, string? Body)> PostRawJsonAsync(string endpoint, object? data);

    /// <summary>
    /// GET brut : renvoie le code HTTP et le corps JSON tels quels, sans
    /// désérialiser dans un DTO C# ni transformer les échecs en 500. FastAPI
    /// répond parfois un 404 légitime avec un message utile (ex: "pas assez
    /// de données pour ce parcours")  l'écraser perdrait ce message.
    /// Voir GetLearningPath dans AIController.
    /// </summary>
    Task<(int StatusCode, string? Body)> GetRawJsonAsync(string endpoint);
    Task<bool> HealthCheckAsync();
    
    // Méthodes métier
    // Module 23 : GetRecommendationsAsync, AnalyzeProgressAsync et
    // GetPerformanceAsync ont été retirées. Les deux premières envoyaient en
    // corps ce que Python attend en paramètre d'URL (422 permanent masqué par
    // un repli vide) et désérialisaient dans des objets sans correspondance
    // avec la réponse Python ; AIController relaie désormais la réponse brute.
    // La troisième appelait /api/get-performance, route inexistante côté Python.

    /// <summary>
    /// Parcours personnalisé calculé par Python depuis les performances réelles
    /// (GET /api/learning-path/{userId}). Renvoie null si Python refuse
    /// (404 « données insuffisantes ») ou est indisponible : plus de repli
    /// silencieux vers un parcours vide présenté comme un succès.
    /// </summary>
    Task<LearningPathResponse?> GenerateLearningPathAsync(int userId);

    /// <summary>
    /// Génère les questions du "mode évaluation" à partir du contenu réel du
    /// PDF de l'épreuve (extraction de texte + LLM côté Python). Renvoie
    /// null si le service est indisponible ou si le PDF n'a pas pu être lu.
    /// </summary>
    Task<(List<QuizQuestionDto>? Questions, string? ErrorDetail)> GenerateExamQuizAsync(int examId, string documentUrl, string title, string? category);

    /// <summary>
    /// Génère un quiz d'entraînement (QCM). Si <paramref name="subject"/> est
    /// nul, DeepSeek choisit lui-même la matière à partir du niveau scolaire
    /// et du contexte fourni (objectifs actifs)  voir QuizService.GenerateAIQuizAsync.
    /// </summary>
    Task<SubjectQuizGenerationResult?> GenerateSubjectQuizAsync(int userId, string? subject, string? topic, string? level, string? contextHint, string? difficulty = null, List<string>? recentQuestionTexts = null);

    /// <summary>
    /// Génère le contenu d'une fiche de révision personnalisée (erreurs de
    /// quiz récentes, épreuves téléchargées, objectifs actifs de l'élève).
    /// </summary>
    Task<GeneratedRevisionContentDto?> GenerateRevisionContentAsync(int userId, string? subject, string? topic, string? level = null, string? contextHint = null, string? difficulty = null);

    /// <summary>
    /// Déclenche l'ingestion RAG d'un document/vidéo fraîchement rattaché à
    /// une fiche (épreuve, contenu de cours, leçon). Fire-and-forget : ne
    /// doit jamais ralentir ni faire échouer l'enregistrement de la fiche
    /// elle-même. POST /api/rag/ingest répond "queued" immédiatement côté
    /// Python, le traitement réel (OCR, transcription, embedding) tourne
    /// en tâche de fond là-bas  voir RAG/router.py.
    /// `authorizationHeader` est capturé sur le thread de la requête
    /// d'origine (ex: Request.Headers["Authorization"]) : le HttpContext
    /// n'est plus fiable une fois la réponse HTTP renvoyée au client, donc
    /// le jeton doit être extrait AVANT d'appeler cette méthode, pas relu
    /// depuis IHttpContextAccessor pendant la tâche de fond.
    /// </summary>
    void QueueRagIngestion(
        string docId,
        string title,
        string fileUrl,
        string? authorizationHeader,
        string? category = null,
        int? subjectId = null,
        int? courseId = null,
        int? lessonId = null);
}

public class FastApiClient : IFastApiClient
{
    /// <summary>Comportement historique : insensible à la casse, sans politique de nommage.</summary>
    public static readonly JsonSerializerOptions DefaultJson = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Routes Python en snake_case (Pydantic sans alias). Même politique que
    /// ChatbotService._fastApiJsonOptions, le patron de référence : sans elle,
    /// « user_id » ne se lie jamais à UserId et la valeur par défaut remplace
    /// silencieusement la donnée réelle.
    /// </summary>
    public static readonly JsonSerializerOptions SnakeCaseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

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
    public async Task<T?> GetAsync<T>(string endpoint, JsonSerializerOptions? jsonOptions = null) where T : class
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
                    var result = JsonSerializer.Deserialize<T>(content, jsonOptions ?? DefaultJson);

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
    /// POST brut : renvoie le code HTTP et le corps tels quels, sans passer
    /// par EnsureSuccessStatusCode(). PostAsync&lt;T&gt; jette et avale le corps
    /// sur tout code non-2xx, donc un message d'erreur utile côté Python
    /// (ex: "PDF scanné, contenu illisible") n'atteignait jamais l'appelant
    /// ni les logs  seul un null générique remontait.
    /// </summary>
    public async Task<(int StatusCode, string? Body)> PostRawJsonAsync(string endpoint, object? data)
    {
        try
        {
            return await _circuitBreakerPolicy.ExecuteAsync(async () =>
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(data ?? new { }), Encoding.UTF8, "application/json")
                    };
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
            _logger.LogError(ex, " Erreur POST brut FastApi API {Endpoint}", endpoint);
            return (502, null);
        }
    }

    /// <summary>
    /// POST request avec retry + circuit breaker
    /// </summary>
    public async Task<T?> PostAsync<T>(string endpoint, object data, JsonSerializerOptions? jsonOptions = null) where T : class
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
                    var result = JsonSerializer.Deserialize<T>(responseContent, jsonOptions ?? DefaultJson);

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

    private class ExamQuizGenerationResult
    {
        public bool Success { get; set; }
        public ExamQuizData? Data { get; set; }
        public string? Error { get; set; }
    }

    private class ExamQuizData
    {
        public List<QuizQuestionDto>? Questions { get; set; }
        /// <summary>Renseigné uniquement par /api/quizzes/generate-content quand la matière est choisie par l'IA.</summary>
        public string? Subject { get; set; }
        /// <summary>Palier réellement appliqué ("easy"/"medium"/"hard"), renvoyé par /api/quizzes/generate-content.</summary>
        public string? Difficulty { get; set; }
    }

    /// <summary>
    /// Génère les questions du "mode évaluation" à partir du texte réel
    /// extrait du PDF de l'épreuve (voir python/routes/exam_quiz_routes.py).
    /// Le message d'erreur est celui renvoyé par Python (detail du
    /// HTTPException) quand disponible, pour distinguer un cas légitime
    /// ("PDF scanné") d'une vraie panne (dépendance manquante, S3, etc.) au
    /// lieu d'un message générique identique dans tous les cas.
    /// </summary>
    public async Task<(List<QuizQuestionDto>? Questions, string? ErrorDetail)> GenerateExamQuizAsync(int examId, string documentUrl, string title, string? category)
    {
        var request = new
        {
            exam_id = examId,
            document_url = documentUrl,
            title,
            category,
        };

        var (statusCode, body) = await PostRawJsonAsync("/api/exam-quiz/generate", request);

        if (statusCode is >= 200 and < 300 && body != null)
        {
            try
            {
                var result = JsonSerializer.Deserialize<ExamQuizGenerationResult>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var questions = result?.Data?.Questions;
                if (questions is { Count: > 0 })
                {
                    _logger.LogInformation("Quiz d'évaluation généré pour l'épreuve {ExamId} : {Count} questions", examId, questions.Count);
                    return (questions, null);
                }
                return (null, "Le service IA n'a renvoyé aucune question.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Réponse illisible du service IA pour l'épreuve {ExamId} : {Body}", examId, body);
                return (null, "Réponse inattendue du service de génération.");
            }
        }

        // FastAPI encapsule le message d'une HTTPException dans { "detail": "..." }.
        string? detail = null;
        if (body != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("detail", out var detailEl))
                    detail = detailEl.GetString();
            }
            catch { /* corps non-JSON (502/503 générés par PostRawJsonAsync) */ }
        }

        _logger.LogWarning("Génération du quiz d'évaluation échouée pour l'épreuve {ExamId} : HTTP {StatusCode}  {Detail}", examId, statusCode, detail ?? body);
        return (null, detail);
    }

    public async Task<SubjectQuizGenerationResult?> GenerateSubjectQuizAsync(int userId, string? subject, string? topic, string? level, string? contextHint, string? difficulty = null, List<string>? recentQuestionTexts = null)
    {
        try
        {
            _logger.LogInformation("Génération d'un quiz d'entraînement IA pour l'utilisateur {UserId} en {Subject}", userId, subject ?? "(matière à choisir par l'IA)");

            var request = new { user_id = userId, subject, topic, level, context_hint = contextHint, difficulty, recent_question_texts = recentQuestionTexts ?? new List<string>() };
            var result = await PostAsync<ExamQuizGenerationResult>("/api/quizzes/generate-content", request);

            if (result == null || !result.Success || result.Data?.Questions == null || result.Data.Questions.Count == 0)
            {
                _logger.LogWarning("Génération de quiz d'entraînement échouée pour l'utilisateur {UserId} : {Error}", userId, result?.Error);
                return null;
            }

            return new SubjectQuizGenerationResult { Subject = result.Data.Subject, Difficulty = result.Data.Difficulty, Questions = result.Data.Questions };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la génération du quiz d'entraînement pour l'utilisateur {UserId}", userId);
            return null;
        }
    }

    public async Task<GeneratedRevisionContentDto?> GenerateRevisionContentAsync(int userId, string? subject, string? topic, string? level = null, string? contextHint = null, string? difficulty = null)
    {
        try
        {
            _logger.LogInformation("Génération d'une fiche de révision IA pour l'utilisateur {UserId} en {Subject}", userId, subject ?? "(matière à choisir par l'IA)");

            var request = new { user_id = userId, subject, topic, level, context_hint = contextHint, difficulty };
            var result = await PostAsync<GeneratedRevisionContentDto>("/api/revisions/generate-content", request);

            if (result == null || !result.Success || string.IsNullOrWhiteSpace(result.ContentMarkdown))
            {
                _logger.LogWarning("Génération de fiche de révision échouée pour l'utilisateur {UserId}", userId);
                return null;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la génération de la fiche de révision pour l'utilisateur {UserId}", userId);
            return null;
        }
    }

    /// <summary>
    /// Parcours d'apprentissage personnalisé  voir IFastApiClient.GenerateLearningPathAsync.
    /// </summary>
    public async Task<LearningPathResponse?> GenerateLearningPathAsync(int userId)
    {
        // Le service Python calcule le parcours depuis les performances réelles
        // en base : il n'attend ni matière ni volume horaire. Réponse en
        // snake_case (schemas.py::LearningPathResponse) : SnakeCaseJson obligatoire,
        // sans quoi phases/learning_velocity restaient vides (Module 23).
        var (statusCode, body) = await GetRawJsonAsync($"/api/learning-path/{userId}");
        if (statusCode is < 200 or >= 300 || body == null)
        {
            _logger.LogWarning("Parcours d'apprentissage non généré pour {UserId} : HTTP {Status}  {Body}", userId, statusCode, body);
            return null;
        }

        try
        {
            var response = JsonSerializer.Deserialize<LearningPathResponse>(body, SnakeCaseJson);
            if (response == null || !response.Success)
            {
                _logger.LogWarning("Parcours d'apprentissage : réponse Python sans succès pour {UserId} : {Body}", userId, body);
                return null;
            }
            _logger.LogInformation("Parcours généré pour {UserId} : {Count} phases", userId, response.Phases.Count);
            return response;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Parcours d'apprentissage : réponse Python illisible pour {UserId} : {Body}", userId, body);
            return null;
        }
    }

    /// <summary>Voir IFastApiClient.QueueRagIngestion.</summary>
    public void QueueRagIngestion(
        string docId,
        string title,
        string fileUrl,
        string? authorizationHeader,
        string? category = null,
        int? subjectId = null,
        int? courseId = null,
        int? lessonId = null)
    {
        var payload = new
        {
            doc_id = docId,
            title,
            file_path = fileUrl,
            category,
            subject_id = subjectId,
            course_id = courseId,
            lesson_id = lessonId,
        };
        var json = JsonSerializer.Serialize(payload);

        _ = Task.Run(async () =>
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rag/ingest")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                if (!string.IsNullOrWhiteSpace(authorizationHeader))
                    request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Ingestion RAG mise en file pour {DocId}", docId);
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning(
                        "Ingestion RAG refusée pour {DocId} : HTTP {Status}  {Body}",
                        docId, (int)response.StatusCode, body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Ingestion RAG non déclenchée pour {DocId} (non bloquant, le contenu reste utilisable normalement)",
                    docId);
            }
        });
    }

    #endregion
}
