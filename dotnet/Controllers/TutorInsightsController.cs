using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Extensions;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// WinAI côté répétiteur (Module 6, professeur_complete.md) : fiche de
/// révision élève (US-REP-11) et rapport mensuel de coaching (US-REP-12).
/// Proxie la génération vers FastAPI (mêmes conventions que SessionsController)
/// et persiste le résultat côté .NET pour l'affichage tableau de bord.
/// </summary>
[ApiController]
[Route("api/tutor-insights")]
[Authorize]
[Produces("application/json")]
public class TutorInsightsController : ControllerBase
{
    private readonly ITutorInsightsService _insights;
    private readonly IHttpClientFactory _httpClientFactory;

    public TutorInsightsController(ITutorInsightsService insights, IHttpClientFactory httpClientFactory)
    {
        _insights = insights;
        _httpClientFactory = httpClientFactory;
    }

    public record SaveRevisionSheetRequest(string? Subject, string Content);

    /// <summary>Élèves ayant eu au moins une séance effectuée, pour choisir à qui générer une fiche.</summary>
    [HttpGet("students")]
    public async Task<IActionResult> GetStudents() => Ok(await _insights.GetStudentsWithHistoryAsync(User.GetUserId()));

    /// <summary>Fiche de révision déjà générée/enregistrée pour cet élève, si elle existe.</summary>
    [HttpGet("revision-sheet/{studentUserId:int}")]
    public async Task<IActionResult> GetRevisionSheet(int studentUserId)
    {
        var sheet = await _insights.GetRevisionSheetAsync(User.GetUserId(), studentUserId);
        return sheet == null ? NotFound() : Ok(sheet);
    }

    /// <summary>WinAI génère une fiche de révision à partir des comptes-rendus de séance disponibles.</summary>
    [HttpPost("revision-sheet/{studentUserId:int}/generate")]
    public async Task<IActionResult> GenerateRevisionSheet(int studentUserId, CancellationToken ct)
    {
        var tutorUserId = User.GetUserId();
        var (studentName, subject, summaries) = await _insights.GetStudentHistoryAsync(tutorUserId, studentUserId);
        if (summaries.Count == 0)
            return BadRequest(new { message = "Aucun compte-rendu de séance disponible pour cet élève. Génère d'abord un compte-rendu depuis l'onglet Cours particuliers." });

        var client = _httpClientFactory.CreateClient("FastApiClient");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/teacher/student-revision-sheet")
        {
            Content = JsonContent.Create(new { student_name = studentName, subject, session_summaries = summaries })
        };
        var auth = HttpContext.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(auth)) req.Headers.TryAddWithoutValidation("Authorization", auth);
        var res = await client.SendAsync(req, ct);
        Response.StatusCode = (int)res.StatusCode;
        return Content(await res.Content.ReadAsStringAsync(ct), "application/json");
    }

    /// <summary>Enregistre la fiche (générée puis éventuellement éditée par le répétiteur) avant partage.</summary>
    [HttpPut("revision-sheet/{studentUserId:int}")]
    public async Task<IActionResult> SaveRevisionSheet(int studentUserId, [FromBody] SaveRevisionSheetRequest request)
        => Ok(await _insights.SaveRevisionSheetAsync(User.GetUserId(), studentUserId, request.Subject, request.Content));

    /// <summary>Dernier rapport mensuel de coaching WinAI disponible.</summary>
    [HttpGet("coaching-report/latest")]
    public async Task<IActionResult> GetLatestCoachingReport()
    {
        var report = await _insights.GetLatestCoachingReportAsync(User.GetUserId());
        return report == null ? NotFound() : Ok(report);
    }

    /// <summary>Génère à la demande le rapport de coaching du mois en cours (le cycle automatique tourne le 1er de chaque mois).</summary>
    [HttpPost("coaching-report/generate-now")]
    public async Task<IActionResult> GenerateCoachingReportNow(CancellationToken ct)
    {
        var tutorUserId = User.GetUserId();
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var (monthLabel, reviews, subjects, sessionsCount, avgRating) =
            await _insights.GetCoachingSourceDataAsync(tutorUserId, monthStart, now);

        if (reviews.Count == 0 && sessionsCount == 0)
            return BadRequest(new { message = "Pas encore assez de séances ou d'avis ce mois-ci pour générer un rapport." });

        var client = _httpClientFactory.CreateClient("FastApiClient");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/teacher/coaching-report")
        {
            Content = JsonContent.Create(new
            {
                month_label = monthLabel,
                reviews,
                subjects_taught = subjects,
                sessions_count = sessionsCount,
                average_rating = avgRating,
            })
        };
        var auth = HttpContext.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(auth)) req.Headers.TryAddWithoutValidation("Authorization", auth);
        var res = await client.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            Response.StatusCode = (int)res.StatusCode;
            return Content(body, "application/json");
        }

        // Le corps complet (strengths, subjects_improving, recommendations…) est
        // persisté tel quel : le front reconstruit l'affichage structuré depuis
        // ce JSON plutôt que depuis un seul champ texte.
        var saved = await _insights.SaveCoachingReportAsync(tutorUserId, monthLabel, body);
        return Ok(saved);
    }
}
