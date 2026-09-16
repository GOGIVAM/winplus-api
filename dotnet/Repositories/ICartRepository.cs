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

    // ── Panier anonyme (avant connexion), persisté en base par DeviceId ──────
    Task<IEnumerable<CartItem>> GetByDeviceIdAsync(string deviceId);
    Task<CartItem?> GetByDeviceAndSubjectAsync(string deviceId, int subjectId);
    Task<bool> RemoveByDeviceAndSubjectAsync(string deviceId, int subjectId);
    Task<bool> ClearDeviceCartAsync(string deviceId);

    /// <summary>
    /// Réassigne en base les items anonymes d'un DeviceId à un UserId (connexion).
    /// Un item déjà présent pour ce UserId et ce SubjectId n'est pas dupliqué : le
    /// doublon anonyme est simplement supprimé plutôt que de violer l'index unique
    /// (UserId, SubjectId).
    /// </summary>
    Task<int> ReassignDeviceCartToUserAsync(string deviceId, int userId);
}
