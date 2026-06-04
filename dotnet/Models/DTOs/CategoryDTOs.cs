namespace Backend.Models.DTOs;

/// <summary>
/// DTO pour un élément de catégorie (valeur simple)
/// </summary>
public class CategoryItem
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Code { get; set; }
    public int? Count { get; set; }
    public bool? IsActive { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// DTO pour la réponse groupée de toutes les catégories
/// </summary>
public class CategoriesResponse
{
    public List<CategoryItem> ExamTypes { get; set; } = new();
    public List<CategoryItem> Subjects { get; set; } = new();
    public List<CategoryItem> Difficulties { get; set; } = new();
    public List<CategoryItem> Years { get; set; } = new();
}

/// <summary>
/// DTO pour la réponse des filtres disponibles
/// </summary>
public class FiltersResponse
{
    public List<CategoryItem>? ExamTypes { get; set; } = new();
    public List<CategoryItem>? Subjects { get; set; } = new();
    public List<CategoryItem>? Levels { get; set; } = new();
    public List<CategoryItem>? Difficulties { get; set; } = new();
    public List<CategoryItem>? Years { get; set; } = new();
    public List<CategoryItem>? Languages { get; set; } = new();
    public List<PriceRange>? PriceRanges { get; set; } = new();
    public List<RatingFilter>? Ratings { get; set; } = new();
}

/// <summary>
/// DTO pour les plages de prix
/// </summary>
public class PriceRange
{
    public decimal Min { get; set; }
    public decimal Max { get; set; }
    public string? Label { get; set; }
}

/// <summary>
/// DTO pour les filtres de notation
/// </summary>
public class RatingFilter
{
    public int MinStars { get; set; }
    public string? Label { get; set; }
}

/// <summary>
/// DTO pour la réponse détaillée d'une catégorie
/// </summary>
public class CategoryDetailResponse
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Code { get; set; }
    public string? Description { get; set; }
    public int? ParentCategoryId { get; set; }
    public bool IsActive { get; set; }
    public int ItemCount { get; set; }
    public int SubjectCount { get; set; }
    public int CourseCount { get; set; }
    public int StudentCount { get; set; }
    public List<CategoryItem> SubCategories { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
