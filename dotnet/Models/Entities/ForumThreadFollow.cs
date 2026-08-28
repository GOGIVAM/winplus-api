using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Fil de discussion suivi par un utilisateur (« Fils suivis » du forum).
/// Une ligne par couple utilisateur / fil.
/// </summary>
public class ForumThreadFollow
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public int ThreadId { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }

    [ForeignKey(nameof(ThreadId))]
    public virtual ForumThread? Thread { get; set; }
}
