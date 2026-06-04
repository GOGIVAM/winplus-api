using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Backend.Services;
using Backend.Models.Entities;
using Backend.Models.DTOs;
using Backend.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Backend.Controllers;

[ApiController]
[Route("api/subjects")]
public class SubjectsController : ControllerBase
{
    private readonly ISubjectService _subjectService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SubjectsController> _logger;

    public SubjectsController(ISubjectService subjectService, ApplicationDbContext context, ILogger<SubjectsController> logger)
    {
        _subjectService = subjectService;
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PaginationResponse<Subject>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1, 
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "createdAt",
        [FromQuery] string sortOrder = "desc")
    {
        try
        {
            if (page < 1) page = 1;
            pageSize = Math.Clamp(pageSize, 1, 500);

            var subjects = await _subjectService.GetAllSubjectsAsync(page, pageSize);
            
            // Apply sorting (if needed - subjects might already be sorted by service)
            var sortedSubjects = SortSubjects(subjects, sortBy, sortOrder);
            
            var totalCount = await _subjectService.GetTotalSubjectsCountAsync();
            
            var response = new PaginationResponse<Subject>(sortedSubjects, totalCount, page, pageSize);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des cours");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Helper method to sort subjects
    /// </summary>
    private List<Subject> SortSubjects(IEnumerable<Subject> subjects, string sortBy, string sortOrder)
    {
        var isAscending = sortOrder?.ToLower() != "desc";
        
        var sorted = sortBy?.ToLower() switch
        {
            "price" => isAscending 
                ? subjects.OrderBy(s => s.Price).ToList()
                : subjects.OrderByDescending(s => s.Price).ToList(),
            "title" => isAscending
                ? subjects.OrderBy(s => s.Title).ToList()
                : subjects.OrderByDescending(s => s.Title).ToList(),
            "createdAt" => isAscending
                ? subjects.OrderBy(s => s.CreatedAt).ToList()
                : subjects.OrderByDescending(s => s.CreatedAt).ToList(),
            _ => subjects.OrderByDescending(s => s.CreatedAt).ToList() // Default sort
        };
        
        return sorted;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var subject = await _subjectService.GetSubjectByIdAsync(id);
            if (subject == null)
                return NotFound();
            return Ok(subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération du cours {SubjectId}", id);
            return StatusCode(500, "Erreur serveur");
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Subject subject)
    {
        try
        {
            var created = await _subjectService.CreateSubjectAsync(subject);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la création du cours");
            return StatusCode(500, "Erreur serveur");
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] Subject subject)
    {
        try
        {
            subject.Id = id;
            var updated = await _subjectService.UpdateSubjectAsync(subject);
            return Ok(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la mise à jour du cours {SubjectId}", id);
            return StatusCode(500, "Erreur serveur");
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var result = await _subjectService.DeleteSubjectAsync(id);
            if (!result)
                return NotFound();
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la suppression du cours {SubjectId}", id);
            return StatusCode(500, "Erreur serveur");
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q, 
        [FromQuery] int? limit = 50, 
        [FromQuery] string? sort = null,
        [FromQuery] bool? isFree = null)
    {
        try
        {
            var results = await _subjectService.SearchSubjectsAsync(q ?? "");
            
            // ✅ AJOUTÉ: Filtre par isFree (prix = 0)
            if (isFree.HasValue && isFree.Value)
            {
                results = results.Where(s => s.Price == 0 || s.Price == null);
            }
            
            // Apply sorting if specified
            if (!string.IsNullOrEmpty(sort))
            {
                results = sort.ToLower() switch
                {
                    "popular" => results.OrderByDescending(s => s.EnrollmentCount),
                    "newest" => results.OrderByDescending(s => s.CreatedAt),
                    "alphabetical" => results.OrderBy(s => s.Title),
                    "rating" => results.OrderByDescending(s => s.AverageRating),
                    _ => results
                };
            }
            
            // Apply limit
            if (limit.HasValue && limit.Value > 0)
            {
                results = results.Take(limit.Value);
            }
            
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la recherche de cours");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Obtenir les cours populaires
    /// GET /api/subjects/popular
    /// </summary>
    [HttpGet("popular")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPopular([FromQuery] int limit = 10)
    {
        try
        {
            _logger.LogInformation("Récupération des {Limit} cours populaires", limit);
            
            var subjects = await _subjectService.GetAllSubjectsAsync(1, limit);
            var popular = subjects.OrderByDescending(s => s.EnrollmentCount).Take(limit).ToList();

            // Enrichir avec viewCount calculé depuis LearningHistories
            var enrichedSubjects = new List<dynamic>();
            foreach (var subject in popular)
            {
                var viewCount = await _context.LearningHistories
                    .Where(l => l.SubjectId == subject.Id)
                    .CountAsync();

                enrichedSubjects.Add(new
                {
                    id = subject.Id,
                    title = subject.Title,
                    description = subject.Description,
                    category = subject.Category,
                    thumbnailUrl = subject.ThumbnailUrl,
                    price = subject.Price,
                    isPublished = subject.IsPublished,
                    enrollmentCount = subject.EnrollmentCount,
                    averageRating = subject.AverageRating,
                    totalRatings = subject.TotalRatings,
                    viewCount = viewCount,
                    downloadCount = subject.DownloadCount ?? 0,
                    isFeatured = subject.IsFeatured
                });
            }

            return Ok(enrichedSubjects);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des cours populaires");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Obtenir les cours en vedette (featured)
    /// GET /api/subjects/featured
    /// </summary>
    [HttpGet("featured")]
    [ProducesResponseType(typeof(List<Subject>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFeatured([FromQuery] int limit = 10)
    {
        try
        {
            _logger.LogInformation("Récupération des {Limit} cours en vedette", limit);
            var subjects = await _subjectService.GetAllSubjectsAsync(1, limit);
            return Ok(subjects.Where(s => s.IsFeatured).Take(limit).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des cours en vedette");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Obtenir les cours récents
    /// GET /api/subjects/recent
    /// </summary>
    [HttpGet("recent")]
    [ProducesResponseType(typeof(List<Subject>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecent([FromQuery] int limit = 10)
    {
        try
        {
            _logger.LogInformation("Récupération des {Limit} cours récents", limit);
            var subjects = await _subjectService.GetAllSubjectsAsync(1, limit);
            return Ok(subjects.OrderByDescending(s => s.CreatedAt).Take(limit).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des cours récents");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Obtenir les cours par catégorie
    /// GET /api/subjects/by-category/{categoryName}
    /// </summary>
    [HttpGet("by-category/{name}")]
    [ProducesResponseType(typeof(PaginationResponse<Subject>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByCategory(
        string name,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            _logger.LogInformation("Récupération des cours pour la catégorie {Category}", name);
            var results = await _subjectService.GetSubjectsByCategoryAsync(name);
            
            var paginated = results
                .OrderByDescending(s => s.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var response = new PaginationResponse<Subject>(paginated, results.Count(), page, pageSize);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des cours par catégorie");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Récupère toutes les catégories disponibles
    /// </summary>
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        try
        {
            var categories = await _subjectService.GetCategoriesAsync();
            return Ok(categories);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des catégories");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Récupère les filtres disponibles pour la recherche
    /// </summary>
    [HttpGet("filters")]
    public async Task<IActionResult> GetFilters()
    {
        try
        {
            var filters = await _subjectService.GetFiltersAsync();
            return Ok(filters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des filtres");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Télécharger le PDF d'une épreuve
    /// GET /api/subjects/{id}/download
    /// Retourne { downloadUrl, filename } ou 403 (abonnement requis) / 404 (PDF manquant)
    /// </summary>
    [HttpGet("{id}/download")]
    [Authorize]
    public async Task<IActionResult> Download(int id)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value
                   ?? User.FindFirst("role")?.Value
                   ?? "free";

        var subject = await _subjectService.GetSubjectByIdAsync(id);
        if (subject == null)
            return NotFound(new { error = "Épreuve introuvable." });

        // Paid subjects require a non-free subscription
        if (subject.Price > 0 && string.Equals(role, "free", StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { error = "Un abonnement est requis pour télécharger cette épreuve." });

        var content = subject.Contents?
            .OrderBy(c => c.OrderIndex)
            .FirstOrDefault(c => !string.IsNullOrEmpty(c.DocumentUrl));

        if (content == null || string.IsNullOrEmpty(content.DocumentUrl))
            return NotFound(new { error = "Le fichier PDF n'est pas encore disponible pour cette épreuve." });

        return Ok(new { downloadUrl = content.DocumentUrl, filename = $"{subject.Title}.pdf" });
    }

    /// <summary>
    /// Récupère les cours similaires à un cours donné
    /// </summary>
    [HttpGet("{id}/similar")]
    public async Task<IActionResult> GetSimilar(int id, [FromQuery] int limit = 5)
    {
        try
        {
            var similar = await _subjectService.GetSimilarSubjectsAsync(id, limit);
            return Ok(similar);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des cours similaires pour {SubjectId}", id);
            return StatusCode(500, "Erreur serveur");
        }
    }
}
