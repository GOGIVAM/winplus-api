using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Backend.Services;
using Backend.Extensions;

namespace Backend.Controllers;

/// <summary>
/// Controller pour les fonctionnalités des professeurs
/// </summary>
[ApiController]
[Route("api/teacher")]
[Produces("application/json")]
[Authorize]
public class TeacherController : ControllerBase
{
    private readonly ITeacherService _teacherService;
    private readonly ILogger<TeacherController> _logger;

    public TeacherController(ITeacherService teacherService, ILogger<TeacherController> logger)
    {
        _teacherService = teacherService;
        _logger = logger;
    }

    /// <summary>
    /// Récupère le contenu du professeur
    /// </summary>
    /// <param name="teacherId">ID du professeur</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Contenu du professeur</returns>
    [HttpGet("contents")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetContents([FromQuery] int teacherId, [FromQuery] int limit = 50)
    {
        try
        {
            var contents = await _teacherService.GetTeacherContentsAsync(teacherId, limit);
            return Ok(new { data = contents, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher contents");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les étudiants récents du professeur
    /// </summary>
    /// <param name="teacherId">ID du professeur</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Étudiants récents</returns>
    [HttpGet("students/recent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetRecentStudents([FromQuery] int teacherId, [FromQuery] int limit = 10)
    {
        try
        {
            var students = await _teacherService.GetTeacherStudentsAsync(teacherId, limit);
            return Ok(new { data = students, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher students");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les corrections en attente
    /// </summary>
    /// <param name="teacherId">ID du professeur</param>
    /// <returns>Corrections en attente</returns>
    [HttpGet("corrections/pending")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPendingCorrections([FromQuery] int teacherId)
    {
        try
        {
            var corrections = await _teacherService.GetPendingCorrectionsAsync(teacherId);
            return Ok(new { data = corrections, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending corrections");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les sessions à venir
    /// </summary>
    /// <param name="teacherId">ID du professeur</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Sessions à venir</returns>
    [HttpGet("sessions/upcoming")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetUpcomingSessions([FromQuery] int teacherId, [FromQuery] int limit = 10)
    {
        try
        {
            var sessions = await _teacherService.GetUpcomingSessionsAsync(teacherId, limit);
            return Ok(new { data = sessions, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting upcoming sessions");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les quizzes disponibles
    /// </summary>
    /// <param name="teacherId">ID du professeur</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Quizzes disponibles</returns>
    [HttpGet("quizzes/available")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAvailableQuizzes([FromQuery] int teacherId, [FromQuery] int limit = 10)
    {
        try
        {
            var quizzes = await _teacherService.GetTeacherQuizzesAsync(teacherId, limit);
            return Ok(new { data = quizzes, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available quizzes");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les révisions disponibles
    /// </summary>
    /// <param name="teacherId">ID du professeur</param>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Révisions disponibles</returns>
    [HttpGet("revisions/available")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAvailableRevisions([FromQuery] int teacherId, [FromQuery] int limit = 10)
    {
        try
        {
            var revisions = await _teacherService.GetTeacherRevisionsAsync(teacherId, limit);
            return Ok(new { data = revisions, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available revisions");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les statistiques du professeur
    /// </summary>
    /// <param name="teacherId">ID du professeur</param>
    /// <returns>Statistiques</returns>
    [HttpGet("stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetStats([FromQuery] int teacherId)
    {
        try
        {
            var stats = await _teacherService.GetTeacherStatsAsync(teacherId);
            return Ok(new { data = stats, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher stats");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère le profil du professeur
    /// </summary>
    [HttpGet("profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            var teacherId = User.GetUserId();
            var profile = await _teacherService.GetTeacherProfileAsync(teacherId);
            return Ok(new { data = profile, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher profile");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les revenus du professeur
    /// </summary>
    [HttpGet("revenues")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetRevenues([FromQuery] int teacherId)
    {
        try
        {
            var revenues = await _teacherService.GetTeacherRevenuesAsync(teacherId);
            return Ok(new { data = revenues, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher revenues");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
