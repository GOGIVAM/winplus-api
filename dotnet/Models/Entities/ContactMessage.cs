namespace Backend.Models.Entities;

/// <summary>
/// Message envoyé par le formulaire de contact du site ou de l'application (25/11/2026). Avant, les deux
/// formulaires du site visaient des routes inexistantes (404) et les messages étaient perdus. Chaque message
/// est gardé ici, puis envoyé par e-mail à l'équipe. Schéma posé par Migrations/SQL_ContactMessages.sql.
/// </summary>
public class ContactMessage
{
    public int Id { get; set; }
    /// <summary>Compte de l'expéditeur s'il était connecté.</summary>
    public int? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    /// <summary>Origine : contact (page Contact), aide (centre d'aide), mobile.</summary>
    public string Source { get; set; } = "contact";
    public string? Locale { get; set; }
    /// <summary>new | handled</summary>
    public string Status { get; set; } = "new";
    public bool EmailSent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? HandledAt { get; set; }
}
