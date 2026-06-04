using Backend.Data;
using Backend.Models.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Service pour la gestion des catégories depuis la BD
/// </summary>
public class CategoryService : ICategoryService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CategoryService> _logger;

    public CategoryService(ApplicationDbContext context, ILogger<CategoryService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Récupère toutes les catégories groupées
    /// </summary>
    public async Task<CategoriesResponse> GetAllCategoriesAsync()
    {
        try
        {
            var response = new CategoriesResponse
            {
                ExamTypes = await GetExamCategoriesAsync(),
                Subjects = await GetSubjectsAsync(),
                Difficulties = await GetDifficultiesAsync(),
                Years = await GetYearsAsync()
            };

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all categories");
            throw;
        }
    }

    /// <summary>
    /// Récupère les catégories d'examens
    /// </summary>
    public async Task<List<CategoryItem>> GetExamCategoriesAsync()
    {
        try
        {
            // Récupérer les types d'examens distincts depuis la table Exams
            var examTypes = await _context.Exams
                .Where(e => !e.IsDeleted && e.IsPublished)
                .Select(e => e.ExamType)
                .Distinct()
                .OrderBy(t => t)
                .ToListAsync();

            var examList = examTypes
                .Select((examType, index) => new CategoryItem
                {
                    Id = index + 1,
                    Name = (examType != null ? examType.ToLower().Replace(" ", "-") : $"exam-{index}"),
                    DisplayName = examType ?? $"Examen {index}",
                    Description = $"Cours et épreuves pour {examType}",
                    Count = _context.Exams.Count(e => e.ExamType == examType && !e.IsDeleted)
                })
                .ToList();

            return examList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting exam categories");
            // Fallback to default values in case of error
            return GetDefaultExamCategories();
        }
    }

    /// <summary>
    /// Récupère les matières
    /// </summary>
    public async Task<List<CategoryItem>> GetSubjectsAsync()
    {
        try
        {
            // Récupérer les catégories de sujets distincts depuis la table Subjects
            var subjectCategories = await _context.Subjects
                .Where(s => !s.IsDeleted && s.IsPublished)
                .Select(s => s.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            var subjectList = subjectCategories
                .Select((category, index) => new CategoryItem
                {
                    Id = index + 1,
                    Name = category?.ToLower().Replace(" ", "-") ?? $"subject-{index}",
                    DisplayName = category ?? $"Matière {index}",
                    Description = $"Tous les cours et épreuves de {category}",
                    Count = _context.Subjects.Count(s => s.Category == category && !s.IsDeleted)
                })
                .ToList();

            return subjectList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subjects");
            return GetDefaultSubjects();
        }
    }

    /// <summary>
    /// Récupère les niveaux de difficulté
    /// </summary>
    public async Task<List<CategoryItem>> GetDifficultiesAsync()
    {
        try
        {
            // Récupérer les niveaux depuis la table Levels
            var levels = await _context.Levels
                .Where(l => l.IsActive)
                .OrderBy(l => l.Order)
                .Select(l => new CategoryItem
                {
                    Id = l.Id,
                    Name = l.Name != null ? l.Name.ToLower().Replace(" ", "-") : $"level-{l.Id}",
                    DisplayName = l.DisplayName ?? l.Name ?? $"Niveau {l.Id}",
                    Description = l.Description,
                    Count = _context.Exams.Count(e => e.Level == l.Name && !e.IsDeleted)
                })
                .ToListAsync();

            // Si pas de niveaux en DB, retourner les difficultés par défaut
            if (!levels.Any())
            {
                return GetDefaultDifficulties();
            }

            return levels;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting difficulties");
            return GetDefaultDifficulties();
        }
    }

    /// <summary>
    /// Récupère les années disponibles
    /// </summary>
    public async Task<List<CategoryItem>> GetYearsAsync()
    {
        try
        {
            // Récupérer les années distinctes des examens
            var years = await _context.Exams
                .Where(e => !e.IsDeleted && e.IsPublished)
                .Select(e => e.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .ToListAsync();

            var yearList = years
                .Select((year, index) => new CategoryItem
                {
                    Id = index + 1,
                    Name = year.ToString(),
                    DisplayName = $"Année {year}",
                    Description = $"Épreuves de {year}",
                    Count = _context.Exams.Count(e => e.Year == year && !e.IsDeleted)
                })
                .ToList();

            return yearList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting years");
            return GetDefaultYears();
        }
    }

    /// <summary>
    /// Récupère les filtres disponibles
    /// </summary>
    public async Task<FiltersResponse> GetFiltersAsync()
    {
        try
        {
            var response = new FiltersResponse
            {
                ExamTypes = await GetExamCategoriesAsync(),
                Subjects = await GetSubjectsAsync(),
                Difficulties = await GetDifficultiesAsync(),
                Years = await GetYearsAsync(),
                PriceRanges = new List<PriceRange>
                {
                    new PriceRange { Min = 0, Max = 0, Label = "Gratuit" },
                    new PriceRange { Min = 1, Max = 5000, Label = "0-5000 FCFA" },
                    new PriceRange { Min = 5001, Max = 10000, Label = "5000-10000 FCFA" },
                    new PriceRange { Min = 10001, Max = 50000, Label = "10000-50000 FCFA" },
                    new PriceRange { Min = 50001, Max = 999999, Label = "50000+ FCFA" }
                },
                Ratings = new List<RatingFilter>
                {
                    new RatingFilter { MinStars = 4, Label = "4+ étoiles" },
                    new RatingFilter { MinStars = 3, Label = "3+ étoiles" },
                    new RatingFilter { MinStars = 2, Label = "2+ étoiles" }
                }
            };

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting filters");
            throw;
        }
    }

    /// <summary>
    /// Récupère une catégorie par ID
    /// </summary>
    public async Task<CategoryDetailResponse?> GetCategoryByIdAsync(int id)
    {
        try
        {
            var level = await _context.Levels
                .FirstOrDefaultAsync(l => l.Id == id && l.IsActive);

            if (level == null)
                return null;

            var examCount = await _context.Exams
                .CountAsync(e => e.Level == level.Name && !e.IsDeleted);

            var subjectCount = await _context.Subjects
                .CountAsync(s => !s.IsDeleted && s.IsPublished);

            return new CategoryDetailResponse
            {
                Id = level.Id,
                Name = level.DisplayName,
                Description = level.Description ?? $"Tous les cours et épreuves pour {level.DisplayName}",
                SubjectCount = subjectCount,
                CourseCount = examCount,
                StudentCount = 0 // À calculer depuis les enrollments si nécessaire
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting category by ID {Id}", id);
            return null;
        }
    }

    /// <summary>
    /// Valeurs par défaut si la BD est vide
    /// </summary>
    private static List<CategoryItem> GetDefaultExamCategories()
    {
        return new List<CategoryItem>
        {
            new CategoryItem { Id = 1, Name = "bepc", DisplayName = "BEPC", Description = "Brevet d'études du premier cycle" },
            new CategoryItem { Id = 2, Name = "probatoire", DisplayName = "Probatoire", Description = "Examen Probatoire" },
            new CategoryItem { Id = 3, Name = "baccalaureat", DisplayName = "Baccalauréat", Description = "Baccalauréat Camerounais" }
        };
    }

    private static List<CategoryItem> GetDefaultSubjects()
    {
        return new List<CategoryItem>
        {
            new CategoryItem { Id = 1, Name = "mathematiques", DisplayName = "Mathématiques", Description = "Mathématiques" },
            new CategoryItem { Id = 2, Name = "physique", DisplayName = "Physique", Description = "Physique" },
            new CategoryItem { Id = 3, Name = "chimie", DisplayName = "Chimie", Description = "Chimie" },
            new CategoryItem { Id = 4, Name = "francais", DisplayName = "Français", Description = "Français" },
            new CategoryItem { Id = 5, Name = "anglais", DisplayName = "Anglais", Description = "Anglais" }
        };
    }

    private static List<CategoryItem> GetDefaultDifficulties()
    {
        return new List<CategoryItem>
        {
            new CategoryItem { Id = 1, Name = "debutant", DisplayName = "Débutant", Description = "Idéal pour débuter" },
            new CategoryItem { Id = 2, Name = "intermediaire", DisplayName = "Intermédiaire", Description = "Contenu de niveau moyen" },
            new CategoryItem { Id = 3, Name = "avance", DisplayName = "Avancé", Description = "Pour approfondir" },
            new CategoryItem { Id = 4, Name = "expert", DisplayName = "Expert", Description = "Préparation intensive" }
        };
    }

    private static List<CategoryItem> GetDefaultYears()
    {
        return Enumerable.Range(2020, 5)
            .OrderByDescending(y => y)
            .Select((y, i) => new CategoryItem
            {
                Id = i + 1,
                Name = y.ToString(),
                DisplayName = $"Année {y}",
                Description = $"Épreuves de {y}"
            })
            .ToList();
    }
}
