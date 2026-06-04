namespace Backend.Models.Entities;

/// <summary>
/// Annonce système
/// </summary>
public class Announcement
{
    public int Id { get; set; }
    
    /// <summary>
    /// Titre de l'annonce
    /// </summary>
    public string Title { get; set; } = string.Empty;
    
    /// <summary>
    /// Contenu de l'annonce
    /// </summary>
    public string? Content { get; set; }
    
    /// <summary>
    /// Priorité (0=low, 1=medium, 2=high, 3=critical)
    /// </summary>
    public int Priority { get; set; } = 0;
    
    /// <summary>
    /// Indicateur de publication
    /// </summary>
    public bool IsPublished { get; set; } = false;
    
    /// <summary>
    /// Date de publication
    /// </summary>
    public DateTime? PublishedAt { get; set; }
    
    /// <summary>
    /// Date d'expiration
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    /// <summary>
    /// Id de l'utilisateur qui a créé l'annonce
    /// </summary>
    public int? CreatedBy { get; set; }
    
    public bool IsDeleted { get; set; } = false;
}
