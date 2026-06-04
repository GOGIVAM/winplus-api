namespace Backend.Models.Entities;

/// <summary>
/// Entité pour les pages CMS (conditions, politique, pages statiques)
/// </summary>
public class Page
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Slug { get; set; }
    public string? Content { get; set; }
    public string? MetaDescription { get; set; }
    public string? MetaKeywords { get; set; }
    public bool IsPublished { get; set; } = true;
    public int Order { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
