using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>Sessions d'enseignement en ligne (Module 5 — live/enregistrement/correction).</summary>
[ApiController]
[Route("api/sessions")]
[Authorize]
public class SessionsController : ControllerBase
{
    private readonly ITeachingSessionService _service;
    private readonly IHttpClientFactory _httpClientFactory;

    public SessionsController(ITeachingSessionService service, IHttpClientFactory httpClientFactory)
    {
        _service = service;
        _httpClientFactory = httpClientFactory;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSessionRequestDto request)
    {
        try
        {
            var teacherId = User.GetUserId();
            var session = await _service.CreateAsync(teacherId, request);
            return Ok(new { data = session, success = true });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, error = ex.Message }); }
    }

    /// <summary>Sessions du professeur connecté (calendrier hebdomadaire, US-SES-02).</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var teacherId = User.GetUserId();
        var sessions = await _service.GetTeacherSessionsAsync(teacherId, from, to);
        return Ok(new { data = sessions, success = true });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var session = await _service.GetByIdAsync(id, User.GetUserId());
        return session == null ? NotFound(new { success = false, error = "Session introuvable." }) : Ok(new { data = session, success = true });
    }

    /// <summary>Annulation avec notification et remboursement automatiques (US-SES-03).</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, [FromBody] CancelSessionRequestDto request)
    {
        try
        {
            var teacherId = User.GetUserId();
            var session = await _service.CancelAsync(teacherId, id, request);
            return Ok(new { data = session, success = true });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, error = ex.Message }); }
    }

    [HttpPost("{id:int}/enroll")]
    public async Task<IActionResult> Enroll(int id, [FromBody] EnrollSessionRequestDto request)
    {
        try
        {
            var studentId = User.GetUserId();
            var session = await _service.EnrollAsync(studentId, id, request);
            return Ok(new { data = session, success = true });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { success = false, error = ex.Message }); }
    }

    /// <summary>
    /// WinAI résume la transcription collée par le professeur (US-SES-04).
    /// Pas de capture audio/vidéo dans ce projet : la transcription arrive en
    /// texte (copié depuis l'outil de visio externe, ou saisie de mémoire),
    /// WinAI la structure plutôt que de "transcrire" un flux qu'elle n'a pas.
    /// </summary>
    [HttpPost("{id:int}/summarize")]
    public async Task<IActionResult> Summarize(int id, [FromBody] GenerateSessionSummaryRequestDto request, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("FastApiClient");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/teacher/session-summary")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { transcript_text = request.TranscriptText })
        };
        var auth = HttpContext.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(auth)) req.Headers.TryAddWithoutValidation("Authorization", auth);
        var res = await client.SendAsync(req, ct);
        return Content(await res.Content.ReadAsStringAsync(ct), "application/json");
    }

    /// <summary>Enregistre le résumé édité par le professeur avant envoi.</summary>
    [HttpPut("{id:int}/summary")]
    public async Task<IActionResult> UpdateSummary(int id, [FromBody] UpdateSessionSummaryRequestDto request)
    {
        try
        {
            var teacherId = User.GetUserId();
            await _service.SetSummaryAsync(teacherId, id, request.SummaryText);
            return Ok(new { success = true });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { success = false, error = ex.Message }); }
    }
}
