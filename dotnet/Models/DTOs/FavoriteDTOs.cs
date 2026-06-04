using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

/// <summary>
/// DTO for updating a favorite with tags and notes
/// </summary>
public class UpdateFavoriteRequest
{
    [MaxLength(10)]
    public List<string> Tags { get; set; }
    
    [MaxLength(5000)]
    public string Notes { get; set; }
}

/// <summary>
/// DTO for favorite response
/// </summary>
public class FavoriteDto
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    
    public int SubjectId { get; set; }
    
    public string SubjectTitle { get; set; }
    
    public List<string> Tags { get; set; }
    
    public string Notes { get; set; }
    
    public DateTime AddedAt { get; set; }
}
