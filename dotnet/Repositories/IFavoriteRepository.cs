using Backend.Models.Entities;

namespace Backend.Repositories;

public interface IFavoriteRepository
{
    Task<Favorite?> GetByIdAsync(int id);
    Task<IEnumerable<Favorite>> GetByUserIdAsync(int userId);
    Task<Favorite?> GetByUserAndSubjectAsync(int userId, int subjectId);
    Task<Favorite> AddAsync(Favorite favorite);
    Task<Favorite> UpdateAsync(Favorite favorite);
    Task<bool> RemoveAsync(int id);
    Task<bool> RemoveByUserAndSubjectAsync(int userId, int subjectId);
    Task<bool> ExistsAsync(int userId, int subjectId);
    Task<int> GetCountByUserAsync(int userId);
}
