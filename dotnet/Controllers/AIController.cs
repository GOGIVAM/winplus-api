using System;
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
        private readonly ILogger<AIController> _logger;

        public AIController(IAIService aiService, ILogger<AIController> logger)
        {
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
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
    }
