using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Note de révision personnelle attachée à une épreuve/matière (S7-1).
/// Remplace le stockage local : la note suit l'élève sur tous ses appareils.
/// </summary>
public class RevisionNote
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int SubjectId { get; set; }

    [Required, MaxLength(300)]
    public required string Content { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(SubjectId))]
    public Subject? Subject { get; set; }
}

/// <summary>
/// Tag de révision (À réviser / Difficile / Maîtrisé / À acheter) posé par
/// un élève sur une épreuve. Un tag au maximum par couple (élève, épreuve, libellé).
/// </summary>
public class RevisionTag
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int SubjectId { get; set; }

    [Required, MaxLength(40)]
    public required string Label { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(SubjectId))]
    public Subject? Subject { get; set; }
}
