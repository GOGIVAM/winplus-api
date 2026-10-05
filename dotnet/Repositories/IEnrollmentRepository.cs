using Backend.Models.Entities;

namespace Backend.Repositories;

/// <summary>
/// Repository interface for Enrollment entity operations
/// </summary>
public interface IEnrollmentRepository : IRepository<Enrollment>
{
    Task<Enrollment?> GetByUserAndSubjectAsync(int userId, int subjectId);

    /// <summary>
    /// Module 21 : même recherche, mais y compris une inscription désinscrite
    /// (suppression logique). Sert à réactiver la ligne existante à une
    /// réinscription plutôt que d'en créer une seconde, ce que l'index unique
    /// (UserId, SubjectId) interdit désormais explicitement pour les lignes
    /// actives (voir ApplicationDbContext, filtre partiel sur l'index).
    /// </summary>
    Task<Enrollment?> GetAnyByUserAndSubjectAsync(int userId, int subjectId);
    Task<IEnumerable<Enrollment>> GetByUserIdAsync(int userId);
    Task<int> CountBySubjectAsync(int subjectId);
    Task<decimal> GetAverageProgressBySubjectAsync(int subjectId);
}
