using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

/// <summary>
/// Generic repository implementation for basic CRUD operations
/// </summary>
/// <typeparam name="TEntity">The entity type</typeparam>
public class GenericRepository<TEntity> : IRepository<TEntity> where TEntity : class
{
    protected readonly ApplicationDbContext Context;
    protected readonly ILogger<GenericRepository<TEntity>> Logger;

    public GenericRepository(ApplicationDbContext context, ILogger<GenericRepository<TEntity>> logger)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public virtual async Task<IEnumerable<TEntity>> GetAllAsync()
    {
        try
        {
            return await Context.Set<TEntity>()
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting all entities of type {EntityType}", typeof(TEntity).Name);
            return Enumerable.Empty<TEntity>();
        }
    }

    public virtual async Task<TEntity?> GetByIdAsync(int id)
    {
        try
        {
            return await Context.Set<TEntity>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => EF.Property<int>(x, "Id") == id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting entity of type {EntityType} by id {Id}", typeof(TEntity).Name, id);
            return null;
        }
    }

    public virtual async Task<TEntity> CreateAsync(TEntity entity)
    {
        try
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            var result = await Context.Set<TEntity>().AddAsync(entity);
            await Context.SaveChangesAsync();
            return result.Entity;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating entity of type {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual async Task<TEntity> UpdateAsync(TEntity entity)
    {
        try
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            Context.Set<TEntity>().Update(entity);
            await Context.SaveChangesAsync();
            return entity;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating entity of type {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual async Task<bool> DeleteAsync(int id)
    {
        try
        {
            var entity = await GetByIdAsync(id);
            if (entity == null)
                return false;

            Context.Set<TEntity>().Remove(entity);
            await Context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting entity of type {EntityType} by id {Id}", typeof(TEntity).Name, id);
            return false;
        }
    }

    public virtual async Task<bool> ExistsAsync(int id)
    {
        try
        {
            var entity = await GetByIdAsync(id);
            return entity != null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error checking existence of entity of type {EntityType} by id {Id}", typeof(TEntity).Name, id);
            return false;
        }
    }

    public virtual async Task<int> CountAsync()
    {
        try
        {
            return await Context.Set<TEntity>().CountAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error counting entities of type {EntityType}", typeof(TEntity).Name);
            return 0;
        }
    }
}
