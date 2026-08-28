using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Alias de compatibilité pour deux routes appelées par le frontend sans le
/// préfixe /ai :
///
///   GET /api/success-prediction/{userId}  →  /api/ai/success-prediction/{userId}
///   GET /api/learning-path/{userId}       →  /api/ai/personalized-path (POST)
///
/// Ces deux appels renvoyaient 404 en production : les endpoints existent bien,
/// mais sous /api/ai/. Plutôt que de modifier le frontend déjà déployé, on
/// expose les routes qu'il utilise réellement.
///
/// Deux différences de contrat volontaires par rapport à AIController :
///  • le userId de la route est ignoré s'il ne correspond pas à l'utilisateur
///    connecté (un élève ne doit pas lire la prédiction d'un autre) ;
///  • quand le service IA ne répond pas, on renvoie 204 No Content et non une
///    erreur : ces deux blocs sont des enrichissements d'écran, leur absence ne
///    doit pas faire échouer le tableau de bord.
/// </summary>
[ApiController]
[Authorize]
public class AiAliasController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAIService _aiService;
    private readonly ILogger<AiAliasController> _logger;

    public AiAliasController(
        IHttpClientFactory httpClientFactory,
        IAIService aiService,
        ILogger<AiAliasController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _aiService = aiService;
        _logger = logger;
    }

    /// <summary>
    /// Vérifie que l'utilisateur demande bien ses propres données.
    /// Un administrateur peut consulter celles de n'importe qui.
    /// </summary>
    private bool CanRead(int requestedUserId, out int effectiveUserId)
    {
        effectiveUserId = 0;
        try
        {
            var me = User.GetUserId();
            if (me == requestedUserId || User.IsAdmin())
            {
                effectiveUserId = requestedUserId;
                return true;
            }
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// GET /api/success-prediction/{userId}
    /// </summary>
    [HttpGet("api/success-prediction/{userId:int}")]
    public async Task<IActionResult> GetSuccessPrediction([FromRoute] int userId, CancellationToken ct)
    {
        if (!CanRead(userId, out var effectiveUserId))
            return StatusCode(403, new { success = false, error = "Accès refusé à ces données." });

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var httpClient = _httpClientFactory.CreateClient("FastApiClient");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/success-prediction/{effectiveUserId}");

            if (Request.Headers.TryGetValue("Authorization", out var auth))
                req.Headers.TryAddWithoutValidation("Authorization", (string?)auth);

            var res = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var content = await res.Content.ReadAsStringAsync(cts.Token);

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("FastAPI success-prediction a renvoyé {Status} pour {UserId}", res.StatusCode, effectiveUserId);
                return NoContent();
            }

            return Content(content, "application/json");
        }
        catch (OperationCanceledException)
        {
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur success-prediction pour {UserId}", effectiveUserId);
            return NoContent();
        }
    }

    /// <summary>
    /// GET /api/learning-path/{userId}?subject=…&amp;weeks=8&amp;hoursPerWeek=10
    ///
    /// Le frontend appelle cette route en GET ; AIController n'expose qu'un POST
    /// /api/ai/personalized-path. On accepte les paramètres en query string, avec
    /// les mêmes valeurs par défaut que le DTO (8 semaines, 10 h/semaine).
    /// </summary>
    [HttpGet("api/learning-path/{userId:int}")]
    public async Task<IActionResult> GetLearningPath(
        [FromRoute] int userId,
        [FromQuery] string? subject = null,
        [FromQuery] int weeks = 8,
        [FromQuery] int hoursPerWeek = 10)
    {
        if (!CanRead(userId, out var effectiveUserId))
            return StatusCode(403, new { success = false, error = "Accès refusé à ces données." });

        if (weeks is < 1 or > 52) weeks = 8;
        if (hoursPerWeek is < 1 or > 168) hoursPerWeek = 10;

        try
        {
            var response = await _aiService.GeneratePersonalizedPathAsync(
                effectiveUserId,
                string.IsNullOrWhiteSpace(subject) ? "general" : subject.Trim(),
                weeks,
                hoursPerWeek);

            // Un parcours sans semaine n'apporte rien à l'écran : on préfère un
            // 204 explicite, que le frontend traite comme « pas encore de plan ».
            if (response?.Weeks == null || response.Weeks.Count == 0)
                return NoContent();

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur learning-path pour {UserId}", effectiveUserId);
            return NoContent();
        }
    }
}
