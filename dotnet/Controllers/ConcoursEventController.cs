using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.Entities;

namespace Backend.Controllers;

[ApiController]
[Route("api/concours-events")]
public class ConcoursEventController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public ConcoursEventController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int year = 0)
    {
        var q = _db.ConcoursEvents.Where(e => e.IsPublished);
        if (year > 0) q = q.Where(e => e.Year == year);
        else q = q.Where(e => e.Year == DateTime.UtcNow.Year || e.Year == DateTime.UtcNow.Year + 1);

        var events = await q
            .OrderBy(e => e.ExamDate)
            .Select(e => new ConcoursEventDto(e))
            .ToListAsync();

        return Ok(events);
    }

    [HttpGet("{slug}/{year:int}")]
    public async Task<IActionResult> Get(string slug, int year)
    {
        var ev = await _db.ConcoursEvents
            .Where(e => e.Slug == slug && e.Year == year && e.IsPublished)
            .FirstOrDefaultAsync();

        if (ev is null) return NotFound();
        return Ok(new ConcoursEventDto(ev));
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetLatest(string slug)
    {
        var ev = await _db.ConcoursEvents
            .Where(e => e.Slug == slug && e.IsPublished)
            .OrderByDescending(e => e.Year)
            .FirstOrDefaultAsync();

        if (ev is null) return NotFound();
        return Ok(new ConcoursEventDto(ev));
    }

    /// <summary>Conseils de réussite + FAQ (Module 2, US-CAT-09).</summary>
    [HttpPut("{id:int}/content")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateContent(int id, [FromBody] UpdateConcoursContentRequest request)
    {
        var ev = await _db.ConcoursEvents.FirstOrDefaultAsync(e => e.Id == id);
        if (ev is null) return NotFound();

        if (request.FaqJson != null && ConcoursEventValidation.FaqError(request.FaqJson, "FAQ") is { } faqError)
            return BadRequest(new { success = false, error = faqError });
        if (request.FaqJsonEn != null && ConcoursEventValidation.FaqError(request.FaqJsonEn, "FAQ anglaise") is { } faqEnError)
            return BadRequest(new { success = false, error = faqEnError });

        if (request.Tips != null) ev.Tips = request.Tips.Length > 2000 ? request.Tips[..2000] : request.Tips;
        if (request.FaqJson != null) ev.FaqJson = request.FaqJson;
        if (request.TipsEn != null) ev.TipsEn = request.TipsEn.Length > 2000 ? request.TipsEn[..2000] : request.TipsEn;
        if (request.FaqJsonEn != null) ev.FaqJsonEn = request.FaqJsonEn;
        ev.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new ConcoursEventDto(ev));
    }
}

public record UpdateConcoursContentRequest(string? Tips, string? FaqJson, string? TipsEn = null, string? FaqJsonEn = null);

/// <summary>
/// Concours tel que l'affichent les pages publiques (et l'écran admin, qui y
/// ajoute IsPublished et les dates de suivi). Lot 10.4 : contenus éditoriaux,
/// source et versions anglaises facultatives (suffixe En).
/// </summary>
public record ConcoursEventDto
{
    public int Id { get; init; }
    public string Slug { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Year { get; init; }
    public DateTime? RegistrationStartDate { get; init; }
    public DateTime? RegistrationEndDate { get; init; }
    public DateTime? ExamDate { get; init; }
    public DateTime? ResultsDate { get; init; }
    public string? Location { get; init; }
    public int? EnrollmentFeeXaf { get; init; }
    public string? OfficialRegistrationUrl { get; init; }
    public string? Notes { get; init; }
    public string RegistrationStatus { get; init; } = "unknown";
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

    public ConcoursEventDto() { }

    public ConcoursEventDto(ConcoursEvent e)
    {
        Id = e.Id;
        Slug = e.Slug;
        Name = e.Name;
        Year = e.Year;
        RegistrationStartDate = e.RegistrationStartDate;
        RegistrationEndDate = e.RegistrationEndDate;
        ExamDate = e.ExamDate;
        ResultsDate = e.ResultsDate;
        Location = e.Location;
        EnrollmentFeeXaf = e.EnrollmentFeeXaf;
        OfficialRegistrationUrl = e.OfficialRegistrationUrl;
        Notes = e.Notes;
        RegistrationStatus = ComputeStatus(e);
        Tips = e.Tips;
        FaqJson = e.FaqJson;
        Programme = e.Programme;
        AdmissionConditions = e.AdmissionConditions;
        Tracks = e.Tracks;
        Coefficients = e.Coefficients;
        SourceUrl = e.SourceUrl;
        SourceCheckedAt = e.SourceCheckedAt;
        NameEn = e.NameEn;
        NotesEn = e.NotesEn;
        TipsEn = e.TipsEn;
        FaqJsonEn = e.FaqJsonEn;
        ProgrammeEn = e.ProgrammeEn;
        AdmissionConditionsEn = e.AdmissionConditionsEn;
        TracksEn = e.TracksEn;
        CoefficientsEn = e.CoefficientsEn;
    }

    public static string ComputeStatus(ConcoursEvent e)
    {
        var now = DateTime.UtcNow;
        if (e.RegistrationStartDate.HasValue && now < e.RegistrationStartDate)
            return "upcoming";
        if (e.RegistrationEndDate.HasValue && now > e.RegistrationEndDate)
            return "closed";
        if (e.RegistrationStartDate.HasValue && e.RegistrationEndDate.HasValue
            && now >= e.RegistrationStartDate && now <= e.RegistrationEndDate)
            return "open";
        return "unknown";
    }
}
