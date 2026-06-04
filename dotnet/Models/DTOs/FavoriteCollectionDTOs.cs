using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

/// <summary>
/// DTO for creating a favorite collection
/// </summary>
public class CreateFavoriteCollectionRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; }
    
    [MaxLength(500)]
    public string Description { get; set; }
    
    [MaxLength(20)]
    public string Color { get; set; } // Hex color
    
    [MaxLength(50)]
    public string Icon { get; set; } // Icon name
}

/// <summary>
/// DTO for updating a favorite collection
/// </summary>
public class UpdateFavoriteCollectionRequest
{
    [MaxLength(100)]
    public string Name { get; set; }
    
    [MaxLength(500)]
    public string Description { get; set; }
    
    [MaxLength(20)]
    public string Color { get; set; }
    
    [MaxLength(50)]
    public string Icon { get; set; }
}

/// <summary>
/// DTO for adding favorite to collection
/// </summary>
public class AddFavoriteToCollectionRequest
{
    [Required]
    public int FavoriteId { get; set; }
    
    [Required]
    public int CollectionId { get; set; }
}

/// <summary>
/// DTO for favorite collection response
/// </summary>
public class FavoriteCollectionDto
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    
    public string Name { get; set; }
    
    public string Description { get; set; }
    
    public string Color { get; set; }
    
    public string Icon { get; set; }
    
    public int Order { get; set; }
    
    public List<FavoriteDto> Favorites { get; set; } = new();
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime? UpdatedAt { get; set; }
}
