namespace Backend.Models.Entities;

/// <summary>
/// OrderItem entity - represents an item within an order
/// </summary>
public class OrderItem
{
    public int Id { get; set; }
    
    public int OrderId { get; set; }
    
    public int SubjectId { get; set; }
    
    public decimal PriceAtPurchase { get; set; }
    
    // Navigation properties
    public required Order Order { get; set; }
    public Subject? Subject { get; set; }
}
