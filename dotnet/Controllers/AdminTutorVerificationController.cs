using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Lot 9.5 (suivi 17.11, 17.12, 17.16) : écrans admin de la vérification des
/// tuteurs pour le présentiel (pièces d'identité + selfie, entretiens assistés
/// par WinAI) et de la sécurité (signalements, suspensions, levée avec motif,
/// historique, contrôle des séances présentielles avec un tuteur non vérifié).
/// Les fichiers d'identité ne sont lisibles qu'ici, par URL présignée de
/// quelques minutes (ou, en stockage local de développement, par
/// l'endpoint « files/{kind} » protégé par le même contrôle admin).
/// </summary>
[ApiController]
[Route("api/admin/tutor-verification")]
[Authorize(Policy = "AdminOnly")]
[Produces("application/json")]
public class AdminTutorVerificationController : ControllerBase
{
    private readonly ITutorVerificationService _verification;
    private readonly ITutorReportService _reports;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AdminTutorVerificationController> _logger;

    public AdminTutorVerificationController(ITutorVerificationService verification, ITutorReportService reports,
        IHttpClientFactory httpClientFactory, ILogger<AdminTutorVerificationController> logger)
    {
        _verification = verification;
        _reports = reports;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private static TutorIdentityUpload? Upload(IFormFile? file) =>
        file == null || file.Length == 0 ? null : new TutorIdentityUpload(file.OpenReadStream(), file.FileName, file.ContentType, file.Length);

    private async Task<ActionResult> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ── Pièce d'identité + selfie ──

    [HttpGet("identity-checks")]
    public Task<ActionResult> IdentityChecks([FromQuery] string? status) => Run(() => _verification.GetIdentityChecksAsync(status));

    /// <summary>URL de lecture courtes (présignées) du recto, du verso et du selfie, pour la comparaison.</summary>
    [HttpGet("identity-checks/{id:int}/files")]
    public Task<ActionResult> IdentityFiles(int id)
    {
        Response.Headers["Cache-Control"] = "no-store";
        return Run(() => _verification.GetIdentityFilesAsync(id, "/api/admin/tutor-verification/identity-checks"));
    }

    /// <summary>Lecture d'un fichier en stockage privé local (développement, sans S3).</summary>
    [HttpGet("identity-checks/{id:int}/files/{kind}")]
    public async Task<IActionResult> IdentityFile(int id, string kind)
    {
        var file = await _verification.OpenIdentityFileAsync(id, kind);
        if (file is not { } f) return NotFound();
        Response.Headers["Cache-Control"] = "no-store";
        return File(f.Content, f.ContentType);
    }

    [HttpPost("identity-checks/{id:int}/approve")]
    public Task<ActionResult> ApproveIdentity(int id) => Run(() => _verification.ReviewIdentityAsync(User.GetUserId(), id, true, null));

    [HttpPost("identity-checks/{id:int}/reject")]
    public Task<ActionResult> RejectIdentity(int id, [FromBody] ReasonRequestDto request) =>
        Run(() => _verification.ReviewIdentityAsync(User.GetUserId(), id, false, request?.Reason));

    // ── Entretiens ──

    [HttpGet("interviews")]
    public Task<ActionResult> Interviews([FromQuery] string? status) => Run(() => _verification.GetInterviewsAsync(status));

    [HttpGet("interviews/{id:int}")]
    public Task<ActionResult> Interview(int id) => Run(() => _verification.GetInterviewAsync(id));

    [HttpPost("interviews/{id:int}/schedule")]
    public Task<ActionResult> Schedule(int id, [FromBody] ScheduleInterviewDto request) =>
        Run(() => _verification.ScheduleInterviewAsync(User.GetUserId(), id, request));

    [HttpPut("interviews/{id:int}/notes")]
    public Task<ActionResult> SaveNotes(int id, [FromBody] SaveInterviewNotesDto request) =>
        Run(() => _verification.SaveInterviewNotesAsync(User.GetUserId(), id, request));

    [HttpPost("interviews/{id:int}/complete")]
    public Task<ActionResult> Complete(int id, [FromBody] CompleteInterviewDto request) =>
        Run(() => _verification.CompleteInterviewAsync(User.GetUserId(), id, request));

    /// <summary>L'agent joint au dossier la pièce d'identité et le selfie pris pendant l'entretien.</summary>
    [HttpPost("interviews/{id:int}/identity")]
    [RequestSizeLimit(3 * TutorVerificationService.MaxIdentityFileBytes + 64 * 1024)]
    public async Task<ActionResult> InterviewIdentity(int id, [FromForm] string documentType,
        [FromForm] IFormFile? front, [FromForm] IFormFile? back, [FromForm] IFormFile? selfie)
    {
        if (Upload(front) is not { } f) return BadRequest(new { message = "Ajoute la photo du recto de la pièce d'identité." });
        if (Upload(selfie) is not { } s) return BadRequest(new { message = "Ajoute le selfie pris pendant l'entretien." });
        return await Run(() => _verification.SubmitIdentityForInterviewAsync(User.GetUserId(), id, documentType, f, Upload(back), s));
    }

    // ── WinAI assiste l'agent ──
    // Le contexte (matières, niveaux, spécialités) est lu en base ici, jamais
    // repris du client ; l'appel Python relaie le jeton de l'admin, contrôlé
    // côté FastAPI par require_user_or_service(roles=("admin",)).

    private async Task<JsonElement?> CallWinAiAsync(string path, object body, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("FastApiClient");
        using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        var auth = HttpContext.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(auth)) req.Headers.TryAddWithoutValidation("Authorization", auth);
        try
        {
            using var res = await client.SendAsync(req, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("WinAI {Path} : {Status} {Body}", path, (int)res.StatusCode, text.Length > 300 ? text[..300] : text);
                return null;
            }
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "WinAI {Path} injoignable", path);
            return null;
        }
    }

    /// <summary>Guide d'entretien et grille d'évaluation générés par WinAI selon le profil du tuteur.</summary>
    [HttpPost("interviews/{id:int}/ai-guide")]
    public async Task<ActionResult> AiGuide(int id, CancellationToken ct)
    {
        AdminInterviewDto interview;
        try { interview = await _verification.GetInterviewAsync(id); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        var result = await CallWinAiAsync("/api/admin/tutor-interview/guide", new
        {
            subjects = interview.Subjects,
            levels = interview.Levels,
            specialties = interview.Specialties,
            teaching_style = interview.TeachingStyle,
            bio = interview.Bio,
            format = interview.Format,
        }, ct);
        if (result == null)
            return StatusCode(502, new { message = "WinAI est momentanément indisponible. Réessaie dans un instant." });
        return await Run(() => _verification.SaveInterviewAiAsync(id, result.Value.GetRawText(), null));
    }

    /// <summary>Brouillon de compte rendu rédigé par WinAI à partir des notes et de la grille de l'agent.</summary>
    [HttpPost("interviews/{id:int}/ai-report")]
    public async Task<ActionResult> AiReport(int id, CancellationToken ct)
    {
        AdminInterviewDto interview;
        try { interview = await _verification.GetInterviewAsync(id); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        if (string.IsNullOrWhiteSpace(interview.AgentNotes) || interview.AgentNotes.Trim().Length < 20)
            return BadRequest(new { message = "Enregistre d'abord tes notes d'entretien (20 caractères au moins)." });

        object? scores = null;
        object? grid = null;
        try { if (!string.IsNullOrWhiteSpace(interview.ScoresJson)) scores = JsonDocument.Parse(interview.ScoresJson).RootElement.Clone(); } catch (JsonException) { }
        try
        {
            if (!string.IsNullOrWhiteSpace(interview.GuideJson)
                && JsonDocument.Parse(interview.GuideJson).RootElement.TryGetProperty("grid", out var g))
                grid = g.Clone();
        }
        catch (JsonException) { }

        var result = await CallWinAiAsync("/api/admin/tutor-interview/report", new
        {
            subjects = interview.Subjects,
            levels = interview.Levels,
            format = interview.Format,
            notes = interview.AgentNotes,
            scores,
            grid,
            identity_attached = interview.Identity != null,
            identity_approved = interview.HasApprovedIdentity,
        }, ct);
        if (result == null)
            return StatusCode(502, new { message = "WinAI est momentanément indisponible. Réessaie dans un instant." });
        var draft = result.Value.TryGetProperty("report", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : result.Value.GetRawText();
        return await Run(() => _verification.SaveInterviewAiAsync(id, null, draft));
    }

    // ── Caution (révocation par un admin si besoin) ──

    [HttpPost("endorsements/{id:int}/revoke")]
    public Task<ActionResult> RevokeEndorsement(int id, [FromBody] ReasonRequestDto request) =>
        Run(() => _verification.RevokeEndorsementAsync(User.GetUserId(), id, request?.Reason, asAdmin: true));

    // ── Signalements, suspensions, historique, contrôle ──

    [HttpGet("reports")]
    public Task<ActionResult> Reports([FromQuery] string? status, [FromQuery] int? tutorProfileId) =>
        Run(() => _reports.GetReportsAsync(status, tutorProfileId));

    [HttpPost("reports/{id:int}/review")]
    public Task<ActionResult> ReviewReport(int id, [FromBody] ReasonRequestDto request) =>
        Run(() => _reports.MarkReviewedAsync(User.GetUserId(), id, request?.Reason));

    [HttpGet("suspensions")]
    public Task<ActionResult> Suspensions() => Run(() => _reports.GetSuspendedAsync());

    [HttpGet("tutors/{tutorProfileId:int}")]
    public Task<ActionResult> SafetyFile(int tutorProfileId) => Run(() => _reports.GetSafetyFileAsync(tutorProfileId));

    [HttpPost("tutors/{tutorProfileId:int}/lift-suspension")]
    public Task<ActionResult> LiftSuspension(int tutorProfileId, [FromBody] ReasonRequestDto request) =>
        Run(() => _reports.LiftSuspensionAsync(User.GetUserId(), tutorProfileId, request?.Reason));

    /// <summary>Contrôle : séances présentielles à venir avec un tuteur non vérifié ou suspendu (non annulées).</summary>
    [HttpGet("in-person-control")]
    public Task<ActionResult> InPersonControl() => Run(() => _verification.GetInPersonControlAsync());
}
