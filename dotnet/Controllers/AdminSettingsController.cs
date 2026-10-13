using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Middlewares;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Écrans « Paramètres » et « WinAI › Configuration » (lot 9.7a, suivi 18.2) :
/// réglages persistés, typés et bornés, lus par le code qui les applique
/// (voir <see cref="PlatformSettingDefinitions"/>). Chaque modification est
/// inscrite au journal d'audit (avant / après).
///
///   GET /api/admin/settings          tous les réglages (valeur effective, défaut, auteur et date de modification)
///   PUT /api/admin/settings/{key}    { value }
///   GET /api/platform/status         public : maintenance et inscriptions ouvertes
/// </summary>
[ApiController]
[Produces("application/json")]
public class AdminSettingsController : ControllerBase
{
    private readonly IPlatformSettingsAdminService _admin;
    private readonly IPlatformSettings _settings;

    public AdminSettingsController(IPlatformSettingsAdminService admin, IPlatformSettings settings)
    {
        _admin = admin;
        _settings = settings;
    }

    public sealed record SettingUpdateRequest(string? Value);

    [HttpGet("api/admin/settings")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> List() => Ok(new { success = true, data = await _admin.ListAsync() });

    [HttpPut("api/admin/settings/{key}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(string key, [FromBody] SettingUpdateRequest request)
    {
        var (ok, error) = await _admin.UpdateAsync(key, request?.Value, User, HttpContext.Connection.RemoteIpAddress?.ToString());
        if (!ok) return BadRequest(new { success = false, error });
        return Ok(new { success = true, data = new { key, value = _settings.GetRaw(key) } });
    }

    /// <summary>État public, lu par le site au démarrage et après une réponse 503 « maintenance ».</summary>
    [HttpGet("api/platform/status")]
    [AllowAnonymous]
    public IActionResult Status()
    {
        var maintenance = _settings.GetBool(PlatformSettingDefinitions.MaintenanceMode);
        var message = _settings.GetString(PlatformSettingDefinitions.MaintenanceMessage);
        return Ok(new
        {
            maintenance,
            maintenanceMessage = maintenance ? (string.IsNullOrWhiteSpace(message) ? MaintenanceMiddleware.DefaultMessage : message) : null,
            signupEnabled = _settings.GetBool(PlatformSettingDefinitions.SignupEnabled),
            forumPostingEnabled = _settings.GetBool(PlatformSettingDefinitions.ForumPostingEnabled),
        });
    }
}
