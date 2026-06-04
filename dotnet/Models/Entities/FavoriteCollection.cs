using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// FavoriteCollection entity - groups favorites into collections/folders
/// </summary>
public class FavoriteCollection
{
    public int Id { get; set; }
    
    [Required]
    public int UserId { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string Name { get; set; }
    
    [MaxLength(500)]
    public string Description { get; set; }
    
    [MaxLength(20)]
    public string Color { get; set; } // Hex color: #FF5733
    
    [MaxLength(50)]
    public string Icon { get; set; } // Icon name: folder, star, book
    
    public int Order { get; set; } = 0;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation properties
    [ForeignKey(nameof(UserId))]
    public required User User { get; set; }
    
    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();
}
