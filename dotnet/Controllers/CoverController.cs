using Backend.Data;
using Backend.Extensions;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Génération de pochette à la demande (WinAI). Admin : tout contenu.
/// Professeur : uniquement ses propres contenus (Subject.AuthorUserId,
/// Course.InstructorId). Les épreuves (Exam) sont réservées à l'admin.
/// </summary>
[ApiController]
[Route("api/covers")]
[Authorize]
public class CoverController : ControllerBase
{
    public record GenerateCoverDto(string Kind, int Id);

    private readonly ICoverGenerationService _covers;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<CoverController> _logger;

    public CoverController(ICoverGenerationService covers, ApplicationDbContext db, ILogger<CoverController> logger)
    {
        _covers = covers;
        _db = db;
        _logger = logger;
    }

    [HttpPost("generate")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(503)]
    public async Task<IActionResult> Generate([FromBody] GenerateCoverDto dto, CancellationToken ct)
    {
        if (dto.Id <= 0) return BadRequest(new { error = "Identifiant invalide." });

        var isAdmin = User.IsInRole("admin");
        if (!isAdmin)
        {
            if (dto.Kind == "exam") return StatusCode(403, new { error = "Accès refusé." });
            var userId = User.GetUserId();
            var owned = dto.Kind switch
            {
                "subject" => await _db.Subjects.AsNoTracking().AnyAsync(s => s.Id == dto.Id && s.AuthorUserId == userId, ct),
                "course" => await _db.Courses.AsNoTracking().AnyAsync(c => c.Id == dto.Id && c.InstructorId == userId, ct),
                _ => false,
            };
            if (!owned) return StatusCode(403, new { error = "Accès refusé." });
        }

        var result = await _covers.GenerateAndAssignAsync(dto.Kind, dto.Id, ct);
        if (!result.Success)
            return StatusCode(503, new { error = result.Error });

        return Ok(new { thumbnailUrl = result.ThumbnailUrl });
    }
}
