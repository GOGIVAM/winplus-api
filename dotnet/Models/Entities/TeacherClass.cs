using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Classe/groupe géré par un enseignant (ex: Terminale A4 2025-2026)
/// </summary>
public class TeacherClass
{
    public int Id { get; set; }

    public int TeacherId { get; set; }

    [Required, MaxLength(150)]
    public required string Name { get; set; }

    [MaxLength(100)]
    public string? Level { get; set; }          // ex: "Terminale", "Première"

    [MaxLength(20)]
    public string? AcademicYear { get; set; }   // ex: "2025-2026"

    [MaxLength(500)]
    public string? Description { get; set; }

    public int StudentCount { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(TeacherId))]
    public User? Teacher { get; set; }
}
