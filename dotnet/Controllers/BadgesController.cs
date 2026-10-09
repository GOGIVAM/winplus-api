using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;

namespace Backend.Controllers;

/// <summary>
/// Module 24 (lot 8, rattrapage 16.2) : badges plateforme de l'utilisateur
/// courant (débloqués + verrouillés avec leur critère), voir BadgeService
/// pour le moteur de déblocage et le catalogue (décision §16.2 du suivi).
///
/// GET /api/badges  — mes badges (débloqués et non débloqués).
/// </summary>
[ApiController]
[Route("api/badges")]
[Authorize]
public class BadgesController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public BadgesController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetMine()
    {
        try
        {
            var userId = User.GetUserId();

            var defs = await _db.BadgeDefinitions.AsNoTracking()
                .Where(d => d.IsActive)
                .OrderBy(d => d.Id)
                .ToListAsync();

            var unlocked = await _db.UserBadges.AsNoTracking()
                .Where(b => b.UserId == userId)
                .ToDictionaryAsync(b => b.BadgeDefinitionId, b => b.UnlockedAt);

            var data = defs.Select(d => new
            {
                code = d.Code,
                title = d.Title,
                description = d.Description,
                icon = d.Icon,
                unlocked = unlocked.ContainsKey(d.Id),
                unlockedAt = unlocked.TryGetValue(d.Id, out var at) ? at : (DateTime?)null,
            }).ToList();

            return Ok(new { data, success = true });
        }
        catch (Exception)
        {
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
