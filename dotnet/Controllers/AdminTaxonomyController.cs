using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;

namespace Backend.Controllers;

/// <summary>
/// Référentiels de l'écran d'upload admin (matières, examens, niveaux).
/// Ces listes étaient codées en dur dans AdminUpload.tsx : ajouter une matière
/// imposait de modifier le frontend. Elles sont désormais déduites du contenu
/// réel de la base, donc toujours à jour.
///
/// GET /api/admin/taxonomy
/// </summary>
[ApiController]
[Route("api/admin/taxonomy")]
[Authorize(Roles = "admin")]
public class AdminTaxonomyController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AdminTaxonomyController> _logger;

    public AdminTaxonomyController(ApplicationDbContext db, ILogger<AdminTaxonomyController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        try
        {
            var categories = await _db.Subjects.AsNoTracking()
                .Where(s => !s.IsDeleted && s.Category != null && s.Category != "")
                .GroupBy(s => s.Category!)
                .Select(g => new { value = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync();

            var examTypes = await _db.Exams.AsNoTracking()
                .Where(e => e.ExamType != null && e.ExamType != "")
                .GroupBy(e => e.ExamType)
                .Select(g => new { value = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync();

            var examLevels = await _db.Exams.AsNoTracking()
                .Where(e => e.Level != null && e.Level != "")
                .Select(e => e.Level!)
                .Distinct()
                .ToListAsync();

            var userLevels = await _db.Users.AsNoTracking()
                .Where(u => !u.IsDeleted && u.Level != null && u.Level != "")
                .Select(u => u.Level!)
                .Distinct()
                .ToListAsync();

            var levels = examLevels.Union(userLevels).OrderBy(l => l).ToList();

            var years = await _db.Exams.AsNoTracking()
                .Select(e => e.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .Take(30)
                .ToListAsync();

            return Ok(new
            {
                data = new { categories, examTypes, levels, years },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building admin taxonomy");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
