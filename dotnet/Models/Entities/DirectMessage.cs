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

    [Required, MaxLength(2000)]
    public required string Content { get; set; }

    public bool IsRead { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ReadAt { get; set; }

    [ForeignKey(nameof(FromUserId))]
    public User? From { get; set; }

    [ForeignKey(nameof(ToUserId))]
    public User? To { get; set; }
}
