using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Lot 9.5 (suivi 17.11, 17.16) : parcours de vérification du tuteur pour le
/// présentiel (pièce d'identité + selfie, entretien) et caution par une
/// institution de rattachement. Les fichiers d'identité ne sont jamais
/// renvoyés au client (ni au tuteur ni à personne d'autre) : seul l'écran
/// admin les lit, par URL présignée courte.
/// </summary>
[ApiController]
[Route("api/tutor-verification")]
[Authorize]
[Produces("application/json")]
public class TutorVerificationController : ControllerBase
{
    private readonly ITutorVerificationService _service;
    private readonly ILogger<TutorVerificationController> _logger;

    public TutorVerificationController(ITutorVerificationService service, ILogger<TutorVerificationController> logger)
    {
        _service = service;
        _logger = logger;
    }

    private static TutorIdentityUpload? Upload(IFormFile? file) =>
        file == null || file.Length == 0 ? null : new TutorIdentityUpload(file.OpenReadStream(), file.FileName, file.ContentType, file.Length);

    private ActionResult Fail(Exception ex) => ex switch
    {
        UnauthorizedAccessException => StatusCode(403, new { message = ex.Message }),
        _ => BadRequest(new { message = ex.Message }),
    };

    /// <summary>Statut de vérification, voies, motifs de refus et historique du tuteur connecté.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<TutorVerificationStatusDto>> GetMine()
    {
        try { return Ok(await _service.GetStatusForTutorAsync(User.GetUserId())); }
        catch (InvalidOperationException ex) { return Fail(ex); }
    }

    /// <summary>
    /// Dépose la pièce d'identité (recto, verso facultatif) et un selfie
    /// (multipart). Avec interviewId : pièce jointe au dossier d'entretien.
    /// </summary>
    [HttpPost("me/identity")]
    [RequestSizeLimit(3 * TutorVerificationService.MaxIdentityFileBytes + 64 * 1024)]
    public async Task<ActionResult<TutorVerificationStatusDto>> SubmitIdentity(
        [FromForm] string documentType, [FromForm] IFormFile? front, [FromForm] IFormFile? back, [FromForm] IFormFile? selfie,
        [FromForm] int? interviewId)
    {
        if (Upload(front) is not { } f) return BadRequest(new { message = "Ajoute la photo du recto de ta pièce d'identité." });
        if (Upload(selfie) is not { } s) return BadRequest(new { message = "Ajoute un selfie (photo de ton visage)." });
        try
        {
            return Ok(await _service.SubmitIdentityAsync(User.GetUserId(), documentType, f, Upload(back), s, interviewId));
        }
        catch (InvalidOperationException ex) { return Fail(ex); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dépôt de pièce d'identité impossible pour l'utilisateur {UserId}", User.GetUserId());
            return StatusCode(500, new { message = "Envoi impossible pour l'instant. Réessaie dans quelques minutes." });
        }
    }

    /// <summary>Demande d'entretien de vérification (visio ou rencontre).</summary>
    [HttpPost("me/interview")]
    public async Task<ActionResult<TutorVerificationStatusDto>> RequestInterview([FromBody] RequestTutorInterviewDto request)
    {
        try { return Ok(await _service.RequestInterviewAsync(User.GetUserId(), request)); }
        catch (InvalidOperationException ex) { return Fail(ex); }
    }

    [HttpPost("me/interview/{id:int}/cancel")]
    public async Task<ActionResult<TutorVerificationStatusDto>> CancelInterview(int id)
    {
        try { return Ok(await _service.CancelInterviewAsync(User.GetUserId(), id)); }
        catch (InvalidOperationException ex) { return Fail(ex); }
    }

    // ── Institution : cautionner et révoquer ──

    /// <summary>Tuteurs rattachés à l'institution du compte connecté, avec l'état de la caution.</summary>
    [HttpGet("institution/tutors")]
    public async Task<ActionResult<List<InstitutionTutorForEndorsementDto>>> InstitutionTutors()
    {
        try { return Ok(await _service.GetInstitutionTutorsAsync(User.GetUserId())); }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { return Fail(ex); }
    }

    [HttpPost("institution/endorsements")]
    public async Task<ActionResult<TutorEndorsementDto>> Endorse([FromBody] CreateTutorEndorsementDto request)
    {
        try { return Ok(await _service.EndorseAsync(User.GetUserId(), request)); }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { return Fail(ex); }
    }

    [HttpPost("institution/endorsements/{id:int}/revoke")]
    public async Task<ActionResult<TutorEndorsementDto>> Revoke(int id, [FromBody] ReasonRequestDto? request)
    {
        try { return Ok(await _service.RevokeEndorsementAsync(User.GetUserId(), id, request?.Reason)); }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { return Fail(ex); }
    }
}

/// <summary>Lot 9.5 (17.12, 17.16) : signaler un tuteur depuis une séance.</summary>
[ApiController]
[Route("api/tutor-reports")]
[Authorize]
[Produces("application/json")]
public class TutorReportsController : ControllerBase
{
    private readonly ITutorReportService _service;

    public TutorReportsController(ITutorReportService service) => _service = service;

    /// <summary>Motifs proposés dans la liste.</summary>
    [HttpGet("reasons")]
    [AllowAnonymous]
    public ActionResult<List<TutorReportReasonDto>> Reasons() => Ok(_service.GetReasons());

    /// <summary>Signale le tuteur d'une séance (participants et payeur, y compris séance annulée ou en cours).</summary>
    [HttpPost]
    public async Task<ActionResult<TutorReportCreatedDto>> Submit([FromBody] CreateTutorReportDto request)
    {
        try { return Ok(await _service.SubmitAsync(User.GetUserId(), request)); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
