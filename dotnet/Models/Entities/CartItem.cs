namespace Backend.Models.Entities;

/// <summary>
/// CartItem entity - represents an item in the user's shopping cart
/// </summary>
public class CartItem
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    
    public int SubjectId { get; set; }
    
    public decimal Price { get; set; }
    
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public required User User { get; set; }
    
    public required Subject Subject { get; set; }
}
