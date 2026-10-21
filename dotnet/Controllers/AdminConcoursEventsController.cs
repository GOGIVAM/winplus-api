using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Data;
using Backend.Services.Translation;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Lot 10.4 : écran admin « Contenus des concours ». CRUD complet sur
/// ConcoursEvents (calendrier, contenus éditoriaux, FAQ, versions anglaises).
///
///   GET    /api/admin/concours-events?slug=&amp;year=   liste (publiés ou non)
///   GET    /api/admin/concours-events/{id}
///   POST   /api/admin/concours-events                 création (brouillon par défaut)
///   PUT    /api/admin/concours-events/{id}            modification complète
///   POST   /api/admin/concours-events/{id}/publish    publication
///   POST   /api/admin/concours-events/{id}/unpublish  dépublication
///   DELETE /api/admin/concours-events/{id}            suppression
///
/// Toute information officielle (programme, conditions d'admission,
/// coefficients, dates) exige une source (URL) et sa date de consultation.
/// Chaque écriture est tracée dans le journal d'audit.
/// </summary>
[ApiController]
[Route("api/admin/concours-events")]
[Authorize(Policy = "AdminOnly")]
public class AdminConcoursEventsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IAdminAuditService _audit;

    public AdminConcoursEventsController(ApplicationDbContext db, IAdminAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public sealed record AdminConcoursEventDto : ConcoursEventDto
    {
        public bool IsPublished { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        /// <summary>Champs *En remplis par WinAI (à relire) et champs à revoir (français modifié depuis).</summary>
        public List<string> AutoTranslatedFields { get; init; }
        public List<string> ReviewFields { get; init; }

        public AdminConcoursEventDto(ConcoursEvent e) : base(e)
        {
            IsPublished = e.IsPublished;
            CreatedAt = e.CreatedAt;
            UpdatedAt = e.UpdatedAt;
            var meta = ConcoursTranslationMeta.Parse(e.TranslationMetaJson);
            AutoTranslatedFields = meta.Auto.Keys.ToList();
            ReviewFields = meta.Review.ToList();
        }
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? slug = null, [FromQuery] int year = 0)
    {
        var q = _db.ConcoursEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(slug)) q = q.Where(e => e.Slug == slug.Trim().ToLower());
        if (year > 0) q = q.Where(e => e.Year == year);
        var rows = await q.OrderBy(e => e.Slug).ThenByDescending(e => e.Year).ThenBy(e => e.Id).ToListAsync();
        return Ok(new { success = true, data = rows.Select(e => new AdminConcoursEventDto(e)).ToList() });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var ev = await _db.ConcoursEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
        if (ev is null) return NotFound(new { success = false, error = "Concours introuvable." });
        return Ok(new { success = true, data = new AdminConcoursEventDto(ev) });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ConcoursEventWriteRequest request)
    {
        var errors = ConcoursEventValidation.Validate(request);
        if (errors.Count > 0) return BadRequest(new { success = false, error = errors[0], errors });

        var slug = request.Slug!.Trim().ToLowerInvariant();
        if (await _db.ConcoursEvents.AnyAsync(e => e.Slug == slug && e.Year == request.Year))
            return Conflict(new { success = false, error = $"Une fiche {slug} {request.Year} existe déjà." });

        var ev = new ConcoursEvent { CreatedAt = DateTime.UtcNow, IsPublished = request.IsPublished ?? false };
        var enBefore = ConcoursTranslationMeta.Snapshot(ev);
        ConcoursEventValidation.Apply(ev, request);
        ConcoursTranslationMeta.ApplyAdminWrite(ev, enBefore, request.EnAutoFields);
        ev.UpdatedAt = DateTime.UtcNow;
        _db.ConcoursEvents.Add(ev);
        await _db.SaveChangesAsync();
        _audit.Add(User, HttpContext?.Connection?.RemoteIpAddress?.ToString(), "concours_event.create", "ConcoursEvent", ev.Id,
            $"{ev.Slug} {ev.Year}", after: new AdminConcoursEventDto(ev));
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = ev.Id }, new { success = true, data = new AdminConcoursEventDto(ev) });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ConcoursEventWriteRequest request)
    {
        var ev = await _db.ConcoursEvents.FirstOrDefaultAsync(e => e.Id == id);
        if (ev is null) return NotFound(new { success = false, error = "Concours introuvable." });

        var errors = ConcoursEventValidation.Validate(request);
        if (errors.Count > 0) return BadRequest(new { success = false, error = errors[0], errors });

        var slug = request.Slug!.Trim().ToLowerInvariant();
        if (await _db.ConcoursEvents.AnyAsync(e => e.Id != id && e.Slug == slug && e.Year == request.Year))
            return Conflict(new { success = false, error = $"Une autre fiche {slug} {request.Year} existe déjà." });

        var before = new AdminConcoursEventDto(ev);
        var enBefore = ConcoursTranslationMeta.Snapshot(ev);
        ConcoursEventValidation.Apply(ev, request);
        // Les champs *En vides seront remplis en tâche de fond (ContentTranslationWorker).
        ConcoursTranslationMeta.ApplyAdminWrite(ev, enBefore, request.EnAutoFields);
        if (request.IsPublished.HasValue) ev.IsPublished = request.IsPublished.Value;
        ev.UpdatedAt = DateTime.UtcNow;
        _audit.Add(User, HttpContext?.Connection?.RemoteIpAddress?.ToString(), "concours_event.update", "ConcoursEvent", ev.Id,
            $"{ev.Slug} {ev.Year}", before: before, after: new AdminConcoursEventDto(ev));
        await _db.SaveChangesAsync();
        return Ok(new { success = true, data = new AdminConcoursEventDto(ev) });
    }

    public sealed record TranslatePreviewRequest(Dictionary<string, string?>? Fields);

    /// <summary>
    /// Bouton « Traduire » : propose la version anglaise des champs français
    /// fournis (Name, Notes, Tips, FaqJson, Programme, AdmissionConditions,
    /// Tracks, Coefficients), sans rien enregistrer.
    /// </summary>
    [HttpPost("translate")]
    public async Task<IActionResult> TranslatePreview([FromBody] TranslatePreviewRequest request,
        [FromServices] ContentTranslationService translations)
    {
        var fields = request.Fields ?? new();
        if (fields.Values.Sum(v => v?.Length ?? 0) > 60000) return BadRequest(new { success = false, error = "Texte trop long." });
        var (data, error) = await translations.PreviewConcoursAsync(fields);
        if (error != null && data.Values.All(v => v == null))
            return StatusCode(503, new { success = false, error = "La traduction automatique est indisponible pour le moment. Réessayez plus tard ; à l'enregistrement, les champs anglais vides seront traduits dès que WinAI répondra." });
        return Ok(new { success = true, data, partial = error != null });
    }

    [HttpPost("{id:int}/publish")]
    public Task<IActionResult> Publish(int id) => SetPublished(id, true);

    [HttpPost("{id:int}/unpublish")]
    public Task<IActionResult> Unpublish(int id) => SetPublished(id, false);

    private async Task<IActionResult> SetPublished(int id, bool published)
    {
        var ev = await _db.ConcoursEvents.FirstOrDefaultAsync(e => e.Id == id);
        if (ev is null) return NotFound(new { success = false, error = "Concours introuvable." });
        if (published)
        {
            // Une fiche publiée doit rester conforme, même saisie avant l'écran (scripts SQL).
            var errors = ConcoursEventValidation.Validate(ConcoursEventWriteRequest.From(ev));
            if (errors.Count > 0) return BadRequest(new { success = false, error = errors[0], errors });
        }
        if (ev.IsPublished != published)
        {
            ev.IsPublished = published;
            ev.UpdatedAt = DateTime.UtcNow;
            _audit.Add(User, HttpContext?.Connection?.RemoteIpAddress?.ToString(),
                published ? "concours_event.publish" : "concours_event.unpublish", "ConcoursEvent", ev.Id, $"{ev.Slug} {ev.Year}");
            await _db.SaveChangesAsync();
        }
        return Ok(new { success = true, data = new AdminConcoursEventDto(ev) });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var ev = await _db.ConcoursEvents.FirstOrDefaultAsync(e => e.Id == id);
        if (ev is null) return NotFound(new { success = false, error = "Concours introuvable." });
        _audit.Add(User, HttpContext?.Connection?.RemoteIpAddress?.ToString(), "concours_event.delete", "ConcoursEvent", ev.Id,
            $"{ev.Slug} {ev.Year}", before: new AdminConcoursEventDto(ev));
        _db.ConcoursEvents.Remove(ev);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}

/// <summary>Corps de création et de modification d'un concours (tous les champs).</summary>
public sealed record ConcoursEventWriteRequest
{
    public string? Slug { get; init; }
    public string? Name { get; init; }
    public int Year { get; init; }
    public DateTime? RegistrationStartDate { get; init; }
    public DateTime? RegistrationEndDate { get; init; }
    public DateTime? ExamDate { get; init; }
    public DateTime? ResultsDate { get; init; }
    public string? Location { get; init; }
    public int? EnrollmentFeeXaf { get; init; }
    public string? OfficialRegistrationUrl { get; init; }
    public string? Notes { get; init; }
    public string? Tips { get; init; }
    public string? FaqJson { get; init; }
    public string? Programme { get; init; }
    public string? AdmissionConditions { get; init; }
    public string? Tracks { get; init; }
    public string? Coefficients { get; init; }
    public string? SourceUrl { get; init; }
    public DateTime? SourceCheckedAt { get; init; }
    public string? NameEn { get; init; }
    public string? NotesEn { get; init; }
    public string? TipsEn { get; init; }
    public string? FaqJsonEn { get; init; }
    public string? ProgrammeEn { get; init; }
    public string? AdmissionConditionsEn { get; init; }
    public string? TracksEn { get; init; }
    public string? CoefficientsEn { get; init; }
    /// <summary>Création : brouillon si absent. Modification : inchangé si absent.</summary>
    public bool? IsPublished { get; init; }
    /// <summary>Champs *En remplis par le bouton « Traduire » et non retouchés : ils restent « auto ».</summary>
    public List<string>? EnAutoFields { get; init; }

    public static ConcoursEventWriteRequest From(ConcoursEvent e) => new()
    {
        Slug = e.Slug, Name = e.Name, Year = e.Year,
        RegistrationStartDate = e.RegistrationStartDate, RegistrationEndDate = e.RegistrationEndDate,
        ExamDate = e.ExamDate, ResultsDate = e.ResultsDate, Location = e.Location,
        EnrollmentFeeXaf = e.EnrollmentFeeXaf, OfficialRegistrationUrl = e.OfficialRegistrationUrl,
        Notes = e.Notes, Tips = e.Tips, FaqJson = e.FaqJson,
        Programme = e.Programme, AdmissionConditions = e.AdmissionConditions, Tracks = e.Tracks, Coefficients = e.Coefficients,
        SourceUrl = e.SourceUrl, SourceCheckedAt = e.SourceCheckedAt,
        NameEn = e.NameEn, NotesEn = e.NotesEn, TipsEn = e.TipsEn, FaqJsonEn = e.FaqJsonEn,
        ProgrammeEn = e.ProgrammeEn, AdmissionConditionsEn = e.AdmissionConditionsEn, TracksEn = e.TracksEn, CoefficientsEn = e.CoefficientsEn,
        IsPublished = e.IsPublished,
    };
}

/// <summary>Règles de saisie des concours (partagées par l'écran admin et l'endpoint /content).</summary>
public static class ConcoursEventValidation
{
    private static readonly Regex SlugPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);
    public const int MaxEditorialLength = 20000;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>URL de la source, une par ligne.</summary>
    public static IReadOnlyList<string> SourceUrls(string? value) =>
        (value ?? string.Empty).Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    /// <summary>Erreur de format d'une FAQ JSON [{question, answer}], ou null si elle est valide (ou vide).</summary>
    public static string? FaqError(string? json, string label = "FAQ")
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return $"{label} : une liste de questions est attendue.";
            var i = 0;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                i++;
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("question", out var q) || q.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(q.GetString())
                    || !item.TryGetProperty("answer", out var a) || a.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(a.GetString()))
                    return $"{label} : la question {i} doit avoir une question et une réponse.";
            }
            return null;
        }
        catch (JsonException)
        {
            return $"{label} : JSON invalide.";
        }
    }

    /// <summary>La fiche contient-elle une information officielle (programme, conditions, coefficients, dates) ?</summary>
    public static bool HasOfficialInformation(ConcoursEventWriteRequest r) =>
        r.RegistrationStartDate.HasValue || r.RegistrationEndDate.HasValue || r.ExamDate.HasValue || r.ResultsDate.HasValue
        || Clean(r.Programme) != null || Clean(r.AdmissionConditions) != null || Clean(r.Coefficients) != null
        || Clean(r.ProgrammeEn) != null || Clean(r.AdmissionConditionsEn) != null || Clean(r.CoefficientsEn) != null;

    public static List<string> Validate(ConcoursEventWriteRequest r)
    {
        var errors = new List<string>();
        var slug = Clean(r.Slug)?.ToLowerInvariant();
        if (slug == null || slug.Length > 50 || !SlugPattern.IsMatch(slug)) errors.Add("Examen : identifiant invalide (lettres minuscules, chiffres et tirets).");
        var name = Clean(r.Name);
        if (name == null) errors.Add("Nom : obligatoire.");
        else if (name.Length > 200) errors.Add("Nom : 200 caractères au plus.");
        if (r.Year < 2000 || r.Year > 2100) errors.Add("Année : entre 2000 et 2100.");

        void Max(string? v, int max, string label) { if (v != null && v.Trim().Length > max) errors.Add($"{label} : {max} caractères au plus."); }
        Max(r.Location, 300, "Lieu");
        Max(r.Notes, 1000, "Notes");
        Max(r.NotesEn, 1000, "Notes (anglais)");
        Max(r.Tips, 2000, "Conseils");
        Max(r.TipsEn, 2000, "Conseils (anglais)");
        Max(r.NameEn, 200, "Nom (anglais)");
        Max(r.OfficialRegistrationUrl, 500, "Site d'inscription");
        Max(r.SourceUrl, 1000, "Source");
        foreach (var (v, label) in new[]
        {
            (r.Programme, "Programme"), (r.AdmissionConditions, "Conditions d'admission"), (r.Tracks, "Filières"), (r.Coefficients, "Coefficients"),
            (r.ProgrammeEn, "Programme (anglais)"), (r.AdmissionConditionsEn, "Conditions d'admission (anglais)"),
            (r.TracksEn, "Filières (anglais)"), (r.CoefficientsEn, "Coefficients (anglais)"),
        })
            Max(v, MaxEditorialLength, label);

        if (r.EnrollmentFeeXaf is < 0) errors.Add("Frais d'inscription : montant négatif.");
        if (Clean(r.OfficialRegistrationUrl) is { } reg && !IsHttpUrl(reg)) errors.Add("Site d'inscription : adresse http(s) attendue.");
        if (r.RegistrationStartDate.HasValue && r.RegistrationEndDate.HasValue && r.RegistrationStartDate > r.RegistrationEndDate)
            errors.Add("Dates : la clôture des inscriptions précède leur ouverture.");

        if (FaqError(r.FaqJson, "FAQ") is { } faq) errors.Add(faq);
        if (FaqError(r.FaqJsonEn, "FAQ anglaise") is { } faqEn) errors.Add(faqEn);

        var urls = SourceUrls(r.SourceUrl);
        if (urls.Any(u => !IsHttpUrl(u))) errors.Add("Source : chaque ligne doit être une adresse http(s).");
        if (r.SourceCheckedAt.HasValue && r.SourceCheckedAt.Value.Date > DateTime.UtcNow.Date.AddDays(1))
            errors.Add("Source : la date de consultation est dans le futur.");
        if (HasOfficialInformation(r))
        {
            if (urls.Count == 0) errors.Add("Source : obligatoire pour le programme, les conditions, les coefficients et les dates.");
            if (!r.SourceCheckedAt.HasValue) errors.Add("Source : indiquez la date de consultation.");
        }
        return errors;
    }

    /// <summary>Copie la saisie validée dans l'entité (textes nettoyés, vides remis à null).</summary>
    public static void Apply(ConcoursEvent ev, ConcoursEventWriteRequest r)
    {
        ev.Slug = r.Slug!.Trim().ToLowerInvariant();
        ev.Name = r.Name!.Trim();
        ev.Year = r.Year;
        ev.RegistrationStartDate = Utc(r.RegistrationStartDate);
        ev.RegistrationEndDate = Utc(r.RegistrationEndDate);
        ev.ExamDate = Utc(r.ExamDate);
        ev.ResultsDate = Utc(r.ResultsDate);
        ev.Location = Clean(r.Location);
        ev.EnrollmentFeeXaf = r.EnrollmentFeeXaf;
        ev.OfficialRegistrationUrl = Clean(r.OfficialRegistrationUrl);
        ev.Notes = Clean(r.Notes);
        ev.Tips = Clean(r.Tips);
        ev.FaqJson = Clean(r.FaqJson);
        ev.Programme = Clean(r.Programme);
        ev.AdmissionConditions = Clean(r.AdmissionConditions);
        ev.Tracks = Clean(r.Tracks);
        ev.Coefficients = Clean(r.Coefficients);
        var urls = SourceUrls(r.SourceUrl);
        ev.SourceUrl = urls.Count == 0 ? null : string.Join("\n", urls);
        ev.SourceCheckedAt = Utc(r.SourceCheckedAt);
        ev.NameEn = Clean(r.NameEn);
        ev.NotesEn = Clean(r.NotesEn);
        ev.TipsEn = Clean(r.TipsEn);
        ev.FaqJsonEn = Clean(r.FaqJsonEn);
        ev.ProgrammeEn = Clean(r.ProgrammeEn);
        ev.AdmissionConditionsEn = Clean(r.AdmissionConditionsEn);
        ev.TracksEn = Clean(r.TracksEn);
        ev.CoefficientsEn = Clean(r.CoefficientsEn);
    }

    /// <summary>Npgsql exige des dates UTC pour « timestamp with time zone ».</summary>
    private static DateTime? Utc(DateTime? d) => d switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        var v => DateTime.SpecifyKind(v.Value, DateTimeKind.Utc),
    };
}
