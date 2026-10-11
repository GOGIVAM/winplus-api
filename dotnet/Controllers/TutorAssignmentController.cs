using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Lot 9.3 (suivi 17.4) : attribution d'un tuteur par un parent (à ses enfants)
/// ou une institution (à un groupe ou une classe), et contexte de réservation
/// pour autrui.
///
/// GET  /api/tutor-assignments/booking-context  enfants, élèves, groupes, attributions actives
/// POST /api/tutor-assignments                  créer (parent ou institution)
/// GET  /api/tutor-assignments/mine             mes attributions (parent / institution)
/// GET  /api/tutor-assignments/for-me           attributions actives dont je suis bénéficiaire (élève)
/// GET  /api/tutor-assignments/tutor            attributions reçues (tuteur)
/// PUT  /api/tutor-assignments/{id}/accept      le tuteur accepte
/// PUT  /api/tutor-assignments/{id}/decline     le tuteur refuse
/// PUT  /api/tutor-assignments/{id}/end         l'attribuant ou le tuteur y met fin
/// </summary>
[ApiController]
[Route("api/tutor-assignments")]
[Authorize]
[Produces("application/json")]
public class TutorAssignmentController : ControllerBase
{
    private readonly ITutorAssignmentService _service;

    public TutorAssignmentController(ITutorAssignmentService service) => _service = service;

    private ActionResult Fail(InvalidOperationException ex) =>
        ex is TutorBookingForbiddenException
            ? StatusCode(403, new { message = ex.Message })
            : BadRequest(new { message = ex.Message });

    [HttpGet("booking-context")]
    public async Task<ActionResult<TutorBookingContextDto>> GetBookingContext()
    {
        try { return Ok(await _service.GetBookingContextAsync(User.GetUserId())); }
        catch (InvalidOperationException ex) { return Fail(ex); }
    }

    [HttpPost]
    public async Task<ActionResult<TutorAssignmentDto>> Create([FromBody] CreateTutorAssignmentRequestDto request)
    {
        try { return Ok(await _service.CreateAsync(User.GetUserId(), request)); }
        catch (InvalidOperationException ex) { return Fail(ex); }
    }

    [HttpGet("mine")]
    public async Task<ActionResult<List<TutorAssignmentDto>>> GetMine()
        => Ok(await _service.GetAsAssignerAsync(User.GetUserId()));

    [HttpGet("for-me")]
    public async Task<ActionResult<List<TutorAssignmentDto>>> GetForMe()
        => Ok(await _service.GetForStudentAsync(User.GetUserId()));

    [HttpGet("tutor")]
    public async Task<ActionResult<List<TutorAssignmentDto>>> GetAsTutor()
        => Ok(await _service.GetAsTutorAsync(User.GetUserId()));

    [HttpPut("{id:int}/accept")]
    public async Task<ActionResult<TutorAssignmentDto>> Accept(int id, [FromBody] RespondTutorAssignmentRequestDto? request)
    {
        try { return Ok(await _service.AcceptAsync(User.GetUserId(), id, request?.Note)); }
        catch (InvalidOperationException ex) { return Fail(ex); }
    }

    [HttpPut("{id:int}/decline")]
    public async Task<ActionResult<TutorAssignmentDto>> Decline(int id, [FromBody] RespondTutorAssignmentRequestDto? request)
    {
        try { return Ok(await _service.DeclineAsync(User.GetUserId(), id, request?.Note)); }
        catch (InvalidOperationException ex) { return Fail(ex); }
    }

    [HttpPut("{id:int}/end")]
    public async Task<ActionResult<TutorAssignmentDto>> End(int id)
    {
        try { return Ok(await _service.EndAsync(User.GetUserId(), id)); }
        catch (InvalidOperationException ex) { return Fail(ex); }
    }
}
