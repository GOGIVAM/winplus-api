using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Devoirs, copies et corrections (Module 4 — professeur_complete.md).
/// </summary>
[ApiController]
[Route("api/teacher/assignments")]
[Authorize]
public class AssignmentsController : ControllerBase
{
    private readonly IAssignmentService _service;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AssignmentsController> _logger;

    public AssignmentsController(IAssignmentService service, IHttpClientFactory httpClientFactory, ILogger<AssignmentsController> logger)
    {
        _service = service;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Crée un devoir. Si un énoncé est fourni, génère aussitôt le barème
    /// WinAI (US-COR-05) en réutilisant /teacher/generate-correction  même
    /// génération que celle du flow de publication, appliquée ici à un
    /// devoir de classe plutôt qu'à une épreuve du catalogue.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAssignmentRequestDto request)
    {
        try
        {
            var teacherId = User.GetUserId();
            var assignment = await _service.CreateAssignmentAsync(teacherId, request);

            if (request.GenerateRubric && !string.IsNullOrWhiteSpace(request.StatementText))
            {
                try
                {
                    var client = _httpClientFactory.CreateClient("FastApiClient");
                    using var req = new HttpRequestMessage(HttpMethod.Post, "/api/teacher/generate-correction")
                    {
                        Content = JsonContent.Create(new { exam_text = request.StatementText, subject = (string?)null, level = (string?)null })
                    };
                    var auth = HttpContext.Request.Headers["Authorization"].ToString();
                    if (!string.IsNullOrEmpty(auth)) req.Headers.TryAddWithoutValidation("Authorization", auth);
                    var res = await client.SendAsync(req);
                    if (res.IsSuccessStatusCode)
                    {
                        var rubricJson = await res.Content.ReadAsStringAsync();
                        await _service.SetRubricAsync(assignment.Id, rubricJson);
                        assignment.RubricJson = rubricJson;
                    }
                }
                catch (Exception ex)
                {
                    // Le devoir reste créé sans barème : le professeur peut le
                    // générer plus tard, ce n'est pas bloquant pour la suite.
                    _logger.LogWarning(ex, "Génération du barème échouée pour le devoir {AssignmentId}", assignment.Id);
                }
            }

            return Ok(new { data = assignment, success = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetMine()
    {
        var teacherId = User.GetUserId();
        var assignments = await _service.GetTeacherAssignmentsAsync(teacherId);
        return Ok(new { data = assignments, success = true });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var teacherId = User.GetUserId();
        var assignment = await _service.GetAssignmentAsync(teacherId, id);
        return assignment == null ? NotFound(new { success = false, error = "Devoir introuvable." }) : Ok(new { data = assignment, success = true });
    }

    /// <summary>Copie papier scannée par le professeur (US-COR-01, flux "upload professeur").</summary>
    [HttpPost("{id:int}/submissions")]
    public async Task<IActionResult> UploadSubmission(int id, [FromBody] TeacherUploadSubmissionRequestDto request)
    {
        try
        {
            var teacherId = User.GetUserId();
            var submission = await _service.TeacherUploadSubmissionAsync(teacherId, id, request);
            return Ok(new { data = submission, success = true });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, error = ex.Message }); }
    }

    /// <summary>Alertes de similarité entre copies pour ce devoir (US-COR-03).</summary>
    [HttpGet("{id:int}/similarity")]
    public async Task<IActionResult> GetSimilarity(int id)
    {
        try
        {
            var teacherId = User.GetUserId();
            var pairs = await _service.GetSimilarityAsync(teacherId, id);
            return Ok(new { data = pairs, success = true });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, error = ex.Message }); }
    }

    [HttpPost("similarity/dismiss")]
    public async Task<IActionResult> DismissSimilarity([FromBody] DismissSimilarityRequest request)
    {
        var teacherId = User.GetUserId();
        await _service.DismissSimilarityAsync(teacherId, request.SubmissionAId, request.SubmissionBId);
        return Ok(new { success = true });
    }
}

public record DismissSimilarityRequest(int SubmissionAId, int SubmissionBId);
