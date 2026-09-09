using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Module 1 — Onboarding et profil répétiteur (professeur_complete.md).
/// Gère le profil "Mode Répétiteur" d'un compte Professeur : matières,
/// niveaux, tarifs, disponibilités, vérification de diplôme. Distinct du
/// mode "Professeur Catalogue" (TeacherController) ; les deux coexistent sur
/// le même compte.
/// </summary>
[ApiController]
[Route("api/tutor-profile")]
[Authorize]
[Produces("application/json")]
public class TutorProfileController : ControllerBase
{
    private readonly ITutorProfileService _service;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IStorageService _storage;
    private readonly ILogger<TutorProfileController> _logger;

    private static readonly string[] DocumentExtensions = { ".pdf", ".jpg", ".jpeg", ".png", ".webp" };
    private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mov", ".m4v" };
    private const long MaxDocumentBytes = 10 * 1024 * 1024;
    private const long MaxVideoBytes = 25 * 1024 * 1024;

    public TutorProfileController(
        ITutorProfileService service,
        IHttpClientFactory httpClientFactory,
        IStorageService storage,
        ILogger<TutorProfileController> logger)
    {
        _service = service;
        _httpClientFactory = httpClientFactory;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Téléversement direct du diplôme/relevé (PDF ou photo scannée). Avant ceci,
    /// le champ "diplôme" du step 5 exigeait de coller l'URL d'un document déjà
    /// hébergé ailleurs — inutilisable en pratique (TC-PRO-06/07/08).
    /// </summary>
    [HttpPost("uploads/document")]
    [RequestSizeLimit(MaxDocumentBytes + 1024)]
    public async Task<IActionResult> UploadDocument([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Fichier manquant." });
        if (file.Length > MaxDocumentBytes)
            return BadRequest(new { message = "Fichier trop volumineux (10 Mo maximum)." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!DocumentExtensions.Contains(ext))
            return BadRequest(new { message = "Format non supporté (PDF, JPG, PNG ou WEBP)." });

        try
        {
            var key = $"tutor-documents/{User.GetUserId()}/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{ext}";
            await using var stream = file.OpenReadStream();
            var url = await _storage.PutAsync(stream, key, file.ContentType, HttpContext.RequestAborted);
            return Ok(new { url });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tutor document upload failed for user {UserId}", User.GetUserId());
            return StatusCode(500, new { message = "Téléversement impossible pour l'instant." });
        }
    }

    /// <summary>Téléversement direct d'une courte vidéo de présentation (alternative au lien YouTube/Vimeo).</summary>
    [HttpPost("uploads/video")]
    [RequestSizeLimit(MaxVideoBytes + 1024)]
    public async Task<IActionResult> UploadVideo([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Fichier manquant." });
        if (file.Length > MaxVideoBytes)
            return BadRequest(new { message = "Vidéo trop volumineuse (25 Mo maximum  environ 1 minute en bonne qualité)." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!VideoExtensions.Contains(ext))
            return BadRequest(new { message = "Format non supporté (MP4, WEBM ou MOV)." });

        try
        {
            var key = $"tutor-videos/{User.GetUserId()}/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{ext}";
            await using var stream = file.OpenReadStream();
            var url = await _storage.PutAsync(stream, key, file.ContentType, HttpContext.RequestAborted);
            return Ok(new { url });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tutor video upload failed for user {UserId}", User.GetUserId());
            return StatusCode(500, new { message = "Téléversement impossible pour l'instant." });
        }
    }

    private async Task<IActionResult> ProxyToWinAI(string pythonPath, object body, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("FastApiClient");
        using var req = new HttpRequestMessage(HttpMethod.Post, pythonPath) { Content = JsonContent.Create(body) };
        var auth = HttpContext.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(auth)) req.Headers.TryAddWithoutValidation("Authorization", auth);
        var res = await client.SendAsync(req, ct);
        return Content(await res.Content.ReadAsStringAsync(ct), "application/json");
    }

    /// <summary>Mon profil répétiteur (créé automatiquement au premier accès, US-PRO-05).</summary>
    [HttpGet("me")]
    public async Task<ActionResult<TutorProfileDto>> GetMine()
    {
        try
        {
            return Ok(await _service.GetOrCreateAsync(User.GetUserId()));
        }
        catch (InvalidOperationException ex)
        {
            return Forbid(ex.Message);
        }
    }

    /// <summary>
    /// État du mode Répétiteur sans jamais créer de profil (à la différence de GET me).
    /// Utilisé par le bandeau de rappel du dashboard Professeur : n'affiche rien pour
    /// un prof qui n'a jamais ouvert les paramètres "Mode Répétiteur" (opt-in, US-PRO-05).
    /// </summary>
    [HttpGet("me/status")]
    public async Task<ActionResult<TutorOnboardingStatusDto>> GetStatus()
        => Ok(await _service.GetStatusAsync(User.GetUserId()));

    /// <summary>Sauvegarde partielle d'une étape de l'onboarding ou d'un champ modifié depuis les paramètres.</summary>
    [HttpPut("me")]
    public async Task<ActionResult<TutorProfileDto>> UpdateMine([FromBody] UpdateTutorProfileRequestDto request)
    {
        try
        {
            return Ok(await _service.UpdateAsync(User.GetUserId(), request));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Grille de disponibilités : sauvegarde instantanée à chaque toggle,
    /// indépendante du reste du profil (US-PRO-08). Renvoie 400 si le
    /// nombre de créneaux actifs dépasse le max séances/semaine configuré.
    /// </summary>
    [HttpPut("me/availability")]
    public async Task<ActionResult<TutorProfileDto>> UpdateAvailability([FromBody] UpdateTutorAvailabilityRequestDto request)
    {
        try
        {
            return Ok(await _service.UpdateAvailabilityAsync(User.GetUserId(), request.Slots));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Score de complétude + éléments manquants (US-PRO-01, US-PRO-04).</summary>
    [HttpGet("me/completion")]
    public async Task<ActionResult<TutorProfileCompletionDto>> GetCompletion()
        => Ok(await _service.GetCompletionAsync(User.GetUserId()));

    /// <summary>Active le profil (visible dans la recherche élève) — fin du Workflow 1.</summary>
    [HttpPost("me/activate")]
    public async Task<ActionResult<TutorProfileDto>> Activate()
    {
        try
        {
            return Ok(await _service.ActivateAsync(User.GetUserId()));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Statut "En vacances" (US-PRO-09) : true = suspend les réservations sans dépublier le profil.</summary>
    [HttpPost("me/vacation")]
    public async Task<ActionResult<TutorProfileDto>> SetVacation([FromBody] bool isOnVacation)
        => Ok(await _service.SetVacationAsync(User.GetUserId(), isOnVacation));

    /// <summary>
    /// Dépose un diplôme/relevé pour validation admin (US-PRO-03). L'upload
    /// du fichier lui-même passe par le service de stockage existant
    /// (S3, comme les épreuves) ; ce endpoint n'enregistre que l'URL obtenue.
    /// </summary>
    [HttpPost("me/verification-document")]
    public async Task<ActionResult<TutorVerificationDocumentDto>> SubmitVerificationDocument([FromBody] UploadTutorVerificationDocumentRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentUrl))
            return BadRequest(new { message = "URL du document requise." });
        try
        {
            return Ok(await _service.SubmitVerificationDocumentAsync(User.GetUserId(), request.DocumentUrl));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Fiche publique d'un répétiteur (US-10, côté élève) — 404 si non actif.</summary>
    [HttpGet("{userId:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<TutorProfileDto>> GetPublic(int userId)
    {
        var profile = await _service.GetPublicProfileAsync(userId);
        return profile == null ? NotFound(new { message = "Profil répétiteur introuvable ou non actif." }) : Ok(profile);
    }

    /// <summary>WinAI — suggestion de tarif horaire (US-PRO-05/US-PRO-07).</summary>
    [HttpPost("me/suggest-rate")]
    public Task<IActionResult> SuggestRate([FromBody] object body, CancellationToken ct)
        => ProxyToWinAI("/api/teacher/suggest-tutor-rate", body, ct);

    /// <summary>WinAI — analyse et suggestions d'optimisation du profil (US-PRO-04).</summary>
    [HttpPost("me/ai-analysis")]
    public Task<IActionResult> AnalyzeProfile([FromBody] object body, CancellationToken ct)
        => ProxyToWinAI("/api/teacher/analyze-tutor-profile", body, ct);

    /// <summary>Moteur de recherche côté élève (Module B du référentiel).</summary>
    [HttpGet("search")]
    [AllowAnonymous]
    public async Task<ActionResult<List<TutorSearchResultDto>>> Search(
        [FromQuery] string? subject,
        [FromQuery] string? level,
        [FromQuery] decimal? maxHourlyRateXaf,
        [FromQuery] bool verifiedOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? mode = null,
        [FromQuery] string? city = null,
        [FromQuery] bool availableSoon = false,
        [FromQuery] double? minRating = null,
        [FromQuery] string? sort = null)
        => Ok(await _service.SearchAsync(subject, level, maxHourlyRateXaf, verifiedOnly, page, pageSize, mode, city, availableSoon, minRating, sort));
}
