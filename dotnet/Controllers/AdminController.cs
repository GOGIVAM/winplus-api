using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Controller pour les opérations administrateur
/// </summary>
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly ILogger<AdminController> _logger;
    private readonly ApplicationDbContext _db;

    public AdminController(IAdminService adminService, ILogger<AdminController> logger, ApplicationDbContext db)
    {
        _adminService = adminService;
        _logger = logger;
        _db = db;
    }

    /// <summary>
    /// Récupère la liste de tous les utilisateurs avec recherche optionnelle
    /// </summary>
    [HttpGet("users")]
    [ProducesResponseType(typeof(AdminUserListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllUsers(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        [FromQuery] string? q = null)
    {
        try
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 50;
            if (limit > 100) limit = 100;

            var query = _db.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var lower = q.ToLower();
                query = query.Where(u =>
                    u.Email.ToLower().Contains(lower) ||
                    (u.FirstName != null && u.FirstName.ToLower().Contains(lower)) ||
                    (u.LastName != null && u.LastName.ToLower().Contains(lower)));
            }

            var total = await query.CountAsync();
            var users = await query
                .OrderByDescending(u => u.CreatedAt)
                .Skip((page - 1) * limit)
                .Take(limit)
                .Select(u => new AdminUserResponse
                {
                    Id = u.Id,
                    Email = u.Email,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    Role = u.Role,
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt,
                    UpdatedAt = u.UpdatedAt,
                    LastLoginAt = u.LastLoginAt
                })
                .ToListAsync();

            return Ok(new AdminUserListResponse
            {
                Users = users,
                Total = total,
                Page = page,
                Limit = limit,
                TotalPages = (int)Math.Ceiling(total / (double)limit)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all users");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Met à jour le rôle d'un utilisateur
    /// </summary>
    [HttpPut("users/{id}/role")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateUserRole(int id, [FromBody] UpdateUserRoleRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var user = await _db.Users.FindAsync(id);
            if (user == null)
                return NotFound(new { error = "User not found" });

            user.Role = request.Role;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Role updated for user {UserId}: {Role}", id, request.Role);
            return Ok(new { success = true, message = "Role updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating role for user {UserId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Met à jour le statut actif d'un utilisateur
    /// </summary>
    [HttpPut("users/{id}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateUserStatus(int id, [FromBody] UpdateUserStatusRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var user = await _db.Users.FindAsync(id);
            if (user == null)
                return NotFound(new { error = "User not found" });

            user.IsActive = request.IsActive;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Status updated for user {UserId}: IsActive={IsActive}", id, request.IsActive);
            return Ok(new { success = true, message = "Status updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating status for user {UserId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère la liste de tous les cours
    /// </summary>
    /// <param name="page">Numéro de page</param>
    /// <param name="limit">Nombre de résultats par page</param>
    /// <returns>Liste des cours</returns>
    /// <response code="200">Cours retournés</response>
    /// <response code="401">Non autorisé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("subjects")]
    [ProducesResponseType(typeof(AdminSubjectListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllSubjects([FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        try
        {
            var response = await _adminService.GetAllSubjectsAsync(page, limit);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all subjects");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère la liste de toutes les commandes
    /// </summary>
    /// <param name="page">Numéro de page</param>
    /// <param name="limit">Nombre de résultats par page</param>
    /// <returns>Liste des commandes</returns>
    /// <response code="200">Commandes retournées</response>
    /// <response code="401">Non autorisé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("orders")]
    [ProducesResponseType(typeof(AdminOrderListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllOrders([FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        try
        {
            var response = await _adminService.GetAllOrdersAsync(page, limit);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all orders");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les statistiques système
    /// </summary>
    /// <returns>Statistiques système</returns>
    /// <response code="200">Statistiques retournées</response>
    /// <response code="401">Non autorisé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("statistics")]
    [ProducesResponseType(typeof(AdminSystemStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSystemStatistics()
    {
        try
        {
            var response = await _adminService.GetSystemStatisticsAsync();
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system statistics");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Bloque un utilisateur
    /// </summary>
    /// <param name="userId">ID de l'utilisateur à bloquer</param>
    /// <returns>Résultat de l'opération</returns>
    /// <response code="200">Utilisateur bloqué avec succès</response>
    /// <response code="400">Requête invalide</response>
    /// <response code="401">Non autorisé</response>
    /// <response code="404">Utilisateur non trouvé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpPost("user/{userId}/block")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> BlockUser(int userId)
    {
        try
        {
            if (userId <= 0)
                return BadRequest(new { error = "Invalid user ID" });

            var result = await _adminService.BlockUserAsync(userId);
            if (!result)
                return NotFound(new { error = "User not found" });

            return Ok(new { message = "User blocked successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error blocking user: {UserId}", userId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Débloque un utilisateur
    /// </summary>
    /// <param name="userId">ID de l'utilisateur à débloquer</param>
    /// <returns>Résultat de l'opération</returns>
    /// <response code="200">Utilisateur débloqué avec succès</response>
    /// <response code="400">Requête invalide</response>
    /// <response code="401">Non autorisé</response>
    /// <response code="404">Utilisateur non trouvé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpPost("user/{userId}/unblock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UnblockUser(int userId)
    {
        try
        {
            if (userId <= 0)
                return BadRequest(new { error = "Invalid user ID" });

            var result = await _adminService.UnblockUserAsync(userId);
            if (!result)
                return NotFound(new { error = "User not found" });

            return Ok(new { message = "User unblocked successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unblocking user: {UserId}", userId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère le dashboard admin
    /// </summary>
    /// <returns>Données du dashboard</returns>
    /// <response code="200">Dashboard retourné</response>
    /// <response code="401">Non autorisé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(AdminDashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDashboard()
    {
        try
        {
            var response = await _adminService.GetAdminDashboardAsync();
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting admin dashboard");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les statistiques du dashboard
    /// </summary>
    /// <returns>Statistiques globales</returns>
    /// <response code="200">Statistiques retournées</response>
    /// <response code="401">Non autorisé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("dashboard/stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDashboardStats()
    {
        try
        {
            var stats = await _adminService.GetSystemStatisticsAsync();
            return Ok(new { data = stats, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard stats");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les activités récentes
    /// </summary>
    /// <returns>Activités récentes</returns>
    /// <response code="200">Activités retournées</response>
    /// <response code="401">Non autorisé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("activities/recent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetRecentActivities()
    {
        try
        {
            // Placeholder: nécessite une table AnalyticsEvents
            var activities = new List<dynamic>();
            return Ok(new { data = activities, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent activities");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère la santé du système
    /// </summary>
    /// <returns>État de santé</returns>
    /// <response code="200">État retourné</response>
    /// <response code="401">Non autorisé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("system/health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSystemHealth()
    {
        try
        {
            var health = new
            {
                dbHealth = "healthy",
                apiHealth = "healthy",
                cacheHealth = "healthy",
                status = "operational"
            };
            return Ok(new { data = health, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system health");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les revenus
    /// </summary>
    /// <param name="period">Période (6months, 1year, etc.)</param>
    /// <returns>Données des revenus</returns>
    /// <response code="200">Revenus retournés</response>
    /// <response code="401">Non autorisé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("analytics/revenues")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetRevenues([FromQuery] string period = "6months")
    {
        try
        {
            var orders = await _adminService.GetAllOrdersAsync(1, int.MaxValue);
            
            // Filter orders by period
            DateTime periodStart = period switch
            {
                "1year" => DateTime.UtcNow.AddYears(-1),
                "3months" => DateTime.UtcNow.AddMonths(-3),
                _ => DateTime.UtcNow.AddMonths(-6)
            };
            
            var totalRevenue = orders.Orders
                .Where(o => o.OrderDate >= periodStart)
                .Sum(o => o.TotalAmount);

            var revenueByPeriod = orders.Orders
                .Where(o => o.OrderDate >= periodStart)
                .GroupBy(o => o.OrderDate.Date)
                .Select(g => new { date = g.Key.ToString("yyyy-MM-dd"), revenue = g.Sum(o => o.TotalAmount) })
                .ToList();

            return Ok(new { data = new { totalRevenue, revenueByPeriod }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting revenues");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les utilisateurs actifs
    /// </summary>
    /// <returns>Données des utilisateurs actifs</returns>
    /// <response code="200">Données retournées</response>
    /// <response code="401">Non autorisé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("analytics/active-users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetActiveUsers()
    {
        try
        {
            var allUsers = await _adminService.GetAllUsersAsync(1, int.MaxValue);
            
            var last30Days = DateTime.UtcNow.AddDays(-30);
            var last90Days = DateTime.UtcNow.AddDays(-90);
            
            var activeUserCount = allUsers.Users
                .Where(u => u.LastLoginAt >= last30Days)
                .Count();
            
            var newUserCount = allUsers.Users
                .Where(u => u.CreatedAt >= last30Days)
                .Count();
            
            var previousActiveCount = allUsers.Users
                .Where(u => u.LastLoginAt >= last90Days && u.LastLoginAt < last30Days)
                .Count();
            
            var churnRate = previousActiveCount > 0 ? 
                ((double)(previousActiveCount - activeUserCount) / previousActiveCount * 100) : 0;

            var activeUsersData = new
            {
                activeUserCount,
                newUserCount,
                churnRate = Math.Round(churnRate, 2)
            };
            return Ok(new { data = activeUsersData, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active users");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les sujets populaires
    /// </summary>
    /// <param name="limit">Limite de résultats</param>
    /// <returns>Sujets populaires</returns>
    /// <response code="200">Sujets retournés</response>
    /// <response code="401">Non autorisé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("analytics/popular-subjects")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPopularSubjects([FromQuery] int limit = 3)
    {
        try
        {
            var subjects = await _adminService.GetAllSubjectsAsync(1, limit);
            return Ok(new { data = subjects, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting popular subjects");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Métriques analytiques clés pour le tableau de bord admin
    /// </summary>
    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics()
    {
        try
        {
            var totalRevenue = await _db.Payments
                .Where(p => p.Status == "completed")
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;

            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
            var newUsersThisWeek = await _db.Users
                .CountAsync(u => u.CreatedAt > sevenDaysAgo);

            var activeSubjects = await _db.Subjects
                .CountAsync(s => s.IsPublished && !s.IsDeleted);

            var totalUsers = await _db.Users.CountAsync();
            var subscribedUsers = await _db.Subscriptions
                .Select(s => s.UserId)
                .Distinct()
                .CountAsync();

            var conversionRate = totalUsers > 0
                ? Math.Round((double)subscribedUsers / totalUsers * 100, 2)
                : 0.0;

            return Ok(new
            {
                success = true,
                totalRevenue,
                newUsersThisWeek,
                activeSubjects,
                conversionRate,
                totalUsers,
                subscribedUsers
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting analytics");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère le taux de conversion
    /// </summary>
    /// <returns>Taux de conversion</returns>
    /// <response code="200">Taux retourné</response>
    /// <response code="401">Non autorisé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("analytics/conversion-rate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetConversionRate()
    {
        try
        {
            var allUsers = await _adminService.GetAllUsersAsync(1, int.MaxValue);
            var allOrders = await _adminService.GetAllOrdersAsync(1, int.MaxValue);
            
            var totalUsers = allUsers.Users.Count();
            var purchasingUsers = allOrders.Orders
                .Select(o => o.UserId)
                .Distinct()
                .Count();
            
            var conversionRate = totalUsers > 0 ? 
                ((double)purchasingUsers / totalUsers * 100) : 0;
            
            // Using average orders per user as a proxy for cart abandonment
            var ordersPerUser = totalUsers > 0 ? (double)allOrders.Orders.Count / totalUsers : 0;
            var cartAbandonmentRate = ordersPerUser > 0 ? 
                ((ordersPerUser - 1) / ordersPerUser * 100) : 0;
            
            var avgOrderValue = allOrders.Orders.Any() ? 
                allOrders.Orders.Average(o => o.TotalAmount) : 0;

            var conversionData = new
            {
                conversionRate = Math.Round(conversionRate, 2),
                cartAbandonmentRate = Math.Round(cartAbandonmentRate, 2),
                avgOrderValue = Math.Round(avgOrderValue, 2)
            };
            return Ok(new { data = conversionData, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting conversion rate");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
