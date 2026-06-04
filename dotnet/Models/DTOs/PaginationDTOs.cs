using System.Collections.Generic;

namespace Backend.Models.DTOs;

/// <summary>
/// Réponse générique pour la pagination
/// </summary>
/// <typeparam name="T">Type des éléments paginés</typeparam>
public class PaginationResponse<T>
{
    /// <summary>
    /// Liste des éléments pour la page courante
    /// </summary>
    public IEnumerable<T> Items { get; set; } = new List<T>();

    /// <summary>
    /// Nombre total d'éléments (tous les pages)
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Numéro de la page courante (1-based)
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Nombre d'éléments par page
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Nombre total de pages
    /// </summary>
    public int TotalPages { get; set; }

    /// <summary>
    /// Indique s'il y a une page suivante
    /// </summary>
    public bool HasNextPage { get; set; }

    /// <summary>
    /// Indique s'il y a une page précédente
    /// </summary>
    public bool HasPreviousPage { get; set; }

    public PaginationResponse() { }

    public PaginationResponse(IEnumerable<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
        TotalPages = (int)System.Math.Ceiling(totalCount / (double)pageSize);
        HasNextPage = page < TotalPages;
        HasPreviousPage = page > 1;
    }
}

/// <summary>
/// Paramètres de pagination pour les requêtes
/// </summary>
public class PaginationParams
{
    private int _page = 1;
    private int _pageSize = 20;

    /// <summary>
    /// Numéro de la page (minimum 1)
    /// </summary>
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    /// <summary>
    /// Nombre d'éléments par page (entre 1 et 100)
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 20 : (value > 100 ? 100 : value);
    }

    /// <summary>
    /// Calcul du nombre d'éléments à ignorer (offset)
    /// </summary>
    public int Skip => (Page - 1) * PageSize;

    /// <summary>
    /// Calcul du nombre d'éléments à prendre (limit)
    /// </summary>
    public int Take => PageSize;
}
