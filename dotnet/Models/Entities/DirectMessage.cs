using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Message direct entre deux utilisateurs (ex: parent ↔ enfant, parent ↔ enseignant)
/// </summary>
public class DirectMessage
{
    public int Id { get; set; }

    public int FromUserId { get; set; }

    public int ToUserId { get; set; }

    /// <summary>Optionnel si le message est une pièce jointe seule (Type != "text").</summary>
    [MaxLength(2000)]
    public string? Content { get; set; }

    /// <summary>text | image | pdf | voice.</summary>
    [MaxLength(20)]
    public string Type { get; set; } = "text";

    [MaxLength(500)]
    public string? FileUrl { get; set; }

    [MaxLength(255)]
    public string? FileName { get; set; }

    public int? ReplyToMessageId { get; set; }

    /// <summary>Non nul = message programmé, invisible du destinataire tant que l'heure n'est pas atteinte.</summary>
    public DateTime? ScheduledSendAt { get; set; }

    /// <summary>false uniquement pour un message programmé pas encore "délivré" — voir ScheduledMessageDeliveryService.</summary>
    public bool ScheduledNotificationSent { get; set; } = true;

    public bool IsDeleted { get; set; } = false;

    public bool IsRead { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ReadAt { get; set; }

    [ForeignKey(nameof(FromUserId))]
    public User? From { get; set; }

    [ForeignKey(nameof(ToUserId))]
    public User? To { get; set; }

    [ForeignKey(nameof(ReplyToMessageId))]
    public DirectMessage? ReplyToMessage { get; set; }

    public ICollection<DirectMessageReaction> Reactions { get; set; } = new List<DirectMessageReaction>();
}

/// <summary>Réaction emoji d'un utilisateur sur un message direct — une par utilisateur et par message (toggle).</summary>
public class DirectMessageReaction
{
    public int Id { get; set; }
    public int DirectMessageId { get; set; }
    public int UserId { get; set; }

    [MaxLength(8)]
    public string Emoji { get; set; } = "👍";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(DirectMessageId))]
    public DirectMessage? DirectMessage { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
