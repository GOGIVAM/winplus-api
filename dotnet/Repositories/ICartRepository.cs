using Backend.Models.Entities;

namespace Backend.Repositories;

public interface ICartRepository
{
    Task<CartItem?> GetByIdAsync(int id);
    Task<IEnumerable<CartItem>> GetByUserIdAsync(int userId);
    Task<CartItem?> GetByUserAndSubjectAsync(int userId, int subjectId);
    Task<CartItem> AddAsync(CartItem cartItem);
    Task<CartItem> UpdateAsync(CartItem cartItem);
    Task<bool> RemoveAsync(int id);
    Task<bool> RemoveByUserAndSubjectAsync(int userId, int subjectId);
    Task<bool> ClearUserCartAsync(int userId);
    Task<decimal> GetTotalAsync(int userId);
    Task<int> GetCountAsync(int userId);
}
