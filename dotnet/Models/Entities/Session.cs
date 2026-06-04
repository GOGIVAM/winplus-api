namespace Backend.Models.Entities;

/// <summary>
/// Session de classe ou tunisienne
/// </summary>
public class Session
{
    public int Id { get; set; }
    
    /// <summary>
    /// Titre de la session
    /// </summary>
    public string Title { get; set; } = string.Empty;
    
    /// <summary>
    /// Description
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Date de début
    /// </summary>
    public DateTime StartDate { get; set; }
    
    /// <summary>
    /// Date de fin
    /// </summary>
    public DateTime EndDate { get; set; }
    
    /// <summary>
    /// Nombre maximum de participants
    /// </summary>
    public int? MaxParticipants { get; set; }
    
    /// <summary>
    /// État de la session (scheduled, ongoing, completed, cancelled)
    /// </summary>
    public string Status { get; set; } = "scheduled";
    
    /// <summary>
    /// Id du créateur
    /// </summary>
    public int? CreatedBy { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
}
