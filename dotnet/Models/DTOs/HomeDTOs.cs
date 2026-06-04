namespace Backend.Models.DTOs;

/// <summary>
/// DTO pour les statistiques globales de la HomePage
/// </summary>
public class HomeStatsDto
{
    public int TotalUsers { get; set; }
    public int TotalExams { get; set; }
    public int TotalRevisions { get; set; }
    public double SuccessRate { get; set; }
    public int StudentCount { get; set; }
    public int TeacherCount { get; set; }
    public int ParentCount { get; set; }
    public int CompletedEnrollments { get; set; }
    public int TotalEnrollments { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// DTO pour les fonctionnalités affichées sur la HomePage
/// </summary>
public class HomeFeatureDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? ImageUrl { get; set; }
    public int Order { get; set; }
}

/// <summary>
/// DTO pour les informations de contact
/// </summary>
public class ContactInfoDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Type { get; set; }
}

/// <summary>
/// DTO pour le contenu des pages statiques
/// </summary>
public class PageContentDto
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? MetaDescription { get; set; }
    public string? MetaKeywords { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// DTO pour le contenu du footer
/// </summary>
public class FooterDto
{
    public string? CompanyDescription { get; set; }
    public string? CompanyName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Copyright { get; set; }
    public Dictionary<string, string>? Links { get; set; }
    public Dictionary<string, string>? SocialMedia { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
