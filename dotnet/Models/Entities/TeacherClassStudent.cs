using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Élève inscrit dans une classe d'enseignant (S4-4).
/// Le compteur TeacherClass.StudentCount est recalculé à chaque écriture.
/// </summary>
public class TeacherClassStudent
{
    public int Id { get; set; }

    public int TeacherClassId { get; set; }

    public int StudentId { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TeacherClassId))]
    public TeacherClass? TeacherClass { get; set; }

    [ForeignKey(nameof(StudentId))]
    public User? Student { get; set; }
}
