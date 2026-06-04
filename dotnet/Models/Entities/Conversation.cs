using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Représente une session de conversation chatbot
/// </summary>
public class Conversation
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = "Nouvelle conversation";

    /// <summary>
    /// Tags pour catégoriser la conversation (JSON array)
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? Tags { get; set; }

    /// <summary>
    /// Métadonnées additionnelles (JSON object)
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? Metadata { get; set; }

    /// <summary>
    /// Indique si la conversation est active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Horodatage du dernier message
    /// </summary>
    public DateTime? LastMessageAt { get; set; }

    /// <summary>
    /// Nombre total de messages dans la conversation
    /// </summary>
    public int MessageCount { get; set; } = 0;

    /// <summary>
    /// Date de création
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Date de dernière mise à jour
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Soft delete
    /// </summary>
    public bool IsDeleted { get; set; } = false;

    // Navigation properties
    [ForeignKey("UserId")]
    public virtual User User { get; set; } = null!;

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
