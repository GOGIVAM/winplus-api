namespace Backend.Models.Entities;

/// <summary>
/// CartItem entity - représente un article du panier, authentifié ou anonyme.
///
/// Un seul chemin de persistance pour les deux cas (voir parent_decisions_session.md
/// / discussion "Cart is empty") : avant cette version, le panier anonyme vivait dans
/// un dictionnaire en mémoire (AnonymousCartService, supprimé), qui ne survivait pas
/// à un redémarrage du service — un panier ajouté avant connexion pouvait disparaître
/// sans trace avant même la fusion au login. UserId et DeviceId sont mutuellement
/// exclusifs : un item anonyme a UserId=null et DeviceId renseigné ; à la connexion,
/// il est réassigné (UserId posé, DeviceId remis à null) plutôt que recréé.
/// </summary>
public class CartItem
{
    public int Id { get; set; }

    /// <summary>Null tant que l'item est anonyme (voir DeviceId).</summary>
    public int? UserId { get; set; }

    /// <summary>Identifiant client anonyme (avant connexion) — null une fois l'item rattaché à un UserId.</summary>
    public string? DeviceId { get; set; }

    public int SubjectId { get; set; }

    public decimal Price { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User? User { get; set; }

    public required Subject Subject { get; set; }
}
