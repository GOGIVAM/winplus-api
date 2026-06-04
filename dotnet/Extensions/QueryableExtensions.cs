using Microsoft.EntityFrameworkCore;
using Backend.Models.DTOs;

namespace Backend.Extensions;

/// <summary>
/// Extension methods for IQueryable to support pagination
/// </summary>
public static class QueryableExtensions
{
    /// <summary>
    /// Converts an IQueryable to a paginated result
    /// </summary>
    public static async Task<PaginationResponse<T>> ToPaginatedListAsync<T>(
        this IQueryable<T> source,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) where T : class
    {
        // Validate parameters
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        
        // Get total count
        var count = await source.CountAsync(cancellationToken);
        
        // Get paginated items
        var items = await source
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        
        // Create and return paginated response
        return new PaginationResponse<T>(items, count, page, pageSize);
    }

    /// <summary>
    /// Converts an IQueryable to a paginated result using PaginationParams
    /// </summary>
    public static async Task<PaginationResponse<T>> ToPaginatedListAsync<T>(
        this IQueryable<T> source,
        PaginationParams paginationParams,
        CancellationToken cancellationToken = default) where T : class
    {
        return await source.ToPaginatedListAsync(paginationParams.Page, paginationParams.PageSize, cancellationToken);
    }
}
