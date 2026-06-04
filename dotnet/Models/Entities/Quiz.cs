using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Quiz Entity - Représente un questionnaire/test interactif
/// </summary>
[Table("Quizzes")]
public class Quiz
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Titre du quiz (ex: "Quiz Trigonométrie - Niveau 2")
    /// </summary>
    [Required]
    [StringLength(255)]
    public string Title { get; set; } = null!;

    /// <summary>
    /// Description du quiz
    /// </summary>
    [StringLength(2000)]
    public string? Description { get; set; }

    /// <summary>
    /// Matière du quiz (Mathématiques, Physique, etc.)
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Subject { get; set; } = null!;

    /// <summary>
    /// Niveau de difficulté (easy, medium, hard)
    /// </summary>
    [StringLength(50)]
    public string Difficulty { get; set; } = "medium";

    /// <summary>
    /// Nombre de questions
    /// </summary>
    public int QuestionCount { get; set; } = 10;

    /// <summary>
    /// Durée limite en minutes
    /// </summary>
    public int? TimeLimit { get; set; }

    /// <summary>
    /// Score minimum pour réussir (ex: 60 pour 60%)
    /// </summary>
    public int PassingScore { get; set; } = 60;

    /// <summary>
    /// Questions du quiz (JSON - stocké comme jsonb)
    /// Format: [
    ///   {
    ///     "id": "q1",
    ///     "question": "...",
    ///     "options": ["A", "B", "C", "D"],
    ///     "correctAnswer": "B",
    ///     "explanation": "..."
    ///   }
    /// ]
    /// </summary>
    [Required]
    public string QuestionsJson { get; set; } = "[]";

    /// <summary>
    /// ID du Subject associé (optionnel)
    /// </summary>
    public int? SubjectId { get; set; }

    /// <summary>
    /// ID de l'Exam associé (optionnel)
    /// </summary>
    public int? ExamId { get; set; }

    /// <summary>
    /// Est-ce un quiz généré par IA?
    /// </summary>
    public bool IsAIGenerated { get; set; } = false;

    /// <summary>
    /// Est publié?
    /// </summary>
    public bool IsPublished { get; set; } = true;

    /// <summary>
    /// Nombre de fois pris
    /// </summary>
    public int Attempts { get; set; } = 0;

    /// <summary>
    /// Nombre de fois réussi
    /// </summary>
    public int PassingAttempts { get; set; } = 0;

    /// <summary>
    /// Score moyen (optionnel)
    /// </summary>
    [Column(TypeName = "numeric(5,2)")]
    public decimal? AverageScore { get; set; }

    /// <summary>
    /// Total des scores (utilisé pour calculer la moyenne)
    /// </summary>
    [Column(TypeName = "numeric(10,2)")]
    public decimal TotalScore { get; set; } = 0;

    /// <summary>
    /// Tags/Keywords
    /// </summary>
    [StringLength(1000)]
    public string? Tags { get; set; } // JSON: ["Trigonométrie", "Calcul"]

    /// <summary>
    /// Est supprimé (soft delete)
    /// </summary>
    public bool IsDeleted { get; set; } = false;

    // Timestamps
    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? PublishedAt { get; set; }

    // Relations
    [ForeignKey(nameof(SubjectId))]
    public virtual Subject? Subject_Reference { get; set; }

    [ForeignKey(nameof(ExamId))]
    public virtual Exam? Exam_Reference { get; set; }

    public virtual ICollection<QuizAttempt> Attempts_Collection { get; set; } = new List<QuizAttempt>();
}

/// <summary>
/// QuizAttempt Entity - Suivi des tentatives de quiz par utilisateur
/// </summary>
[Table("QuizAttempts")]
public class QuizAttempt
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Utilisateur qui a tenté le quiz
    /// </summary>
    [Required]
    public int UserId { get; set; }

    /// <summary>
    /// Quiz tenté
    /// </summary>
    [Required]
    public int QuizId { get; set; }

    /// <summary>
    /// Réponses de l'utilisateur (JSON)
    /// </summary>
    [Required]
    public string UserAnswersJson { get; set; } = "{}";

    /// <summary>
    /// Score obtenu (0-100)
    /// </summary>
    [Column(TypeName = "numeric(5,2)")]
    public decimal Score { get; set; }

    /// <summary>
    /// Nombre de bonnes réponses
    /// </summary>
    public int CorrectAnswers { get; set; }

    /// <summary>
    /// Temps passé (en secondes)
    /// </summary>
    public int? TimeSpentSeconds { get; set; }

    /// <summary>
    /// Statut (Submitted, Graded, Reviewed)
    /// </summary>
    [StringLength(50)]
    public string Status { get; set; } = "Submitted";

    /// <summary>
    /// Le quiz est complété?
    /// </summary>
    public bool IsCompleted { get; set; } = true;

    /// <summary>
    /// Quiz réussi (score >= PassingScore)
    /// </summary>
    public bool Passed { get; set; } = false;

    /// <summary>
    /// Numéro de la tentative
    /// </summary>
    public int AttemptNumber { get; set; } = 1;

    // Timestamps
    [Required]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

    // Relations
    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }

    [ForeignKey(nameof(QuizId))]
    public virtual Quiz? Quiz { get; set; }
}
