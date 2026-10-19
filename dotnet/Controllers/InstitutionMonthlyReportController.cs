using Backend.Extensions;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Espace institution, phase 2 (étape 5) : rapport mensuel de direction.
/// Il part automatiquement le 1er de chaque mois (MonthlyInstitutionReportService) ;
/// POST /api/institution/monthly-report/send l'envoie tout de suite, pour le
/// mois en cours jusqu'à aujourd'hui, à l'adresse du compte.
/// </summary>
[ApiController]
[Route("api/institution/monthly-report")]
[Authorize]
public class InstitutionMonthlyReportController : ControllerBase
{
    private readonly MonthlyInstitutionReportService _reports;
    private readonly ILogger<InstitutionMonthlyReportController> _logger;

    public InstitutionMonthlyReportController(MonthlyInstitutionReportService reports, ILogger<InstitutionMonthlyReportController> logger)
    {
        _reports = reports;
        _logger = logger;
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendNow(CancellationToken ct)
    {
        if (User.GetUserRole() != "institution") return Forbid();
        try
        {
            var ok = await _reports.SendNowAsync(User.GetUserId(), ct);
            return ok ? Ok(new { success = true }) : BadRequest(new { error = "Aucune adresse e-mail sur ce compte." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Monthly report send-now failed");
            return StatusCode(500, new { success = false, error = "Le rapport n'a pas pu être envoyé. Réessayez dans un instant." });
        }
    }
}
