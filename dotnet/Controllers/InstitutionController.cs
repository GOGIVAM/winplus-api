using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Controller pour la gestion des institutions
/// </summary>
[ApiController]
[Route("api/institutions")]
[Produces("application/json")]
public class InstitutionController : ControllerBase
{
    private readonly IInstitutionService _institutionService;
    private readonly ILogger<InstitutionController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public InstitutionController(
        IInstitutionService institutionService,
        ILogger<InstitutionController> logger,
        IHttpClientFactory httpClientFactory)
    {
        _institutionService = institutionService ?? throw new ArgumentNullException(nameof(institutionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory;
    }

    private HttpClient PyClient() => _httpClientFactory.CreateClient("FastApiClient");

    /// <summary>
    /// Lot 9.7b, étape 4 (étude 2.3) : les fonctions IA d'un compte
    /// établissement sont réservées aux établissements sous licence et
    /// prélevées sur le budget WinAI de la licence (20 000 tokens par siège et
    /// par an). Renvoie la réponse de refus, ou null si l'appel peut partir.
    /// </summary>
    private async Task<(IActionResult? Refusal, int? InstitutionUserId)> InstitutionAiGateAsync()
    {
        var db = HttpContext.RequestServices.GetRequiredService<Backend.Data.ApplicationDbContext>();
        int userId;
        try { userId = User.GetUserId(); } catch { return (null, null); }
        var me = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.Role, u.InstitutionId }).FirstOrDefaultAsync();
        if (me == null || !string.Equals(me.Role, "institution", StringComparison.OrdinalIgnoreCase) || me.InstitutionId is not int iid)
            return (null, null);
        var licenses = HttpContext.RequestServices.GetRequiredService<Backend.Services.Entitlements.InstitutionLicenseService>();
        var (budget, used) = await licenses.AiBudgetAsync(iid);
        if (budget <= 0)
            return (StatusCode(403, new { success = false, code = "license_required", error = "Les analyses WinAI de l'établissement sont incluses dans la licence WinPlus. Souscrivez une licence (ou un essai) pour les utiliser." }), null);
        if (used >= budget)
            return (StatusCode(402, new { success = false, code = "institution_ai_budget_exhausted", error = "Le budget WinAI de votre licence est épuisé pour cette période." }), null);
        return (null, userId);
    }

    private async Task RecordInstitutionAiAsync(int? institutionUserId, string feature, HttpResponseMessage response)
    {
        if (institutionUserId is not int uid) return;
        var tokens = Backend.Services.Entitlements.IAiFeatureGate.TokensOf(response);
        await HttpContext.RequestServices.GetRequiredService<Backend.Services.Entitlements.InstitutionLicenseService>()
            .RecordAiUsageAsync(uid, feature, tokens);
    }

    private void ForwardAuth(HttpClient client)
    {
        var auth = Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(auth))
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", auth);
    }

    // ── Endpoints READ existants ─────────────────────────────────────────────

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<Institution>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var institutions = await _institutionService.GetAllInstitutionsAsync();
            return Ok(new { data = institutions, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all institutions");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("by-country")]
    [ProducesResponseType(typeof(IEnumerable<Institution>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByCountry([FromQuery] string? country = null)
    {
        try
        {
            var institutions = string.IsNullOrWhiteSpace(country)
                ? await _institutionService.GetAllInstitutionsAsync()
                : await _institutionService.GetInstitutionsByCountryAsync(country);
            return Ok(new { data = institutions, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institutions by country {Country}", country);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(Institution), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var institution = await _institutionService.GetInstitutionByIdAsync(id);
            if (institution == null)
                return NotFound(new { success = false, error = "Institution not found" });
            return Ok(new { data = institution, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institution {InstitutionId}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    // ── AI proxy endpoints ────────────────────────────────────────────────────

    /// <summary>Analyse prédictive de réussite  proxy vers Python FastAPI</summary>
    [HttpPost("class-prediction")]
    [Authorize]
    public async Task<IActionResult> ClassPrediction([FromBody] JsonElement body)
    {
        try
        {
            var (refusal, instUser) = await InstitutionAiGateAsync();
            if (refusal != null) return refusal;
            var client = PyClient();
            ForwardAuth(client);
            var response = await client.PostAsJsonAsync("/api/institution/class-prediction", body);
            await RecordInstitutionAiAsync(instUser, "prediction", response);
            var content = await response.Content.ReadAsStringAsync();
            Response.ContentType = "application/json";
            return StatusCode((int)response.StatusCode, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying institution class-prediction");
            return StatusCode(500, new { success = false, error = "Service unavailable" });
        }
    }

    /// <summary>Benchmarking anonyme vs national  proxy vers Python FastAPI</summary>
    [HttpGet("{institutionId}/benchmark")]
    [Authorize]
    public async Task<IActionResult> Benchmark(int institutionId, [FromQuery] string? studentIds = null)
    {
        try
        {
            var (refusal, instUser) = await InstitutionAiGateAsync();
            if (refusal != null) return refusal;
            var client = PyClient();
            ForwardAuth(client);
            var url = $"/api/institution/benchmark/{institutionId}";
            if (!string.IsNullOrEmpty(studentIds)) url += $"?student_ids={Uri.EscapeDataString(studentIds)}";
            var response = await client.GetAsync(url);
            await RecordInstitutionAiAsync(instUser, "benchmark", response);
            var content = await response.Content.ReadAsStringAsync();
            Response.ContentType = "application/json";
            return StatusCode((int)response.StatusCode, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying institution benchmark {InstitutionId}", institutionId);
            return StatusCode(500, new { success = false, error = "Service unavailable" });
        }
    }

    /// <summary>Plan d'action institutionnel IA  proxy vers Python FastAPI</summary>
    [HttpPost("action-plan")]
    [Authorize]
    public async Task<IActionResult> ActionPlan([FromBody] JsonElement body)
    {
        try
        {
            var (refusal, instUser) = await InstitutionAiGateAsync();
            if (refusal != null) return refusal;
            var client = PyClient();
            ForwardAuth(client);
            var response = await client.PostAsJsonAsync("/api/institution/action-plan", body);
            await RecordInstitutionAiAsync(instUser, "action_plan", response);
            var content = await response.Content.ReadAsStringAsync();
            Response.ContentType = "application/json";
            return StatusCode((int)response.StatusCode, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying institution action-plan");
            return StatusCode(500, new { success = false, error = "Service unavailable" });
        }
    }

    /// <summary>Détection des étudiants à risque  proxy vers Python FastAPI</summary>
    [HttpGet("{institutionId}/at-risk-students")]
    [Authorize]
    public async Task<IActionResult> AtRiskStudents(int institutionId, [FromQuery] string? studentIds = null)
    {
        try
        {
            var (refusal, instUser) = await InstitutionAiGateAsync();
            if (refusal != null) return refusal;
            var client = PyClient();
            ForwardAuth(client);
            var url = $"/api/institution/at-risk-students/{institutionId}";
            if (!string.IsNullOrEmpty(studentIds)) url += $"?student_ids={Uri.EscapeDataString(studentIds)}";
            var response = await client.GetAsync(url);
            await RecordInstitutionAiAsync(instUser, "at_risk", response);
            var content = await response.Content.ReadAsStringAsync();
            Response.ContentType = "application/json";
            return StatusCode((int)response.StatusCode, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying institution at-risk-students {InstitutionId}", institutionId);
            return StatusCode(500, new { success = false, error = "Service unavailable" });
        }
    }
}
