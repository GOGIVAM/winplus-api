using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

/// <summary>
/// Repository implementation for Enrollment entity
/// </summary>
public class EnrollmentRepository : GenericRepository<Enrollment>, IEnrollmentRepository
{
    public EnrollmentRepository(ApplicationDbContext context, ILogger<EnrollmentRepository> logger)
        : base(context, logger)
    {
    }

    public async Task<Enrollment?> GetByUserAndSubjectAsync(int userId, int subjectId)
    {
        try
        {
            return await Context.Set<Enrollment>()
                .Include(e => e.User)
                .Include(e => e.Subject)
                .Include(e => e.Certificate)
                .FirstOrDefaultAsync(e => e.UserId == userId && e.SubjectId == subjectId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting enrollment for user {UserId} and subject {SubjectId}", userId, subjectId);
            return null;
        }
    }

    public async Task<IEnumerable<Enrollment>> GetByUserIdAsync(int userId)
    {
        try
        {
            return await Context.Set<Enrollment>()
                .Include(e => e.Subject)
                .Include(e => e.Certificate)
                .Where(e => e.UserId == userId)
                .OrderByDescending(e => e.EnrolledAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting enrollments for user {UserId}", userId);
            return Enumerable.Empty<Enrollment>();
        }
    }

    public async Task<int> CountBySubjectAsync(int subjectId)
    {
        try
        {
            return await Context.Set<Enrollment>()
                .CountAsync(e => e.SubjectId == subjectId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error counting enrollments for subject {SubjectId}", subjectId);
            return 0;
        }
    }

    public async Task<decimal> GetAverageProgressBySubjectAsync(int subjectId)
    {
        try
        {
            var avg = await Context.Set<Enrollment>()
                .Where(e => e.SubjectId == subjectId)
                .AverageAsync(e => (double?)e.ProgressPercentage);
            return avg.HasValue ? (decimal)Math.Round(avg.Value, 2) : 0m;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error calculating average progress for subject {SubjectId}", subjectId);
            return 0m;
        }
    }

    public override async Task<Enrollment?> GetByIdAsync(int id)
    {
        try
        {
            return await Context.Set<Enrollment>()
                .Include(e => e.User)
                .Include(e => e.Subject)
                .Include(e => e.Certificate)
                .FirstOrDefaultAsync(e => e.Id == id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting enrollment {EnrollmentId}", id);
            return null;
        }
    }
}
