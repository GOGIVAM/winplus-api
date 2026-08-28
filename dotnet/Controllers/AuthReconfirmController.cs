using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;

namespace Backend.Controllers;

/// <summary>
/// Statut de reconfirmation périodique (S6-2).
/// L'envoi du code et sa validation existent déjà :
///   POST /api/auth/send-confirmation-code
///   POST /api/auth/verify-confirmation
/// Il manquait le point d'entrée qui dit à l'app s'il faut demander la
/// reconfirmation — sinon l'écran ne peut jamais s'afficher.
///
/// GET /api/auth/reconfirmation-status
/// </summary>
[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthReconfirmController : ControllerBase
{
    /// <summary>Fenêtre de reconfirmation, en jours.</summary>
    private const int ReconfirmAfterDays = 35;

    private readonly ApplicationDbContext _db;
    private readonly ILogger<AuthReconfirmController> _logger;

    public AuthReconfirmController(ApplicationDbContext db, ILogger<AuthReconfirmController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("reconfirmation-status")]
    public async Task<IActionResult> GetStatus()
    {
        try
        {
            var userId = User.GetUserId();

            var user = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Email, u.LastPeriodicConfirmAt, u.CreatedAt })
                .FirstOrDefaultAsync();

            if (user == null) return Unauthorized(new { success = false, error = "Utilisateur introuvable." });

            var reference = user.LastPeriodicConfirmAt ?? user.CreatedAt;
            var daysSince = (int)(DateTime.UtcNow - reference).TotalDays;
            var required  = daysSince >= ReconfirmAfterDays;

            // Email masqué : m***@example.com
            var at = user.Email.IndexOf('@');
            var masked = at > 1
                ? user.Email[0] + new string('*', Math.Max(1, at - 1)) + user.Email[at..]
                : user.Email;

            return Ok(new
            {
                data = new
                {
                    required,
                    daysSinceLastConfirm = daysSince,
                    windowDays           = ReconfirmAfterDays,
                    lastConfirmedAt      = user.LastPeriodicConfirmAt,
                    maskedEmail          = masked
                },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting reconfirmation status");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
