using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Favorite entity - represents a user's favorited course
/// </summary>
public class Favorite
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    
    public int SubjectId { get; set; }
    
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    
    // Tags & Notes for organization
    [MaxLength(500)]
    public string Tags { get; set; } // JSON: ["urgent", "examen", "revoir"]
    
    public string Notes { get; set; } // Texte libre
    
    // Collection association
    public int? CollectionId { get; set; }
    
    // Navigation properties
    public required User User { get; set; }
    
    public required Subject Subject { get; set; }
    
    [ForeignKey(nameof(CollectionId))]
    public FavoriteCollection Collection { get; set; }
}
