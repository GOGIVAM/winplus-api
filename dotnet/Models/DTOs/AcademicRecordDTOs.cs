namespace Backend.Models.DTOs;

public class AcademicRecordDto
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public string SchoolYear { get; set; } = null!;
    public decimal AverageGrade { get; set; }
    public int RecordedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class UpsertAcademicRecordRequestDto
{
    public string SchoolYear { get; set; } = null!;
    public decimal AverageGrade { get; set; }
}
