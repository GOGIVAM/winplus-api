using Microsoft.AspNetCore.Mvc;
using Backend.Models.Entities;
using Backend.Services.Entitlements;

namespace Backend.Controllers;

/// <summary>
/// Tarification publique (lot 9.7b). Tout est servi depuis la grille versionnée
/// (PlanVersions, PlanPrices, PlanEntitlements) : prix TTC par période et
/// uniquement les droits réellement appliqués par le serveur. Les anciens
/// endpoints fondés sur les colonnes historiques de PricingPlans (prix unique,
/// fonctionnalités en texte libre, /compare, /plans/{id}) et sur l'entité
/// Promotion (supprimée) ont été retirés.
/// </summary>
[ApiController]
[Route("api/pricing")]
[Produces("application/json")]
public class PricingController : ControllerBase
{
    private readonly PlanCatalogService _plans;

    public PricingController(PlanCatalogService plans) => _plans = plans;

    private static string? AudienceOf(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "students" or "student" or "eleve" => PlanAudiences.Student,
        "parents" or "parent" => PlanAudiences.Parent,
        "teachers" or "teacher" or "professeur" => PlanAudiences.Teacher,
        _ => "invalid",
    };

    /// <summary>
    /// GET /api/pricing/catalog?audience=student : plans en vente par public,
    /// version publiée, prix TTC par période, multiplicateur WinAI et droits.
    /// Sans public : les trois publics.
    /// </summary>
    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog([FromQuery] string? audience = null)
    {
        var a = AudienceOf(audience);
        if (a == "invalid") return BadRequest(new { error = "Public invalide (student, parent ou teacher)." });
        var audiences = a == null ? PlanAudiences.All : new[] { a };
        var result = new Dictionary<string, object>();
        foreach (var x in audiences)
            result[x] = await _plans.PublicCardsAsync(x, HttpContext.RequestAborted);
        return Ok(new { data = result, periods = BillingPeriods.All });
    }

    /// <summary>
    /// GET /api/pricing/licenses : grille publique des licences d'établissement
    /// (lot 9.7b étape 4, décision 18.21), prix TTC par élève et par an.
    /// </summary>
    [HttpGet("licenses")]
    public IActionResult GetLicenseGrid() => Ok(new
    {
        minSeats = LicensePolicy.MinStudentSeats,
        studentsPerTeacherSeat = LicensePolicy.StudentsPerTeacherSeat,
        minTeacherSeats = LicensePolicy.MinTeacherSeats,
        tiers = new[]
        {
            new { tier = LicenseTiers.School, label = "École", fromSeats = 20, toSeats = (int?)199, yearlyPerSeatTtc = LicensePolicy.YearlyPricePerSeat(20) },
            new { tier = LicenseTiers.Establishment, label = "Établissement", fromSeats = 200, toSeats = (int?)999, yearlyPerSeatTtc = LicensePolicy.YearlyPricePerSeat(200) },
            new { tier = LicenseTiers.Network, label = "Réseau", fromSeats = 1000, toSeats = (int?)null, yearlyPerSeatTtc = LicensePolicy.YearlyPricePerSeat(1000) },
        },
        studentRights = new[] { "Catalogue complet en lecture", "Plan d'étude et session guidée", "Exam Coach", "Bonus d'usage WinAI ajouté au plan de l'élève" },
        teacherRights = new[] { "Outils du plan Pro (10 classes de 100 élèves)", "Génération de quiz et correction assistée", "Part de 75 % sur le tutorat si son plan est inférieur" },
        institutionRights = new[] { "Tableau de bord, élèves à risque, plan d'action et prévisions WinAI", "Rapports dans l'application et export CSV" },
        trialDays = LicensePolicy.DefaultTrialDays,
        trialSeats = LicensePolicy.TrialSeats,
    });

    /// <summary>
    /// GET /api/pricing/plans?audience=student (ou category=students, forme
    /// historique) : liste plate des cartes d'un public.
    /// </summary>
    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans([FromQuery] string? audience = null, [FromQuery] string? category = null)
    {
        var a = AudienceOf(audience ?? category);
        if (a == "invalid") return BadRequest(new { error = "Public invalide (student, parent ou teacher)." });
        var list = new List<PlanCardDto>();
        foreach (var x in a == null ? PlanAudiences.All : new[] { a })
            list.AddRange(await _plans.PublicCardsAsync(x, HttpContext.RequestAborted));
        return Ok(new { data = list, success = true });
    }
}
