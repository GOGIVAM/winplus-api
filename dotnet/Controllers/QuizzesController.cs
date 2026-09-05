using Backend.Models.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;
using Backend.Extensions;
using Microsoft.AspNetCore.Authorization;

namespace Backend.Controllers;

/// <summary>
/// API pour gérer les Quiz
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class QuizzesController : ControllerBase
{
    private readonly IQuizService _quizService;
    private readonly IDailyScoreService _dailyScore;

    public QuizzesController(IQuizService quizService, IDailyScoreService dailyScore)
    {
        _quizService = quizService;
        _dailyScore = dailyScore;
    }

    /// <summary>
    /// Récupère tous les quiz avec pagination
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<QuizDto>), 200)]
    public async Task<ActionResult<IEnumerable<QuizDto>>> GetAllQuizzes([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var quizzes = await _quizService.GetAllQuizzesAsync(page, pageSize);
        return Ok(quizzes);
    }

    /// <summary>
    /// Récupère un quiz par son ID
    /// </summary>
    [HttpGet("{id}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(QuizDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<QuizDto>> GetQuizById(int id)
    {
        var quiz = await _quizService.GetQuizByIdAsync(id);
        if (quiz == null)
            return NotFound(new { message = "Quiz not found" });

        return Ok(quiz);
    }

    /// <summary>
    /// Récupère les quiz filtrés
    /// </summary>
    [HttpPost("filter")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<QuizDto>), 200)]
    public async Task<ActionResult<IEnumerable<QuizDto>>> SearchQuizzes([FromBody] QuizSearchFilterDto filter)
    {
        var quizzes = await _quizService.GetQuizzesAsync(filter);
        return Ok(quizzes);
    }

    /// <summary>
    /// Récupère les quiz par sujet
    /// </summary>
    [HttpGet("by-subject/{subject}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<QuizDto>), 200)]
    public async Task<ActionResult<IEnumerable<QuizDto>>> GetQuizzesBySubject(string subject, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var quizzes = await _quizService.GetQuizzesBySubjectAsync(subject, page, pageSize);
        return Ok(quizzes);
    }

    /// <summary>
    /// Récupère les quiz par difficulté
    /// </summary>
    [HttpGet("by-difficulty/{difficulty}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<QuizDto>), 200)]
    public async Task<ActionResult<IEnumerable<QuizDto>>> GetQuizzesByDifficulty(string difficulty, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var quizzes = await _quizService.GetQuizzesByDifficultyAsync(difficulty, page, pageSize);
        return Ok(quizzes);
    }

    /// <summary>
    /// Recherche des quiz
    /// </summary>
    [HttpGet("search")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<QuizDto>), 200)]
    public async Task<ActionResult<IEnumerable<QuizDto>>> SearchQuizzes([FromQuery] string q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var quizzes = await _quizService.SearchQuizzesAsync(q, page, pageSize);
        return Ok(quizzes);
    }

    /// <summary>
    /// Récupère les quiz publiés
    /// </summary>
    [HttpGet("published")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<QuizDto>), 200)]
    public async Task<ActionResult<IEnumerable<QuizDto>>> GetPublishedQuizzes([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var quizzes = await _quizService.GetPublishedQuizzesAsync(page, pageSize);
        return Ok(quizzes);
    }

    /// <summary>
    /// Génère un quiz d'entraînement personnalisé par IA pour l'utilisateur
    /// courant (matière la plus faible si non précisée).
    /// </summary>
    [HttpPost("me/generate")]
    [ProducesResponseType(typeof(QuizDto), 200)]
    [ProducesResponseType(503)]
    public async Task<ActionResult<QuizDto>> GenerateQuizForMe([FromBody] GenerateRevisionRequestDto? request)
    {
        var userId = GetUserId();
        if (userId == 0)
            return Unauthorized(new { message = "User not authenticated" });

        try
        {
            var quiz = await _quizService.GenerateAIQuizAsync(userId, request?.Subject, request?.Topic);
            return Ok(quiz);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Soumet les réponses d'un quiz et obtient les résultats évalués
    /// </summary>
    [HttpPost("{quizId}/submit")]
    [ProducesResponseType(typeof(QuizResultResponseDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<QuizResultResponseDto>> SubmitQuizAttempt(int quizId, [FromBody] SubmitQuizAttemptRequestDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // Récupérer l'ID utilisateur depuis le token (à implémenter selon votre système d'auth)
            var userId = GetUserId();
            if (userId == 0)
                return Unauthorized(new { message = "User not authenticated" });

            var result = await _quizService.SubmitQuizAttemptAsync(quizId, userId, request);
            try { await _dailyScore.UpsertDailyScoreAsync(userId, (decimal)result.Score); } catch { /* best-effort */ }
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Quiz not found" });
        }
    }

    /// <summary>
    /// Récupère les résultats d'une tentative de quiz
    /// </summary>
    [HttpGet("attempts/{attemptId}")]
    [ProducesResponseType(typeof(QuizAttemptDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<QuizAttemptDto>> GetQuizAttempt(int attemptId)
    {
        var attempt = await _quizService.GetQuizAttemptAsync(attemptId);
        if (attempt == null)
            return NotFound(new { message = "Quiz attempt not found" });

        return Ok(attempt);
    }

    /// <summary>
    /// Récupère les tentatives d'un utilisateur pour un quiz spécifique
    /// </summary>
    [HttpGet("{quizId}/user-attempts")]
    [ProducesResponseType(typeof(IEnumerable<QuizAttemptDto>), 200)]
    public async Task<ActionResult<IEnumerable<QuizAttemptDto>>> GetUserQuizAttempts(int quizId)
    {
        var userId = GetUserId();
        if (userId == 0)
            return Unauthorized(new { message = "User not authenticated" });

        var attempts = await _quizService.GetUserQuizAttemptsAsync(userId, quizId);
        return Ok(attempts);
    }

    /// <summary>
    /// Récupère toutes les tentatives de quiz d'un utilisateur
    /// </summary>
    [HttpGet("me/attempts")]
    [ProducesResponseType(typeof(IEnumerable<QuizAttemptDto>), 200)]
    public async Task<ActionResult<IEnumerable<QuizAttemptDto>>> GetMyQuizAttempts([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = GetUserId();
        if (userId == 0)
            return Unauthorized(new { message = "User not authenticated" });

        var attempts = await _quizService.GetUserAllQuizAttemptsAsync(userId, page, pageSize);
        return Ok(attempts);
    }

    /// <summary>
    /// « Mode évaluation » : récupère le quiz chronométré rattaché à cette
    /// épreuve précise, généré à partir du contenu réel du PDF s'il n'existe
    /// pas encore. Les tentatives suivantes réutilisent le même quiz.
    /// </summary>
    [HttpPost("exam/{examId}")]
    [ProducesResponseType(typeof(QuizDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<ActionResult<QuizDto>> GetOrCreateExamQuiz(int examId)
    {
        try
        {
            var quiz = await _quizService.GetOrCreateExamQuizAsync(examId);
            return Ok(quiz);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Crée un nouveau quiz (Admin only)
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ProducesResponseType(typeof(QuizDto), 201)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<QuizDto>> CreateQuiz([FromBody] CreateQuizRequestDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var quiz = await _quizService.CreateQuizAsync(request);
        return CreatedAtAction(nameof(GetQuizById), new { id = quiz.Id }, quiz);
    }

    /// <summary>
    /// Met à jour un quiz (Admin only)
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(QuizDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<QuizDto>> UpdateQuiz(int id, [FromBody] UpdateQuizRequestDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var quiz = await _quizService.UpdateQuizAsync(id, request);
            return Ok(quiz);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Quiz not found" });
        }
    }

    /// <summary>
    /// Publie un quiz (Admin only)
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/publish")]
    [ProducesResponseType(typeof(QuizDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<QuizDto>> PublishQuiz(int id)
    {
        try
        {
            var quiz = await _quizService.PublishQuizAsync(id);
            return Ok(quiz);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Quiz not found" });
        }
    }

    /// <summary>
    /// Dépublie un quiz (Admin only)
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/unpublish")]
    [ProducesResponseType(typeof(QuizDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<QuizDto>> UnpublishQuiz(int id)
    {
        try
        {
            var quiz = await _quizService.UnpublishQuizAsync(id);
            return Ok(quiz);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Quiz not found" });
        }
    }

    /// <summary>
    /// Supprime un quiz (Admin only)
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteQuiz(int id)
    {
        var result = await _quizService.DeleteQuizAsync(id);
        if (!result)
            return NotFound(new { message = "Quiz not found" });

        return NoContent();
    }

    /// <summary>
    /// Récupère les statistiques d'un quiz
    /// </summary>
    [HttpGet("{id}/stats")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<object>> GetQuizStats(int id)
    {
        try
        {
            var stats = await _quizService.GetQuizStatsAsync(id);
            return Ok(stats);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Quiz not found" });
        }
    }

    /// <summary>
    /// Récupère le score moyen d'un quiz
    /// </summary>
    [HttpGet("{id}/average-score")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(double), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<double>> GetQuizAverageScore(int id)
    {
        try
        {
            var averageScore = await _quizService.GetQuizAverageScoreAsync(id);
            return Ok(new { averageScore });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Quiz not found" });
        }
    }

    private int GetUserId()
    {
        // ⚠ Correctif : l'ancienne version lisait FindFirst("sub"), or ASP.NET Core
        // remappe "sub" vers ClaimTypes.NameIdentifier à la validation du JWT.
        // Le claim était donc toujours introuvable → 0 → 401, y compris avec un
        // token parfaitement valide (d'où les boucles de refresh inutiles côté
        // frontend). L'extension partagée essaie user_id, NameIdentifier puis sub.
        try
        {
            return User.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
