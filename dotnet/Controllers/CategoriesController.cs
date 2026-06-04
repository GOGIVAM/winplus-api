using Microsoft.AspNetCore.Mvc;
using Backend.Services;
using Backend.Models.DTOs;

namespace Backend.Controllers;

/// <summary>
/// Controller pour gérer les catégories de cours
/// ✅ CORRIGÉ: Données lues depuis la BD au lieu de hardcodage
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;
    private readonly ILogger<CategoriesController> _logger;

    public CategoriesController(
        ICategoryService categoryService,
        ILogger<CategoriesController> logger)
    {
        _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Obtenir toutes les catégories d'examens
    /// GET /api/categories
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(CategoriesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories()
    {
        try
        {
            _logger.LogInformation("Récupération des catégories");
            var response = await _categoryService.GetAllCategoriesAsync();
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting categories");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Obtenir les catégories d'examens
    /// GET /api/categories/exams
    /// </summary>
    [HttpGet("exams")]
    [ProducesResponseType(typeof(List<CategoryItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExamCategories()
    {
        try
        {
            _logger.LogInformation("Récupération des catégories d'examens");
            var exams = await _categoryService.GetExamCategoriesAsync();
            return Ok(exams);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting exam categories");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Obtenir les matières disponibles
    /// GET /api/categories/subjects
    /// </summary>
    [HttpGet("subjects")]
    [ProducesResponseType(typeof(List<CategoryItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSubjects()
    {
        try
        {
            _logger.LogInformation("Récupération des matières");
            var subjects = await _categoryService.GetSubjectsAsync();
            return Ok(subjects);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subjects");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Obtenir les niveaux de difficulté
    /// GET /api/categories/difficulties
    /// </summary>
    [HttpGet("difficulties")]
    [ProducesResponseType(typeof(List<CategoryItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDifficulties()
    {
        try
        {
            _logger.LogInformation("Récupération des niveaux de difficulté");
            var difficulties = await _categoryService.GetDifficultiesAsync();
            return Ok(difficulties);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting difficulties");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Obtenir les années disponibles
    /// GET /api/categories/years
    /// </summary>
    [HttpGet("years")]
    [ProducesResponseType(typeof(List<CategoryItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetYears()
    {
        try
        {
            _logger.LogInformation("Récupération des années");
            var years = await _categoryService.GetYearsAsync();
            return Ok(years);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting years");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Obtenir les filtres disponibles
    /// GET /api/categories/filters
    /// </summary>
    [HttpGet("filters")]
    [ProducesResponseType(typeof(FiltersResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFilters()
    {
        try
        {
            _logger.LogInformation("Récupération des filtres disponibles");
            var response = await _categoryService.GetFiltersAsync();
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting filters");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Obtenir une catégorie spécifique par ID
    /// GET /api/categories/{id}
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(CategoryDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCategoryById(int id)
    {
        try
        {
            _logger.LogInformation("Récupération de la catégorie {Id}", id);
            var category = await _categoryService.GetCategoryByIdAsync(id);
            
            if (category == null)
                return NotFound(new { message = "Catégorie non trouvée" });

            return Ok(category);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting category by ID");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}

// DTOs pour les réponses
public class CategoriesResponse
{
    public List<CategoryItem> ExamTypes { get; set; } = new();
    public List<CategoryItem> Subjects { get; set; } = new();
    public List<CategoryItem> Difficulties { get; set; } = new();
    public List<CategoryItem> Years { get; set; } = new();
}

public class CategoryItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Count { get; set; }
}

public class FiltersResponse
{
    public List<string> ExamTypes { get; set; } = new();
    public List<string> Subjects { get; set; } = new();
    public List<string> Difficulties { get; set; } = new();
    public List<string> Years { get; set; } = new();
    public List<PriceRange> PriceRanges { get; set; } = new();
    public List<RatingFilter> Ratings { get; set; } = new();
}

public class PriceRange
{
    public decimal Min { get; set; }
    public decimal Max { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class RatingFilter
{
    public int MinStars { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class CategoryDetailResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SubjectCount { get; set; }
    public int CourseCount { get; set; }
    public int StudentCount { get; set; }
}
