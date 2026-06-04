using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

[Table("Exams")]
public class Exam
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(255)]
    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    [Required]
    [StringLength(100)]
    public string ExamType { get; set; } = null!;

    [Column("Category")]
    [Required]
    [StringLength(100)]
    public string Category { get; set; } = null!;

    [Required]
    public int Year { get; set; }

    [StringLength(50)]
    public string? Session { get; set; }

    [StringLength(100)]
    public string? Level { get; set; }

    [Column("Duration")]
    public int? DurationMinutes { get; set; }

    [Column("DocumentUrl")]
    [StringLength(500)]
    public string? DocumentUrl { get; set; }

    [StringLength(500)]
    public string? CorrectionUrl { get; set; }

    [StringLength(50)]
    public string? Difficulty { get; set; }

    [Column("DownloadCount")]
    public int DownloadCount { get; set; } = 0;

    public bool IsPublished { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
    public int? SubjectId { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Relations
    [ForeignKey(nameof(SubjectId))]
    public virtual Subject? SubjectReference { get; set; }

    // Navigation properties
    public virtual ICollection<Quiz>? Quizzes { get; set; } = new List<Quiz>();
    public virtual ICollection<Revision>? Revisions { get; set; } = new List<Revision>();
}
