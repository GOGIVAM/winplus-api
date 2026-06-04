namespace Backend.Models.Entities;

/// <summary>
/// Entité pour les objectifs d'apprentissage des étudiants
/// </summary>
public class Goal
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Type { get; set; } // academic, personal, skill, etc.
    public int? Progress { get; set; } = 0; // 0-100%
    public string Status { get; set; } = "active"; // active, completed, cancelled
    public DateTime TargetDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
