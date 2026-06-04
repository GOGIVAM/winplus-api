using Backend.Models.Entities;
using Backend.Repositories;

namespace Backend.Services;

public interface IUserService
{
    Task<User?> GetUserByIdAsync(int id);
    Task<User?> GetUserByEmailAsync(string email);
    Task<User?> GetUserByCognitoIdAsync(string cognitoId);
    Task<IEnumerable<User>> GetAllUsersAsync();
    Task<IEnumerable<User>> GetAllUsersAsync(int page, int limit);
    Task<User> CreateUserAsync(User user);
    Task<User> UpdateUserAsync(User user);
    Task<bool> DeleteUserAsync(int id);
    Task<bool> SoftDeleteUserAsync(int userId, int deletedBy);
    Task<bool> RestoreUserAsync(int userId);
    Task<bool> HardDeleteUserAsync(int userId);
    Task<bool> IsEmailAvailableAsync(string email);
    Task<bool> IsCognitoIdAvailableAsync(string cognitoId);
    Task<int> GetTotalUsersCountAsync();
    Task<User> UpdateUserProfileAsync(int userId, string firstName, string lastName, string bio, string? profileImageUrl);
    Task<Dictionary<string, object>> GetUserStatisticsAsync(int userId);
    Task<Dictionary<string, object>> GetProfileStatisticsAsync();
}

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UserService> _logger;

    public UserService(IUserRepository userRepository, ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<User?> GetUserByIdAsync(int id)
    {
        try
        {
            return await _userRepository.GetByIdAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by id {UserId}", id);
            return null;
        }
    }

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        try
        {
            return await _userRepository.GetByEmailAsync(email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by email {Email}", email);
            return null;
        }
    }

    public async Task<User?> GetUserByCognitoIdAsync(string cognitoId)
    {
        try
        {
            return await _userRepository.GetByCognitoIdAsync(cognitoId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by cognito id");
            return null;
        }
    }

    public async Task<IEnumerable<User>> GetAllUsersAsync()
    {
        try
        {
            return await _userRepository.GetAllAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all users");
            return Enumerable.Empty<User>();
        }
    }

    public async Task<IEnumerable<User>> GetAllUsersAsync(int page, int limit)
    {
        try
        {
            if (page < 1) page = 1;
            if (limit < 1 || limit > 100) limit = 20;

            var skip = (page - 1) * limit;
            var users = await _userRepository.GetAllAsync();
            return users.Skip(skip).Take(limit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paginated users (page {Page}, limit {Limit})", page, limit);
            return Enumerable.Empty<User>();
        }
    }

    public async Task<User> CreateUserAsync(User user)
    {
        try
        {
            if (string.IsNullOrEmpty(user.Email))
                throw new ArgumentException("Email is required");

            var emailExists = await _userRepository.ExistsByEmailAsync(user.Email);
            if (emailExists)
                throw new InvalidOperationException($"User with email {user.Email} already exists");

            user.IsActive = true;
            return await _userRepository.CreateAsync(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user");
            throw;
        }
    }

    public async Task<User> UpdateUserAsync(User user)
    {
        try
        {
            return await _userRepository.UpdateAsync(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user {UserId}", user.Id);
            throw;
        }
    }

    public async Task<bool> DeleteUserAsync(int id)
    {
        try
        {
            return await _userRepository.DeleteAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", id);
            throw;
        }
    }

    public async Task<bool> SoftDeleteUserAsync(int userId, int deletedBy)
    {
        try
        {
            return await _userRepository.SoftDeleteAsync(userId, deletedBy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error soft deleting user {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> RestoreUserAsync(int userId)
    {
        try
        {
            return await _userRepository.RestoreAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring user {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> HardDeleteUserAsync(int userId)
    {
        try
        {
            return await _userRepository.HardDeleteAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error hard deleting user {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> IsEmailAvailableAsync(string email)
    {
        try
        {
            var exists = await _userRepository.ExistsByEmailAsync(email);
            return !exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking email availability");
            return false;
        }
    }

    public async Task<bool> IsCognitoIdAvailableAsync(string cognitoId)
    {
        try
        {
            var exists = await _userRepository.ExistsByCognitoIdAsync(cognitoId);
            return !exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking cognito id availability");
            return false;
        }
    }

    public async Task<int> GetTotalUsersCountAsync()
    {
        try
        {
            return await _userRepository.CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting users");
            return 0;
        }
    }

    public async Task<User> UpdateUserProfileAsync(int userId, string firstName, string lastName, string bio, string? profileImageUrl)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                throw new InvalidOperationException($"User {userId} not found");

            user.FirstName = firstName;
            user.LastName = lastName;
            user.Bio = bio;
            if (!string.IsNullOrEmpty(profileImageUrl))
                user.ProfileImageUrl = profileImageUrl;

            return await _userRepository.UpdateAsync(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user profile {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Récupère les statistiques d'un utilisateur spécifique
    /// </summary>
    public async Task<Dictionary<string, object>> GetUserStatisticsAsync(int userId)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                throw new InvalidOperationException($"User {userId} not found");

            var stats = new Dictionary<string, object>
            {
                { "userId", user.Id },
                { "email", user.Email },
                { "firstName", user.FirstName ?? string.Empty },
                { "lastName", user.LastName ?? string.Empty },
                { "totalEnrollments", 0 },
                { "totalCoursesCompleted", 0 },
                { "averageProgress", 0.0 },
                { "joinDate", user.CreatedAt },
                { "lastActive", user.UpdatedAt },
                { "isActive", user.IsActive }
            };

            _logger.LogInformation("Retrieved statistics for user {UserId}", userId);
            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user statistics for {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Récupère les statistiques de profil global (pour le profil utilisateur courant)
    /// </summary>
    public async Task<Dictionary<string, object>> GetProfileStatisticsAsync()
    {
        try
        {
            var totalUsers = await _userRepository.CountAsync();
            var allUsers = await _userRepository.GetAllAsync();

            var stats = new Dictionary<string, object>
            {
                { "totalUsers", totalUsers },
                { "activeUsers", allUsers.Count(u => u.IsActive) },
                { "registeredThisMonth", allUsers.Count(u => u.CreatedAt.Month == DateTime.UtcNow.Month && u.CreatedAt.Year == DateTime.UtcNow.Year) },
                { "averageEnrollments", 0 },
                { "platformActivity", new { lastUpdated = DateTime.UtcNow } }
            };

            _logger.LogInformation("Retrieved profile statistics");
            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving profile statistics");
            throw;
        }
    }
}
