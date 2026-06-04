using Backend.Data;
using Backend.Models.Entities;
using Backend.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class SubjectRepository : ISubjectRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SubjectRepository> _logger;

    public SubjectRepository(ApplicationDbContext context, ILogger<SubjectRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Subject?> GetByIdAsync(int id)
    {
        try
        {
            return await _context.Subjects
                .WhereNotDeleted()
                .Include(s => s.Contents)
                .Include(s => s.Enrollments)
                .FirstOrDefaultAsync(s => s.Id == id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subject by id {SubjectId}", id);
            return null;
        }
    }

    public async Task<IEnumerable<Subject>> GetAllAsync()
    {
        try
        {
            return await _context.Subjects
                .WhereNotDeleted()
                .Include(s => s.Contents)
                .AsNoTracking()
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all subjects");
            return Enumerable.Empty<Subject>();
        }
    }

    public async Task<IEnumerable<Subject>> GetAllAsync(int page, int limit)
    {
        try
        {
            var skip = (page - 1) * limit;
            return await _context.Subjects
                .WhereNotDeleted()
                .Include(s => s.Contents)
                .AsNoTracking()
                .OrderByDescending(s => s.CreatedAt)
                .Skip(skip)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paginated subjects (page {Page}, limit {Limit})", page, limit);
            return Enumerable.Empty<Subject>();
        }
    }

    public async Task<IEnumerable<Subject>> GetPublishedAsync()
    {
        try
        {
            return await _context.Subjects
                .WhereNotDeleted()
                .Where(s => s.IsPublished)
                .Include(s => s.Contents)
                .AsNoTracking()
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting published subjects");
            return Enumerable.Empty<Subject>();
        }
    }

    public async Task<IEnumerable<Subject>> GetByCategoryAsync(string category)
    {
        try
        {
            return await _context.Subjects
                .WhereNotDeleted()
                .Where(s => s.Category == category && s.IsPublished)
                .AsNoTracking()
                .Include(s => s.Contents)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subjects by category {Category}", category);
            return Enumerable.Empty<Subject>();
        }
    }

    public async Task<IEnumerable<Subject>> SearchAsync(string searchTerm)
    {
        try
        {
            var term = searchTerm.ToLower();
            return await _context.Subjects
                .WhereNotDeleted()
                .Where(s => s.IsPublished && 
                    (s.Title.ToLower().Contains(term) || 
                     s.Description.ToLower().Contains(term)))
                .AsNoTracking()
                .Include(s => s.Contents)
                .OrderByDescending(s => s.EnrollmentCount)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching subjects with term {SearchTerm}", searchTerm);
            return Enumerable.Empty<Subject>();
        }
    }

    public async Task<Subject> CreateAsync(Subject subject)
    {
        try
        {
            subject.CreatedAt = DateTime.UtcNow;
            subject.UpdatedAt = DateTime.UtcNow;
            
            _context.Subjects.Add(subject);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Subject {SubjectId} created successfully", subject.Id);
            return subject;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subject");
            throw;
        }
    }

    public async Task<Subject> UpdateAsync(Subject subject)
    {
        try
        {
            subject.UpdatedAt = DateTime.UtcNow;
            
            _context.Subjects.Update(subject);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Subject {SubjectId} updated successfully", subject.Id);
            return subject;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating subject {SubjectId}", subject.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            var subject = await _context.Subjects.FindAsync(id);
            if (subject == null)
                return false;

            // Soft delete - mark as deleted instead of removing
            subject.IsDeleted = true;
            
            _context.Subjects.Update(subject);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Subject {SubjectId} soft deleted successfully", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting subject {SubjectId}", id);
            throw;
        }
    }

    public async Task<int> CountAsync()
    {
        try
        {
            return await _context.Subjects.WhereNotDeleted().CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting subjects");
            return 0;
        }
    }

    public async Task<int> GetCountAsync()
    {
        // Alias for CountAsync for backward compatibility
        return await CountAsync();
    }

    public async Task<IEnumerable<Subject>> GetPopularAsync(int limit = 10)
    {
        try
        {
            return await _context.Subjects
                .Include(s => s.Contents)
                .AsNoTracking()
                .OrderByDescending(s => s.EnrollmentCount)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting popular subjects");
            return Enumerable.Empty<Subject>();
        }
    }
}
