using System.Text.Json.Serialization;

namespace Backend.Models.Entities;

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public int SubjectId { get; set; }

    public decimal PriceAtPurchase { get; set; }

    [JsonIgnore]
    public Order Order { get; set; } = null!;
    public Subject? Subject { get; set; }
}
