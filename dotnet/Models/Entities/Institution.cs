namespace Backend.Models.Entities;

/// <summary>
/// Institution éducative (université, école, etc.)
/// </summary>
public class Institution
{
    public int Id { get; set; }
    
    /// <summary>
    /// Nom de l'institution
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Code de l'institution
    /// </summary>
    public string? Code { get; set; }
    
    /// <summary>
    /// Pays de l'institution
    /// </summary>
    public string Country { get; set; } = string.Empty;
    
    /// <summary>
    /// Ville
    /// </summary>
    public string? City { get; set; }
    
    /// <summary>
    /// Adresse
    /// </summary>
    public string? Address { get; set; }
    
    /// <summary>
    /// Email de contact
    /// </summary>
    public string? Email { get; set; }
    
    /// <summary>
    /// Téléphone de contact
    /// </summary>
    public string? Phone { get; set; }
    
    /// <summary>
    /// Région
    /// </summary>
    public string? Region { get; set; }
    
    /// <summary>
    /// Type d'institution (University, School, College, etc.)
    /// </summary>
    public string? Type { get; set; }
    
    /// <summary>
    /// Indicateur d'activité
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
}
