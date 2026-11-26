using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Backend.Data;
using Backend.Extensions;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Fiche de l'établissement, relue et modifiable après l'inscription (26/11/2026).
///   GET /api/institution/profile
///   PUT /api/institution/profile   champs facultatifs : seuls ceux envoyés sont modifiés
/// Compte « institution » rattaché à un établissement seulement (403 role_institution, 404 etablissement_non_rattache).
/// Le nom n'est pas modifié directement : la demande part à l'équipe WinPlus (alerte), pour qu'un compte ne
/// puisse pas prendre le nom d'un autre établissement ; la réponse indique nameChangeRequested.
/// Le logo est la photo du compte établissement (POST /api/users/profile/avatar), renvoyée en logoUrl.
/// Refus : 400 {success:false, code, error, message}.
/// </summary>
[ApiController]
[Route("api/institution/profile")]
[Authorize]
public class InstitutionProfileController : ControllerBase
{
    private static readonly string[] Types = { "ecole_primaire", "college", "lycee", "lycee_technique", "universite", "grande_ecole", "centre_formation", "centre_concours" };
    private static readonly string[] CycleValues = { "primaire", "secondaire", "superieur", "concours" };
    private static readonly string[] GoalValues = { "suivi", "examens", "devoirs", "parents", "enseignants", "orientation" };
    private static readonly Regex PhonePattern = new(@"^\+?[0-9][0-9 .\-]{5,19}$", RegexOptions.Compiled);

    private readonly ApplicationDbContext _db;
    private readonly INtfyService _ntfy;
    private readonly ILogger<InstitutionProfileController> _logger;

    public InstitutionProfileController(ApplicationDbContext db, INtfyService ntfy, ILogger<InstitutionProfileController> logger)
    {
        _db = db; _ntfy = ntfy; _logger = logger;
    }

    public sealed class UpdateInstitutionProfileRequest
    {
        public string? Name { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Type { get; set; }
        public List<string>? Cycles { get; set; }
        /// <summary>fr | en | bilingue</summary>
        public string? Subsystem { get; set; }
        /// <summary>general | technique | both</summary>
        public string? Teaching { get; set; }
        public int? StudentsEstimate { get; set; }
        public List<string>? Goals { get; set; }
    }

    private static ObjectResult Refus(int status, string code, string message) =>
        new(new { success = false, code, error = message, message }) { StatusCode = status };

    private async Task<(Models.Entities.User? User, Models.Entities.Institution? Inst, IActionResult? Error)> LoadAsync(bool track)
    {
        if (User.GetUserRole() != "institution")
            return (null, null, Refus(403, "role_institution", "Cette fiche est réservée au compte établissement."));
        var me = User.GetUserId();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == me);
        if (user?.InstitutionId == null)
            return (user, null, Refus(404, "etablissement_non_rattache", "Votre compte n'est pas encore rattaché à un établissement : terminez l'inscription de l'établissement."));
        var q = _db.Institutions.Where(i => i.Id == user.InstitutionId && !i.IsDeleted);
        var inst = track ? await q.FirstOrDefaultAsync() : await q.AsNoTracking().FirstOrDefaultAsync();
        return inst == null
            ? (user, null, Refus(404, "etablissement_non_rattache", "Établissement introuvable."))
            : (user, inst, null);
    }

    private static object View(Models.Entities.Institution i, Models.Entities.User u) => new
    {
        i.Id, i.Name, i.Code, i.Country, i.City, i.Region, i.Address, i.Phone, i.Email, i.Type,
        cycles = Split(i.Cycles), i.Subsystem, i.Teaching, i.StudentsEstimate, goals = Split(i.Goals),
        logoUrl = u.AvatarUrl ?? u.ProfileImageUrl, i.OnboardedAt, i.UpdatedAt,
    };

    private static List<string> Split(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? new() : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var (user, inst, error) = await LoadAsync(track: false);
        if (error != null) return error;
        return Ok(new { success = true, data = View(inst!, user!) });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateInstitutionProfileRequest req)
    {
        var (user, inst, error) = await LoadAsync(track: true);
        if (error != null) return error;

        string? Clean(string? s, int max) => s == null ? null : (s.Trim().Length == 0 ? "" : s.Trim()[..Math.Min(s.Trim().Length, max)]);

        if (req.Type != null && !Types.Contains(req.Type))
            return Refus(400, "type_invalide", "Type d'établissement invalide.");
        List<string>? cycles = null;
        if (req.Cycles != null)
        {
            cycles = req.Cycles.Where(c => CycleValues.Contains(c)).Distinct().ToList();
            if (cycles.Count == 0) return Refus(400, "cycles_invalides", "Choisissez au moins un cycle.");
        }
        if (req.Subsystem != null && req.Subsystem is not ("fr" or "en" or "bilingue"))
            return Refus(400, "sous_systeme_invalide", "Sous-système invalide : fr, en ou bilingue.");
        if (req.Teaching != null && req.Teaching is not ("general" or "technique" or "both"))
            return Refus(400, "enseignement_invalide", "Enseignement invalide : general, technique ou both.");
        if (req.StudentsEstimate is < 0 or >= 100000)
            return Refus(400, "effectif_invalide", "L'effectif estimé doit être compris entre 0 et 99 999.");
        var phone = Clean(req.Phone, 30);
        if (!string.IsNullOrEmpty(phone) && !PhonePattern.IsMatch(phone))
            return Refus(400, "telephone_invalide", "Numéro de téléphone invalide.");
        var email = Clean(req.Email, 200);
        if (!string.IsNullOrEmpty(email) && !new EmailAddressAttribute().IsValid(email))
            return Refus(400, "email_invalide", "Adresse e-mail invalide.");
        var city = Clean(req.City, 100);
        if (city == "") return Refus(400, "ville_requise", "La ville ne peut pas être vide.");

        if (req.Type != null) inst!.Type = req.Type;
        if (cycles != null) inst!.Cycles = string.Join(',', cycles);
        if (req.Subsystem != null) inst!.Subsystem = req.Subsystem;
        if (req.Teaching != null) inst!.Teaching = req.Teaching;
        if (req.StudentsEstimate.HasValue) inst!.StudentsEstimate = req.StudentsEstimate.Value == 0 ? null : req.StudentsEstimate;
        if (req.Goals != null) inst!.Goals = string.Join(',', req.Goals.Where(g => GoalValues.Contains(g)).Distinct());
        if (city != null) inst!.City = city;
        if (req.Region != null) inst!.Region = NullIfEmpty(Clean(req.Region, 100));
        if (req.Address != null) inst!.Address = NullIfEmpty(Clean(req.Address, 300));
        if (phone != null) inst!.Phone = NullIfEmpty(phone);
        if (email != null) inst!.Email = NullIfEmpty(email);

        // Nom : demande de modification envoyée à l'équipe, jamais appliquée directement.
        var nameChangeRequested = false;
        var newName = Clean(req.Name, 200);
        if (!string.IsNullOrEmpty(newName) && !string.Equals(newName, inst!.Name, StringComparison.Ordinal))
        {
            if (newName.Length < 3) return Refus(400, "nom_invalide", "Le nom doit compter au moins 3 caractères.");
            try
            {
                await _ntfy.PublishAdminAsync("Changement de nom d'établissement demandé",
                    $"« {inst.Name} » (#{inst.Id}) demande à s'appeler « {newName} » · compte {user!.Email}",
                    priority: "high", type: "InstitutionRename", relatedEntityType: "Institution", relatedEntityId: inst.Id);
                nameChangeRequested = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Alerte de changement de nom impossible pour l'établissement {Id}", inst.Id);
                return Refus(502, "demande_non_envoyee", "La demande de changement de nom n'a pas pu être envoyée. Réessayez dans un instant.");
            }
        }

        inst!.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { success = true, data = View(inst, user!), nameChangeRequested });
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
