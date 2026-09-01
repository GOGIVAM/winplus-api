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
}

public record ConcoursEventDto(
    int Id,
    string Slug,
    string Name,
    int Year,
    DateTime? RegistrationStartDate,
    DateTime? RegistrationEndDate,
    DateTime? ExamDate,
    DateTime? ResultsDate,
    string? Location,
    int? EnrollmentFeeXaf,
    string? OfficialRegistrationUrl,
    string? Notes,
    string RegistrationStatus
)
{
    public ConcoursEventDto(ConcoursEvent e) : this(
        e.Id,
        e.Slug,
        e.Name,
        e.Year,
        e.RegistrationStartDate,
        e.RegistrationEndDate,
        e.ExamDate,
        e.ResultsDate,
        e.Location,
        e.EnrollmentFeeXaf,
        e.OfficialRegistrationUrl,
        e.Notes,
        ComputeStatus(e)
    ) { }

    private static string ComputeStatus(ConcoursEvent e)
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
