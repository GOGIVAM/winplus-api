using Backend.Models.DTOs;

namespace Backend.Services;

/// <summary>
/// Interface pour les services de catégories
/// </summary>
public interface ICategoryService
{
    /// <summary>
    /// Récupère toutes les catégories groupées
    /// </summary>
    Task<CategoriesResponse> GetAllCategoriesAsync();

    /// <summary>
    /// Récupère les catégories d'examens
    /// </summary>
    Task<List<CategoryItem>> GetExamCategoriesAsync();

    /// <summary>
    /// Récupère les matières
    /// </summary>
    Task<List<CategoryItem>> GetSubjectsAsync();

    /// <summary>
    /// Récupère les niveaux de difficulté
    /// </summary>
    Task<List<CategoryItem>> GetDifficultiesAsync();

    /// <summary>
    /// Récupère les années disponibles
    /// </summary>
    Task<List<CategoryItem>> GetYearsAsync();

    /// <summary>
    /// Récupère les filtres disponibles
    /// </summary>
    Task<FiltersResponse> GetFiltersAsync();

    /// <summary>
    /// Récupère une catégorie par ID
    /// </summary>
    Task<CategoryDetailResponse?> GetCategoryByIdAsync(int id);
}
