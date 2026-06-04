using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

public class ForumPost
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [ForeignKey("Thread")]
    public int ThreadId { get; set; }

    [Required]
    [ForeignKey("User")]
    public int UserId { get; set; }

    [Required]
    public string Content { get; set; } = string.Empty;

    public int Upvotes { get; set; } = 0;

    public bool IsAccepted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; } = false;

    // Navigation properties
    public virtual ForumThread? Thread { get; set; }

    public virtual User? User { get; set; }

    public virtual ICollection<ForumVote> Votes { get; set; } = new List<ForumVote>();
}
