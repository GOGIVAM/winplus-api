using Backend.Models.Entities;

namespace Backend.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(int id);
    Task<User?> GetByIdAsync(int id, bool includeDeleted = false);
    Task<User?> GetByEmailAsync(string email);
    Task<IEnumerable<User>> GetAllAsync();
    Task<IEnumerable<User>> GetAllAsync(int page, int limit);
    Task<User> CreateAsync(User user);
    Task<User> UpdateAsync(User user);
    Task<bool> DeleteAsync(int id);
    Task<bool> SoftDeleteAsync(int userId, int deletedBy);
    Task<bool> RestoreAsync(int userId);
    Task<bool> HardDeleteAsync(int userId);
    Task<bool> ExistsByEmailAsync(string email);
    Task<int> CountAsync();
    Task<int> GetCountAsync();
}
