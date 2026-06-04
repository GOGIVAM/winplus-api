namespace Backend.Models.Entities;

/// <summary>
/// Entité pour les niveaux d'apprentissage (difficultés)
/// </summary>
public class Level
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int Order { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
