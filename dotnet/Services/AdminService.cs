using Backend.Models.DTOs;
using Backend.Repositories;

namespace Backend.Services;

/// <summary>
/// Interface pour le service admin
/// </summary>
public interface IAdminService
{
    Task<AdminUserListResponse> GetAllUsersAsync(int page = 1, int limit = 50);
    Task<AdminSubjectListResponse> GetAllSubjectsAsync(int page = 1, int limit = 50);
    Task<AdminOrderListResponse> GetAllOrdersAsync(int page = 1, int limit = 50);
    Task<AdminSystemStatsResponse> GetSystemStatisticsAsync();
    Task<bool> BlockUserAsync(int userId);
    Task<bool> UnblockUserAsync(int userId);
    Task<AdminDashboardResponse> GetAdminDashboardAsync();
}

/// <summary>
/// Service pour les opérations admin
/// </summary>
public class AdminService : IAdminService
{
    private readonly IUserRepository _userRepository;
    private readonly ISubjectRepository _subjectRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly ILogger<AdminService> _logger;

    public AdminService(
        IUserRepository userRepository,
        ISubjectRepository subjectRepository,
        IOrderRepository orderRepository,
        ILogger<AdminService> logger)
    {
        _userRepository = userRepository;
        _subjectRepository = subjectRepository;
        _orderRepository = orderRepository;
        _logger = logger;
    }

    /// <summary>
    /// Récupère la liste de tous les utilisateurs
    /// </summary>
    public async Task<AdminUserListResponse> GetAllUsersAsync(int page = 1, int limit = 50)
    {
        try
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 50;
            if (limit > 100) limit = 100;

            var users = await _userRepository.GetAllAsync(page, limit);
            var totalCount = await _userRepository.GetCountAsync();

            return new AdminUserListResponse
            {
                Users = users.Select(u => new AdminUserResponse
                {
                    Id = u.Id,
                    Email = u.Email,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt,
                    UpdatedAt = u.UpdatedAt,
                    LastLoginAt = u.LastLoginAt
                }).ToList(),
                Total = totalCount,
                Page = page,
                Limit = limit,
                TotalPages = (int)Math.Ceiling(totalCount / (double)limit)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all users");
            throw;
        }
    }

    /// <summary>
    /// Récupère la liste de tous les cours
    /// </summary>
    public async Task<AdminSubjectListResponse> GetAllSubjectsAsync(int page = 1, int limit = 50)
    {
        try
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 50;
            if (limit > 100) limit = 100;

            var subjects = await _subjectRepository.GetAllAsync(page, limit);
            var totalCount = await _subjectRepository.GetCountAsync();

            return new AdminSubjectListResponse
            {
                Subjects = subjects.Select(s => new AdminSubjectResponse
                {
                    Id = s.Id,
                    Title = s.Title,
                    Category = s.Category,
                    Price = s.Price,
                    AverageRating = s.AverageRating,
                    CreatedAt = s.CreatedAt
                }).ToList(),
                Total = totalCount,
                Page = page,
                Limit = limit,
                TotalPages = (int)Math.Ceiling(totalCount / (double)limit)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all subjects");
            throw;
        }
    }

    /// <summary>
    /// Récupère la liste de toutes les commandes
    /// </summary>
    public async Task<AdminOrderListResponse> GetAllOrdersAsync(int page = 1, int limit = 50)
    {
        try
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 50;
            if (limit > 100) limit = 100;

            var orders = await _orderRepository.GetAllAsync(page, limit);
            var totalCount = await _orderRepository.GetCountAsync();

            return new AdminOrderListResponse
            {
                Orders = orders.Select(o => new AdminOrderResponse
                {
                    Id = o.Id,
                    OrderNumber = o.OrderNumber,
                    UserId = o.UserId,
                    TotalAmount = o.TotalAmount,
                    Status = o.Status,
                    OrderDate = o.OrderDate,
                    CompletedDate = o.CompletedDate,
                    ItemsCount = o.Items?.Count ?? 0
                }).ToList(),
                Total = totalCount,
                Page = page,
                Limit = limit,
                TotalPages = (int)Math.Ceiling(totalCount / (double)limit)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all orders");
            throw;
        }
    }

    /// <summary>
    /// Récupère les statistiques système
    /// </summary>
    public async Task<AdminSystemStatsResponse> GetSystemStatisticsAsync()
    {
        try
        {
            var totalUsers = await _userRepository.GetCountAsync();
            var totalSubjects = await _subjectRepository.GetCountAsync();
            var totalOrders = await _orderRepository.GetCountAsync();

            return new AdminSystemStatsResponse
            {
                TotalUsers = totalUsers,
                TotalSubjects = totalSubjects,
                TotalOrders = totalOrders,
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system statistics");
            throw;
        }
    }

    /// <summary>
    /// Bloque un utilisateur
    /// </summary>
    public async Task<bool> BlockUserAsync(int userId)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("User not found for blocking: {UserId}", userId);
                return false;
            }

            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.UpdateAsync(user);
            
            _logger.LogWarning("User blocked: {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error blocking user: {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Débloque un utilisateur
    /// </summary>
    public async Task<bool> UnblockUserAsync(int userId)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("User not found for unblocking: {UserId}", userId);
                return false;
            }

            user.IsActive = true;
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.UpdateAsync(user);
            
            _logger.LogInformation("User unblocked: {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unblocking user: {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Récupère le dashboard admin avec statistiques
    /// </summary>
    public async Task<AdminDashboardResponse> GetAdminDashboardAsync()
    {
        try
        {
            var stats = await GetSystemStatisticsAsync();
            var activeUsers = await _userRepository.GetCountAsync(); // À améliorer

            return new AdminDashboardResponse
            {
                TotalUsers = stats.TotalUsers,
                TotalSubjects = stats.TotalSubjects,
                TotalOrders = stats.TotalOrders,
                SystemHealthStatus = "Healthy",
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting admin dashboard");
            throw;
        }
    }
}
