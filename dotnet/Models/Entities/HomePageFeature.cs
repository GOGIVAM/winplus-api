namespace Backend.Models.Entities;

/// <summary>
/// Entité pour les statistiques et contenu de la page d'accueil
/// </summary>
public class HomePage
{
    public int Id { get; set; }
    public int TotalUsers { get; set; } = 0;
    public int ActiveStudents { get; set; } = 0;
    public int TotalCourses { get; set; } = 0;
    public int TotalExams { get; set; } = 0;
    public decimal TotalRevenue { get; set; } = 0;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Entité pour les fonctionnalités affichées sur la page d'accueil
/// </summary>
public class HomePageFeature
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? ImageUrl { get; set; }
    public int Order { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
