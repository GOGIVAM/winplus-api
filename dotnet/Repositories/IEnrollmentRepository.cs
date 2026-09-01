using Backend.Models.Entities;

namespace Backend.Repositories;

/// <summary>
/// Repository interface for Enrollment entity operations
/// </summary>
public interface IEnrollmentRepository : IRepository<Enrollment>
{
    Task<Enrollment?> GetByUserAndSubjectAsync(int userId, int subjectId);
    Task<IEnumerable<Enrollment>> GetByUserIdAsync(int userId);
    Task<int> CountBySubjectAsync(int subjectId);
    Task<decimal> GetAverageProgressBySubjectAsync(int subjectId);
}
