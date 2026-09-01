using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;

namespace Backend.Controllers;

public class StartFocusRequest
{
    public int PlannedDurationSeconds { get; set; } = 1500;
    public string? Label { get; set; }
}

public class CompleteFocusRequest
{
    public int ActualDurationSeconds { get; set; }
}

/// <summary>
/// Gestion des sessions de concentration (Focus Mode).
/// POST /api/focus-sessions/start      → démarre une session
/// POST /api/focus-sessions/{id}/complete → termine la session avec durée réelle
/// GET  /api/focus-sessions/stats      → stats hebdo pour /mes-rapports
/// </summary>
[ApiController]
[Route("api/focus-sessions")]
[Authorize]
public class FocusSessionController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public FocusSessionController(ApplicationDbContext db) => _db = db;

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartFocusRequest req)
    {
        var userId = User.GetUserId();
        var session = new FocusSession
        {
            UserId = userId,
            PlannedDurationSeconds = Math.Clamp(req.PlannedDurationSeconds, 60, 7200),
            Label = req.Label,
            StartedAt = DateTime.UtcNow,
        };
        _db.FocusSessions.Add(session);
        await _db.SaveChangesAsync();
        return Ok(new { success = true, data = new { session.Id, session.StartedAt, session.PlannedDurationSeconds } });
    }

    [HttpPost("{id:int}/complete")]
    public async Task<IActionResult> Complete([FromRoute] int id, [FromBody] CompleteFocusRequest req)
    {
        var userId = User.GetUserId();
        var session = await _db.FocusSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        if (session is null) return NotFound(new { success = false, error = "Session introuvable." });
        if (session.CompletedAt.HasValue) return Ok(new { success = true, data = new { session.Id, session.ActualDurationSeconds } });

        session.ActualDurationSeconds = Math.Max(0, req.ActualDurationSeconds);
        session.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { success = true, data = new { session.Id, session.ActualDurationSeconds, session.CompletedAt } });
    }

    /// <summary>Stats des 7 derniers jours : total de minutes de concentration par jour.</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var userId = User.GetUserId();
        var since = DateTime.UtcNow.AddDays(-7);

        var rows = await _db.FocusSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.CompletedAt.HasValue && s.StartedAt >= since)
            .Select(s => new
            {
                day = s.StartedAt.Date,
                seconds = s.ActualDurationSeconds ?? 0,
            })
            .ToListAsync();

        var byDay = rows
            .GroupBy(r => r.day)
            .Select(g => new { date = g.Key.ToString("yyyy-MM-dd"), minutes = g.Sum(r => r.seconds) / 60 })
            .OrderBy(x => x.date)
            .ToList();

        var totalMinutes = rows.Sum(r => r.seconds) / 60;
        return Ok(new { success = true, data = new { totalMinutes, byDay } });
    }
}
