using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Lot 9.6 (suivi 17.5) : support de séance généré par WinAI (cours, quiz,
/// exercices) pour une séance de tutorat. Le tuteur génère (débit de SES
/// tokens, droit ai.feature.session_material), relance, modifie et partage ;
/// les élèves participants consultent et répondent ; le payeur consulte.
/// Tout est affiché dans l'application : aucune route de téléchargement.
/// </summary>
[ApiController]
[Route("api/tutor-bookings")]
[Authorize]
[Produces("application/json")]
public class TutorSessionMaterialController : ControllerBase
{
    private readonly ITutorSessionMaterialService _service;

    public TutorSessionMaterialController(ITutorSessionMaterialService service) => _service = service;

    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Ok(await action()); }
        catch (TutorSessionMaterialForbiddenException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (TutorSessionMaterialGenerationException ex) { return StatusCode(502, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Support de la séance vu par le compte connecté (tuteur, participant ou payeur).</summary>
    [HttpGet("{id:int}/material")]
    public Task<IActionResult> Get(int id) => Run(async () => await _service.GetAsync(User.GetUserId(), id));

    /// <summary>Le tuteur génère ou relance la génération (copie de travail, non partagée).</summary>
    [HttpPost("{id:int}/material/generate")]
    public async Task<IActionResult> Generate(int id, [FromBody] GenerateTutorSessionMaterialRequestDto? request, CancellationToken ct)
    {
        try
        {
            var (refusal, view) = await _service.GenerateAsync(User.GetUserId(), id, request?.Instructions,
                HttpContext.Request.Headers["Authorization"].ToString(), automatic: false, ct);
            return refusal ?? Ok(view);
        }
        catch (TutorSessionMaterialForbiddenException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (TutorSessionMaterialGenerationException ex) { return StatusCode(502, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Le tuteur enregistre ses modifications de la copie de travail.</summary>
    [HttpPut("{id:int}/material")]
    public Task<IActionResult> Update(int id, [FromBody] UpdateTutorSessionMaterialRequestDto request) =>
        Run(async () => await _service.UpdateAsync(User.GetUserId(), id, request.Content));

    /// <summary>Le tuteur partage la copie de travail avec les participants et le payeur.</summary>
    [HttpPost("{id:int}/material/share")]
    public Task<IActionResult> Share(int id) => Run(async () => await _service.ShareAsync(User.GetUserId(), id));

    /// <summary>Un élève participant envoie ses réponses (quiz noté, exercices transmis au tuteur).</summary>
    [HttpPost("{id:int}/material/submit")]
    public Task<IActionResult> Submit(int id, [FromBody] SubmitTutorSessionMaterialRequestDto request) =>
        Run(async () => await _service.SubmitAsync(User.GetUserId(), id, request));

    /// <summary>Réglage du tuteur : génération automatique à l'acceptation d'une séance (désactivée par défaut).</summary>
    [HttpGet("session-material/settings")]
    public Task<IActionResult> GetSettings() => Run(async () => await _service.GetSettingsAsync(User.GetUserId()));

    [HttpPut("session-material/settings")]
    public Task<IActionResult> SetSettings([FromBody] TutorSessionMaterialSettingsDto request) =>
        Run(async () => await _service.SetSettingsAsync(User.GetUserId(), request.AutoGenerateOnAccept));
}
