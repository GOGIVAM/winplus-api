using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

/// <summary>
/// Devoir donné par un établissement à une de ses classes : un quiz du
/// catalogue ou une épreuve transformée en quiz. Schéma posé par
/// Migrations/SQL_InstitutionHomework.sql.
/// </summary>
public class InstitutionHomework
{
    public int Id { get; set; }

    public int InstitutionId { get; set; }

    public int StudyGroupId { get; set; }

    public int CreatedByUserId { get; set; }

    /// <summary>Quiz à faire (pour une épreuve : son quiz généré).</summary>
    public int QuizId { get; set; }

    /// <summary>Épreuve d'origine, quand le devoir a été donné à partir d'une épreuve.</summary>
    public int? ExamId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string? Instructions { get; set; }

    public DateTime? DueDate { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public StudyGroup? StudyGroup { get; set; }
    public Quiz? Quiz { get; set; }
}
