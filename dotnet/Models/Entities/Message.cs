using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Représente un message dans une conversation chatbot
/// </summary>
public class Message
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int ConversationId { get; set; }

    /// <summary>
    /// Rôle de l'émetteur: "user" ou "assistant"
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Role { get; set; } = "user";

    /// <summary>
    /// Contenu du message (texte, markdown, LaTeX)
    /// </summary>
    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Pièces jointes (images, fichiers) - JSON array
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? Attachments { get; set; }

    /// <summary>
    /// Tokens utilisés pour ce message (input + output)
    /// </summary>
    public int? TokensUsed { get; set; }

    /// <summary>
    /// Note de feedback utilisateur (-1, 0, 1)
    /// </summary>
    public int? FeedbackRating { get; set; }

    /// <summary>
    /// Commentaire de feedback
    /// </summary>
    [MaxLength(1000)]
    public string? FeedbackComment { get; set; }

    /// <summary>
    /// Temps de génération de la réponse en millisecondes
    /// </summary>
    public int? GenerationTimeMs { get; set; }

    /// <summary>
    /// Date de création
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Soft delete
    /// </summary>
    public bool IsDeleted { get; set; } = false;

    // Navigation properties
    [ForeignKey("ConversationId")]
    public virtual Conversation Conversation { get; set; } = null!;
}
