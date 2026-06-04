namespace Backend.Models.Entities;

/// <summary>
/// Événement (classe, examen, deadline, etc.)
/// </summary>
public class Event
{
    public int Id { get; set; }
    
    /// <summary>
    /// Titre de l'événement
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
    /// Date de fin (optionnel)
    /// </summary>
    public DateTime? EndDate { get; set; }
    
    /// <summary>
    /// Lieu
    /// </summary>
    public string? Location { get; set; }
    
    /// <summary>
    /// Type d'événement (class, exam, meeting, deadline)
    /// </summary>
    public string EventType { get; set; } = "class";
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
}
