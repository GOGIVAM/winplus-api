using System.Linq.Expressions;
using Backend.Models.Entities;

namespace Backend.Extensions;

/// <summary>
/// Extension methods pour les soft deletes
/// </summary>
public static class SoftDeleteExtensions
{
    /// <summary>
    /// Filtre les entités non supprimées
    /// </summary>
    public static IQueryable<User> WhereNotDeleted(this IQueryable<User> query)
    {
        return query.Where(u => !u.IsDeleted);
    }

    /// <summary>
    /// Filtre les commandes non supprimées
    /// </summary>
    public static IQueryable<Order> WhereNotDeleted(this IQueryable<Order> query)
    {
        return query.Where(o => !o.IsDeleted);
    }

    /// <summary>
    /// Filtre les sujets non supprimés
    /// </summary>
    public static IQueryable<Subject> WhereNotDeleted(this IQueryable<Subject> query)
    {
        return query.Where(s => !s.IsDeleted);
    }

    /// <summary>
    /// Récupère seulement les entités supprimées
    /// </summary>
    public static IQueryable<User> WhereDeleted(this IQueryable<User> query)
    {
        return query.Where(u => u.IsDeleted);
    }

    /// <summary>
    /// Récupère seulement les commandes supprimées
    /// </summary>
    public static IQueryable<Order> WhereDeleted(this IQueryable<Order> query)
    {
        return query.Where(o => o.IsDeleted);
    }

    /// <summary>
    /// Récupère seulement les sujets supprimés
    /// </summary>
    public static IQueryable<Subject> WhereDeleted(this IQueryable<Subject> query)
    {
        return query.Where(s => s.IsDeleted);
    }
}

/// <summary>
/// Service pour gérer les soft deletes
/// </summary>
public interface ISoftDeleteService<T> where T : class
{
    Task<bool> DeleteAsync(int id);
    Task<bool> RestoreAsync(int id);
    Task<bool> PermanentlyDeleteAsync(int id);
}

/// <summary>
/// Implémentation générique du service soft delete
/// </summary>
public abstract class SoftDeleteService<T> : ISoftDeleteService<T> where T : class
{
    protected readonly ILogger<SoftDeleteService<T>> _logger;

    protected SoftDeleteService(ILogger<SoftDeleteService<T>> logger)
    {
        _logger = logger;
    }

    public virtual async Task<bool> DeleteAsync(int id)
    {
        _logger.LogInformation("Soft deleting entity {EntityType} with id {Id}", typeof(T).Name, id);
        // Implémenté par les classes dérivées
        return await Task.FromResult(false);
    }

    public virtual async Task<bool> RestoreAsync(int id)
    {
        _logger.LogInformation("Restoring entity {EntityType} with id {Id}", typeof(T).Name, id);
        // Implémenté par les classes dérivées
        return await Task.FromResult(false);
    }

    public virtual async Task<bool> PermanentlyDeleteAsync(int id)
    {
        _logger.LogWarning("Permanently deleting entity {EntityType} with id {Id}", typeof(T).Name, id);
        // Implémenté par les classes dérivées
        return await Task.FromResult(false);
    }
}
