using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Question ratée par un élève, enregistrée à la fin d'une tentative de quiz.
/// Alimente le quiz de révision (S7-4). Aucune donnée fictive : chaque ligne
/// provient d'une tentative réelle.
/// </summary>
public class QuizMistake
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int? QuizId { get; set; }

    public int? QuizAttemptId { get; set; }

    [MaxLength(100)]
    public string? Subject { get; set; }

    [Required, MaxLength(1000)]
    public required string Question { get; set; }

    [MaxLength(500)]
    public string? GivenAnswer { get; set; }

    [MaxLength(500)]
    public string? CorrectAnswer { get; set; }

    /// <summary>Passe à true quand l'élève répond juste en révision.</summary>
    public bool IsResolved { get; set; } = false;

    public DateTime? ResolvedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(QuizId))]
    public Quiz? Quiz { get; set; }
}
