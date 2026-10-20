using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

/// <summary>
/// Entrée du cahier de textes d'une classe d'établissement : ce qui a été fait
/// en cours et le travail à faire. Schéma posé par Migrations/SQL_ClassLogbook.sql.
/// </summary>
public class ClassLogbookEntry
{
    public int Id { get; set; }
    public int StudyGroupId { get; set; }
    public int AuthorUserId { get; set; }

    [MaxLength(100)]
    public string Subject { get; set; } = string.Empty;

    public DateTime LessonDate { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? HomeworkText { get; set; }
    public DateTime? HomeworkDue { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public StudyGroup? StudyGroup { get; set; }
    public User? Author { get; set; }
}
