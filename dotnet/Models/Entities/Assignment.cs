using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Devoir donné par un professeur à une classe (Module 4  Corrections).
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

    /// <summary>Énoncé de l'exercice, collé ou uploadé  sert à générer le barème.</summary>
    public string? StatementText { get; set; }

    /// <summary>Barème structuré généré par WinAI (JSON), modifiable par le professeur.</summary>
    public string? RubricJson { get; set; }

    /// <summary>
    /// Module 9 : corrigé/barème de référence déposé par le professeur lui-même
    /// (texte saisi), en complément facultatif du barème généré par WinAI.
    /// Sert de base de comparaison supplémentaire à l'analyse de copie.
    /// </summary>
    public string? ReferenceAnswerText { get; set; }

    /// <summary>Module 9 : corrigé de référence déposé sous forme de document.</summary>
    [MaxLength(500)]
    public string? ReferenceAnswerFileUrl { get; set; }

    /// <summary>
    /// Module 11 : quand renseigné, ce devoir EST un quiz ou une épreuve du
    /// catalogue assigné à la classe (voir <see cref="Quiz"/>, et pour une
    /// épreuve QuizService.GetOrCreateExamQuizAsync qui matérialise l'épreuve
    /// en quiz jouable). StatementText/RubricJson restent alors inutilisés :
    /// l'élève répond depuis l'écran de quiz, pas par soumission de texte/fichier.
    /// Choix structurel (vs. une table de liaison séparée) : il réutilise tel
    /// quel le flux de soumission/correction/notification des devoirs au lieu
    /// d'en dupliquer un pour les quiz (voir Module 11 dans le rapport de lot).
    /// </summary>
    public int? QuizId { get; set; }

    public decimal MaxScore { get; set; } = 20;

    public DateTime? DueDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TeacherId))]
    public User? Teacher { get; set; }

    [ForeignKey(nameof(TeacherClassId))]
    public TeacherClass? TeacherClass { get; set; }

    [ForeignKey(nameof(QuizId))]
    public Quiz? Quiz { get; set; }

    public ICollection<Submission> Submissions { get; set; } = new List<Submission>();
}
