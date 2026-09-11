namespace Backend.Models.Entities;

/// <summary>
/// Order entity - represents a purchase transaction
/// </summary>
public class Order
{
    public int Id { get; set; }
    
    public int? UserId { get; set; }

    public string? GuestEmail { get; set; }

    public string? GuestName { get; set; }

    public required string OrderNumber { get; set; }
    
    public decimal TotalAmount { get; set; }
    
    public string Status { get; set; } = "Pending"; // Pending, Completed, Failed, Refunded
    
    public string? PaymentMethod { get; set; }
    
    public string? TransactionId { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    
    public DateTime? CompletedDate { get; set; }

    /// <summary>
    /// Derniere modification. La colonne "updated_at" existe bien dans la table
    /// orders (schema : TIMESTAMP DEFAULT CURRENT_TIMESTAMP) mais n'etait pas
    /// mappee ici, d'ou l'erreur de compilation CS1061 sur
    /// OrdersController.RequestRefund.
    ///
    /// Nullable a dessein : les fichiers de schema du depot divergent sur cette
    /// colonne, et une ligne heritee a NULL ferait echouer la lecture d'un
    /// DateTime non nullable a l'execution.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
    
    public string? Notes { get; set; }

    public decimal DiscountAmount { get; set; } = 0;

    public bool IsDeleted { get; set; } = false;

    /// <summary>
    /// Code d'affiliation capturé côté frontend (?ref=CODE, localStorage) au
    /// moment de la commande — la fenêtre d'attribution ne peut plus être
    /// vérifiée une fois le paiement confirmé (webhook async, sans contexte
    /// requête d'origine), donc le code doit être figé ici dès la création.
    /// Voir AffiliateService.RecordCommissionForOrderAsync.
    /// </summary>
    public string? ReferralCode { get; set; }

    // Navigation properties
    public User? User { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();

    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
