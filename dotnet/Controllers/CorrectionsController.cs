using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Notation d'une copie précise (Module 4). Route distincte de
/// /api/teacher/assignments : CorrectionQueue.tsx appelle déjà
/// POST /api/corrections/{id} pour envoyer ou brouillonner une note.
/// </summary>
[ApiController]
[Route("api/corrections")]
[Authorize]
public class CorrectionsController : ControllerBase
{
    private readonly IAssignmentService _service;

    public CorrectionsController(IAssignmentService service)
    {
        _service = service;
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var teacherId = User.GetUserId();
        var submission = await _service.GetSubmissionAsync(teacherId, id);
        return submission == null ? NotFound(new { success = false, error = "Copie introuvable." }) : Ok(new { data = submission, success = true });
    }

    /// <summary>Body: {note, comment, status: "draft"|"submitted"} — contrat déjà utilisé par CorrectionQueue.tsx.</summary>
    [HttpPost("{id:int}")]
    public async Task<IActionResult> Grade(int id, [FromBody] GradeSubmissionRequestDto request)
    {
        try
        {
            var teacherId = User.GetUserId();
            var submission = await _service.GradeSubmissionAsync(teacherId, id, request);
            return Ok(new { data = submission, success = true });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, error = ex.Message }); }
    }
}
