using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Liaison parent-enfant — un parent peut avoir plusieurs enfants (étudiants)
/// </summary>
public class ParentStudentLink
{
    public int Id { get; set; }

    public int ParentId { get; set; }

    public int StudentId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ParentId))]
    public User? Parent { get; set; }

    [ForeignKey(nameof(StudentId))]
    public User? Student { get; set; }
}
