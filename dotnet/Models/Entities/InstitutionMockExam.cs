using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

/// <summary>
/// Examen blanc d'un établissement : un quiz donné à une ou plusieurs classes,
/// ouvert sur une fenêtre de dates, avec une durée. Schéma posé par
/// Migrations/SQL_InstitutionMockExams.sql.
/// </summary>
public class InstitutionMockExam
{
    public int Id { get; set; }
    public int InstitutionId { get; set; }
    public int CreatedByUserId { get; set; }
    public int QuizId { get; set; }
    public int? ExamId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Classes concernées (StudyGroups), séparées par des virgules.</summary>
    [MaxLength(500)]
    public string GroupIds { get; set; } = string.Empty;

    public DateTime OpensAt { get; set; }
    public DateTime ClosesAt { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public DateTime? ResultsPublishedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Quiz? Quiz { get; set; }

    public List<int> GroupIdList() =>
        GroupIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x, out var v) ? v : 0).Where(v => v > 0).Distinct().ToList();
}
