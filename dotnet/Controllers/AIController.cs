using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Controllers;

[ApiController]
    [Route("api/ai")]
    [Authorize]
    public class AIController : ControllerBase
    {
        private readonly IAIService _aiService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AIController> _logger;

        public AIController(IAIService aiService, IHttpClientFactory httpClientFactory, ILogger<AIController> logger)
        {
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpPost("recommend")]
        [ProducesResponseType(typeof(RecommendationResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetRecommendations([FromBody] RecommendationRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var response = await _aiService.GetRecommendationsAsync(
                    request.UserId,
                    request.NumberOfRecommendations,
                    request.PreferenceLevel,
                    request.SubjectCategory);

                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
                return StatusCode(500, new { message = "An error occurred" });
            }
        }

        [HttpPost("analyze-progress")]
        [ProducesResponseType(typeof(ProgressAnalysisResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> AnalyzeProgress([FromBody] ProgressAnalysisRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var response = await _aiService.AnalyzeProgressAsync(
                    request.UserId,
                    request.SubjectId,
                    request.AnalysisDepth);

                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
                return StatusCode(500, new { message = "An error occurred" });
            }
        }

        [HttpPost("generate-quiz")]
        [ProducesResponseType(typeof(QuizGenerationResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GenerateQuiz([FromBody] QuizGenerationRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var response = await _aiService.GenerateQuizAsync(
                    request.UserId,
                    request.SubjectId,
                    request.NumberOfQuestions,
                    request.Difficulty);

                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
                return StatusCode(500, new { message = "An error occurred" });
            }
        }

        [HttpGet("performance")]
        [ProducesResponseType(typeof(PerformanceMetricsResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetPerformance(
            [FromQuery] int userId,
            [FromQuery] string timePeriod = "7days")
        {
            try
            {
                if (userId <= 0)
                    return BadRequest(new { message = "User ID must be > 0" });

                var response = await _aiService.GetPerformanceMetricsAsync(userId, timePeriod);

                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
                return StatusCode(500, new { message = "An error occurred" });
            }
        }

        [HttpPost("personalized-path")]
        [ProducesResponseType(typeof(LearningPathResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GeneratePersonalizedPath([FromBody] LearningPathRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var response = await _aiService.GeneratePersonalizedPathAsync(
                    request.UserId,
                    request.GoalSubject,
                    request.TimeframeWeeks,
                    request.AvailableHoursPerWeek);

                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
                return StatusCode(500, new { message = "An error occurred" });
            }
        }

        /// <summary>
        /// GET /api/ai/recommendations/{id}
        /// Récupérer les recommandations IA pour un sujet spécifique
        /// </summary>
        [HttpGet("recommendations/{id}")]
        [ProducesResponseType(typeof(RecommendationResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetRecommendationsById(
            [FromRoute] int id,
            [FromQuery] int count = 5,
            [FromQuery] string preferenceLevel = "intermediate",
            [FromQuery] string category = "all")
        {
            try
            {
                if (id <= 0)
                    return BadRequest(new { message = "Invalid subject ID" });

                var response = await _aiService.GetRecommendationsAsync(id, count, preferenceLevel, category);
                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
                return StatusCode(500, new { message = "An error occurred" });
            }
        }

        /// <summary>
        /// POST /api/ai/predict-success
        /// Prédire le succès de l'utilisateur pour un sujet
        /// </summary>
        [HttpPost("predict-success")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> PredictSuccess([FromBody] PredictSuccessRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                // Pour l'instant, retourner une réponse simulée
                var response = new
                {
                    subjectId = request.SubjectId,
                    successProbability = new Random().NextDouble(),
                    recommendation = "Êtes-vous prêt à apprendre ce sujet ?",
                    estimatedHours = new Random().Next(10, 100)
                };

                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
                return StatusCode(500, new { message = "An error occurred" });
            }
        }

        /// <summary>
        /// POST /api/ai/study-plan
        /// Générer un plan d'étude personnalisé
        /// </summary>
        [HttpPost("study-plan")]
        [ProducesResponseType(typeof(LearningPathResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GenerateStudyPlan([FromBody] StudyPlanRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var response = await _aiService.GeneratePersonalizedPathAsync(
                    request.UserId,
                    request.SubjectName,
                    request.DurationWeeks,
                    request.HoursPerWeek);

                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
                return StatusCode(500, new { message = "An error occurred" });
            }
        }

        /// <summary>
        /// POST /api/ai/chat
        /// Discuter avec l'assistant IA
        /// </summary>
        [HttpPost("chat")]
        [ProducesResponseType(typeof(ChatResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(500)]
        /// <summary>
        /// POST /api/ai/explain-error
        /// Streaming proxy → Python /api/quiz/explain-error
        /// Retourne un flux SSE avec l'explication pédagogique WinAI.
        /// </summary>
        [HttpPost("explain-error")]
        public async Task ExplainError([FromBody] ExplainErrorRequest request, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                Response.StatusCode = 400;
                return;
            }

            Response.ContentType = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";

            var body = new
            {
                question_text  = request.QuestionText,
                wrong_answer   = request.WrongAnswer,
                correct_answer = request.CorrectAnswer,
                subject        = request.Subject,
                level          = request.Level,
            };

            var httpClient = _httpClientFactory.CreateClient("FastApiClient");
            using var fastApiReq = new HttpRequestMessage(HttpMethod.Post, "/api/quiz/explain-error");
            fastApiReq.Content = JsonContent.Create(body);

            var authHeader = HttpContext.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(authHeader))
                fastApiReq.Headers.TryAddWithoutValidation("Authorization", authHeader);

            try
            {
                using var fastApiRes = await httpClient.SendAsync(
                    fastApiReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!fastApiRes.IsSuccessStatusCode)
                {
                    _logger.LogError("FastAPI explain-error returned {Status}", fastApiRes.StatusCode);
                    await Response.WriteAsync("data: {\"error\": \"AI service unavailable\"}\n\ndata: [DONE]\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                    return;
                }

                using var stream = await fastApiRes.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new System.IO.StreamReader(stream);

                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line == null) break;
                    await Response.WriteAsync(line + "\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("explain-error stream cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "explain-error stream proxy error");
                try
                {
                    await Response.WriteAsync("data: {\"error\": \"Stream error\"}\n\ndata: [DONE]\n\n");
                    await Response.Body.FlushAsync(CancellationToken.None);
                }
                catch { }
            }
        }

        /// <summary>
        /// GET /api/ai/study-habits
        /// Récupérer les habitudes d'étude de l'utilisateur
        /// </summary>
        [HttpGet("study-habits")]
        [ProducesResponseType(typeof(StudyHabitsResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetStudyHabits([FromQuery] int userId = 0)
        {
            try
            {
                // Pour l'instant, retourner une réponse simulée
                var response = new StudyHabitsResponse
                {
                    AverageDailyHours = 2.5,
                    PreferredStudyTime = "Evening",
                    MostActiveDay = "Wednesday",
                    CompletionRate = 0.85,
                    LastStudySession = DateTime.UtcNow.AddDays(-1)
                };

                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
                return StatusCode(500, new { message = "An error occurred" });
            }
        }

        /// <summary>
        /// POST /api/ai/exam-coach/generate
        /// Proxy → Python /api/exam-coach/generate
        /// </summary>
        [HttpPost("exam-coach/generate")]
        public async Task<IActionResult> ExamCoachGenerate([FromBody] object body, CancellationToken ct)
        {
            var httpClient = _httpClientFactory.CreateClient("FastApiClient");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/exam-coach/generate");
            req.Content = JsonContent.Create(body);
            var auth = HttpContext.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(auth)) req.Headers.TryAddWithoutValidation("Authorization", auth);
            var res = await httpClient.SendAsync(req, ct);
            var content = await res.Content.ReadAsStringAsync(ct);
            return Content(content, "application/json");
        }

        /// <summary>
        /// POST /api/ai/exam-coach/recalibrate
        /// Proxy → Python /api/exam-coach/recalibrate
        /// </summary>
        [HttpPost("exam-coach/recalibrate")]
        public async Task<IActionResult> ExamCoachRecalibrate([FromBody] object body, CancellationToken ct)
        {
            var httpClient = _httpClientFactory.CreateClient("FastApiClient");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/exam-coach/recalibrate");
            req.Content = JsonContent.Create(body);
            var auth = HttpContext.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(auth)) req.Headers.TryAddWithoutValidation("Authorization", auth);
            var res = await httpClient.SendAsync(req, ct);
            var content = await res.Content.ReadAsStringAsync(ct);
            return Content(UnwrapPythonData(content), "application/json");
        }

        /// <summary>
        /// GET /api/ai/exam-coach/today
        /// Proxy → Python /api/exam-coach/today/{userId}
        /// </summary>
        [HttpGet("exam-coach/today")]
        public async Task<IActionResult> ExamCoachToday([FromQuery] int userId, CancellationToken ct)
        {
            var httpClient = _httpClientFactory.CreateClient("FastApiClient");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/exam-coach/today/{userId}");
            var auth = HttpContext.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(auth)) req.Headers.TryAddWithoutValidation("Authorization", auth);
            var res = await httpClient.SendAsync(req, ct);
            var content = await res.Content.ReadAsStringAsync(ct);
            return Content(UnwrapPythonData(content), "application/json");
        }

        /// Extracts the inner `data` field from Python's { success, data } response envelope.
        private static string UnwrapPythonData(string raw)
        {
            try
            {
                var doc = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(raw);
                if (doc.TryGetProperty("data", out var data))
                    return data.GetRawText();
            }
            catch { }
            return raw;
        }
    }
