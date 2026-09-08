using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Devoir donné par un professeur à une classe (Module 4 — Corrections).
/// Porte l'énoncé (texte) utilisé pour générer le barème WinAI (US-COR-05),
/// réutilisé ensuite pendant chaque session de correction des copies.
/// </summary>
public class Assignment
{
    public int Id { get; set; }

    public int TeacherId { get; set; }

    public int TeacherClassId { get; set; }

    [Required, MaxLength(200)]
    public required string Title { get; set; }

    /// <summary>Énoncé de l'exercice, collé ou uploadé — sert à générer le barème.</summary>
    public string? StatementText { get; set; }

    /// <summary>Barème structuré généré par WinAI (JSON), modifiable par le professeur.</summary>
    public string? RubricJson { get; set; }

    public decimal MaxScore { get; set; } = 20;

    public DateTime? DueDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TeacherId))]
    public User? Teacher { get; set; }

    [ForeignKey(nameof(TeacherClassId))]
    public TeacherClass? TeacherClass { get; set; }

    public ICollection<Submission> Submissions { get; set; } = new List<Submission>();
}
