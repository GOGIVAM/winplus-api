using System;
using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

/// <summary>
/// DTO pour un utilisateur dans la liste admin
/// </summary>
public class AdminUserResponse
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Role { get; set; }
    public bool IsActive { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class UpdateUserRoleRequest
{
    [Required]
    public string Role { get; set; } = string.Empty;
}

public class UpdateUserStatusRequest
{
    [Required]
    public bool IsActive { get; set; }
}

/// <summary>
/// DTO pour la mise à jour générique d'un utilisateur depuis l'admin
/// (tous les champs sont optionnels — seuls ceux fournis sont modifiés)
/// </summary>
public class AdminUpdateUserRequest
{
    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    [EmailAddress]
    [MaxLength(255)]
    public string? Email { get; set; }

    [MaxLength(50)]
    public string? Role { get; set; }

    /// <summary>"active" ou "suspended" — autre valeur ignorée</summary>
    public string? Status { get; set; }
}

/// <summary>
/// Réponse générique pour les actions admin sur un utilisateur
/// </summary>
public class AdminUserActionResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>
/// DTO pour la liste des utilisateurs admin
/// </summary>
public class AdminUserListResponse
{
    public List<AdminUserResponse> Users { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int Limit { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>
/// DTO pour un cours dans la liste admin
/// </summary>
public class AdminSubjectResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Category { get; set; }
    public decimal Price { get; set; }
    public decimal AverageRating { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// DTO pour la liste des cours admin
/// </summary>
public class AdminSubjectListResponse
{
    public List<AdminSubjectResponse> Subjects { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int Limit { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>
/// DTO pour une commande dans la liste admin
/// </summary>
public class AdminOrderResponse
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public int? UserId { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "";
    public DateTime OrderDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public int ItemsCount { get; set; }
}

/// <summary>
/// DTO pour la liste des commandes admin
/// </summary>
public class AdminOrderListResponse
{
    public List<AdminOrderResponse> Orders { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int Limit { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>
/// DTO pour les statistiques système admin
/// </summary>
public class AdminSystemStatsResponse
{
    public int TotalUsers { get; set; }
    public int TotalSubjects { get; set; }
    public int TotalOrders { get; set; }
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// DTO pour le dashboard admin
/// </summary>
public class AdminDashboardResponse
{
    public int TotalUsers { get; set; }
    public int TotalSubjects { get; set; }
    public int TotalOrders { get; set; }
    public string SystemHealthStatus { get; set; } = "Healthy";
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// DTO pour bloquer/débloquer un utilisateur
/// </summary>
public class BlockUserRequest
{
    [Required]
    public int UserId { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }
}

/// <summary>
/// Réponse pour l'action de blocage
/// </summary>
public class BlockUserResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}
