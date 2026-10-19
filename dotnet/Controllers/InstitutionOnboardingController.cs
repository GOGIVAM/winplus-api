using Backend.Data;
using Backend.Extensions;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Espace institution, phase 2 (étape 7) : onboarding de l'établissement.
///
/// GET /api/institution/onboarding   profil actuel (rattachement, type, cycles…)
/// PUT /api/institution/onboarding   enregistrer le profil
///
/// Compte rattaché : le profil de l'établissement est mis à jour.
/// Compte non rattaché : un établissement ne peut pas être « réclamé » par
/// n'importe quel compte ; la demande part à l'équipe WinPlus (alerte admin)
/// avec les informations saisies, pour création et rattachement.
/// </summary>
[ApiController]
[Route("api/institution/onboarding")]
[Authorize]
public class InstitutionOnboardingController : ControllerBase
{
    private static readonly string[] Types = { "ecole_primaire", "college", "lycee", "lycee_technique", "universite", "grande_ecole", "centre_formation", "centre_concours" };
    private static readonly string[] CycleValues = { "primaire", "secondaire", "superieur", "concours" };
    private static readonly string[] GoalValues = { "suivi", "examens", "devoirs", "parents", "enseignants", "orientation" };

    private readonly ApplicationDbContext _db;
    private readonly INtfyService _ntfy;
    private readonly ILogger<InstitutionOnboardingController> _logger;

    public InstitutionOnboardingController(ApplicationDbContext db, INtfyService ntfy, ILogger<InstitutionOnboardingController> logger)
    {
        _db = db;
        _ntfy = ntfy;
        _logger = logger;
    }

    public record OnboardingRequest(string Type, List<string>? Cycles, string? Subsystem, string? Teaching,
        int? StudentsEstimate, List<string>? Goals, string? Name, string? City, string? Phone);

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (User.GetUserRole() != "institution") return Forbid();
        var me = User.GetUserId();
        var institutionId = await _db.Users.Where(u => u.Id == me).Select(u => u.InstitutionId).FirstOrDefaultAsync();
        var inst = institutionId == null ? null : await _db.Institutions.AsNoTracking()
            .Where(i => i.Id == institutionId && !i.IsDeleted)
            .Select(i => new { i.Id, i.Name, i.City, i.Type, i.Cycles, i.Subsystem, i.Teaching, i.StudentsEstimate, i.Goals, i.OnboardedAt })
            .FirstOrDefaultAsync();
        return Ok(new { data = new { attached = inst != null, institution = inst }, success = true });
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] OnboardingRequest req)
    {
        try
        {
            if (User.GetUserRole() != "institution") return Forbid();
            var me = User.GetUserId();
            if (!Types.Contains(req.Type)) return BadRequest(new { error = "Type d'établissement invalide." });
            var cycles = (req.Cycles ?? new()).Where(c => CycleValues.Contains(c)).Distinct().ToList();
            if (cycles.Count == 0) return BadRequest(new { error = "Choisissez au moins un cycle." });
            var subsystem = req.Subsystem is "fr" or "en" or "bilingue" ? req.Subsystem : null;
            var teaching = req.Teaching is "general" or "technique" or "both" ? req.Teaching : null;
            var goals = (req.Goals ?? new()).Where(g => GoalValues.Contains(g)).Distinct().ToList();
            var estimate = req.StudentsEstimate is > 0 and < 100000 ? req.StudentsEstimate : null;

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == me);
            if (user == null) return NotFound();
            var inst = user.InstitutionId == null ? null : await _db.Institutions.FirstOrDefaultAsync(i => i.Id == user.InstitutionId && !i.IsDeleted);

            if (inst != null)
            {
                inst.Type = req.Type;
                inst.Cycles = string.Join(',', cycles);
                inst.Subsystem = subsystem;
                inst.Teaching = teaching;
                inst.StudentsEstimate = estimate;
                inst.Goals = string.Join(',', goals);
                if (!string.IsNullOrWhiteSpace(req.City)) inst.City = req.City.Trim();
                if (!string.IsNullOrWhiteSpace(req.Phone) && string.IsNullOrWhiteSpace(inst.Phone)) inst.Phone = req.Phone.Trim();
                inst.OnboardedAt ??= DateTime.UtcNow;
                inst.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return Ok(new { data = new { attached = true }, success = true });
            }

            // Non rattaché : demande à l'équipe WinPlus.
            var name = (req.Name ?? string.Empty).Trim();
            if (name.Length < 3) return BadRequest(new { error = "Indiquez le nom de votre établissement." });
            var message = $"{name} ({req.City?.Trim() ?? "ville non précisée"}) · {req.Type} · cycles : {string.Join(", ", cycles)}"
                + (subsystem != null ? $" · {subsystem}" : "") + (estimate != null ? $" · ~{estimate} élèves" : "")
                + $" · compte {user.Email}" + (string.IsNullOrWhiteSpace(req.Phone) ? "" : $" · tél. {req.Phone.Trim()}");
            await _ntfy.PublishAdminAsync("Établissement à rattacher", message, priority: "high",
                type: "InstitutionOnboarding", relatedEntityType: "User", relatedEntityId: me);
            return Ok(new { data = new { attached = false }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving institution onboarding");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
