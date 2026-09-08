using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>Avis élève sur les séances de cours particulier (US-REP-08, Module 6).</summary>
[ApiController]
[Route("api/tutor-reviews")]
[Produces("application/json")]
public class TutorReviewController : ControllerBase
{
    private readonly ITutorReviewService _service;

    public TutorReviewController(ITutorReviewService service)
    {
        _service = service;
    }

    /// <summary>L'élève note une séance effectuée (une seule fois par réservation).</summary>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<TutorReviewDto>> Submit([FromBody] SubmitTutorReviewRequestDto request)
    {
        try { return Ok(await _service.SubmitAsync(User.GetUserId(), request)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Le répétiteur répond publiquement à un avis.</summary>
    [HttpPost("{id:int}/reply")]
    [Authorize]
    public async Task<ActionResult<TutorReviewDto>> Reply(int id, [FromBody] ReplyToTutorReviewRequestDto request)
    {
        try { return Ok(await _service.ReplyAsync(User.GetUserId(), id, request.Reply)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Signale un avis comme abusif — l'équipe WinPlus tranche.</summary>
    [HttpPost("{id:int}/report")]
    [Authorize]
    public async Task<IActionResult> Report(int id, [FromBody] ReportTutorReviewRequestDto request)
    {
        try
        {
            await _service.ReportAsync(User.GetUserId(), id, request.Reason);
            return Ok(new { message = "Avis signalé. L'équipe WinPlus va l'examiner." });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Avis publics d'un répétiteur (fiche publique).</summary>
    [HttpGet("tutor/{tutorUserId:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<TutorReviewDto>>> GetForTutor(int tutorUserId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(await _service.GetForTutorAsync(tutorUserId, page, pageSize));
}
