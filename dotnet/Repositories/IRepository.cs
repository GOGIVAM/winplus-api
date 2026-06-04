namespace Backend.Repositories;

/// <summary>
/// Generic repository interface for CRUD operations
/// </summary>
/// <typeparam name="TEntity">The entity type</typeparam>
public interface IRepository<TEntity> where TEntity : class
{
    /// <summary>
    /// Get all entities
    /// </summary>
    Task<IEnumerable<TEntity>> GetAllAsync();

    /// <summary>
    /// Get entity by id
    /// </summary>
    Task<TEntity?> GetByIdAsync(int id);

    /// <summary>
    /// Create new entity
    /// </summary>
    Task<TEntity> CreateAsync(TEntity entity);

    /// <summary>
    /// Update existing entity
    /// </summary>
    Task<TEntity> UpdateAsync(TEntity entity);

    /// <summary>
    /// Delete entity
    /// </summary>
    Task<bool> DeleteAsync(int id);

    /// <summary>
    /// Check if entity exists
    /// </summary>
    Task<bool> ExistsAsync(int id);

    /// <summary>
    /// Count all entities
    /// </summary>
    Task<int> CountAsync();
}
