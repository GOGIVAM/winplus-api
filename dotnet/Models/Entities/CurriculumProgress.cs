using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Classe et série suivies par un élève dans « Mon programme » (programme
/// officiel du secondaire). Une ligne par élève.
/// </summary>
public class CurriculumSelection
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    /// <summary>6e, 5e, 4e, 3e, 2nde, 1ere ou tle.</summary>
    [Required, MaxLength(10)]
    public string Level { get; set; } = string.Empty;

    /// <summary>Série au second cycle (A, C, D, E, TI), sinon null.</summary>
    [MaxLength(5)]
    public string? Series { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}

/// <summary>
/// État d'un chapitre du programme pour un élève. Une ligne par élève et par
/// chapitre commencé ; un chapitre « à voir » n'a pas de ligne.
/// La clé du chapitre est celle du référentiel côté site :
/// « classe|matière|module|chapitre » (ex. « tle|maths|2|3 »).
/// </summary>
public class CurriculumProgress
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required, MaxLength(120)]
    public string ChapterKey { get; set; } = string.Empty;

    /// <summary>« doing » (en cours) ou « done » (vu).</summary>
    [Required, MaxLength(10)]
    public string Status { get; set; } = "doing";

    [Column(TypeName = "timestamp with time zone")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}
