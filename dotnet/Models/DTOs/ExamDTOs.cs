namespace Backend.Models.DTOs;

public class ExamDto
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string ExamType { get; set; } = null!;
    public string Category { get; set; } = null!;
    public int Year { get; set; }
    public string? Session { get; set; }
    public string? Level { get; set; }
    public string? Difficulty { get; set; }
    public int? DurationMinutes { get; set; }
    public string? DocumentUrl { get; set; }
    public string? CorrectionUrl { get; set; }
    public int DownloadCount { get; set; }
    public bool IsPublished { get; set; }
    public int? SubjectId { get; set; }
    public decimal Price { get; set; }
    public bool IsFree => Price == 0;
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateExamRequestDto
{
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string ExamType { get; set; } = null!;
    public string Category { get; set; } = null!;
    public int Year { get; set; }
    public string? Session { get; set; }
    public string? Level { get; set; }
    public string? Difficulty { get; set; }
    public int? DurationMinutes { get; set; }
    public string? DocumentUrl { get; set; }
    public string? CorrectionUrl { get; set; }
    public int? SubjectId { get; set; }
}

public class UpdateExamRequestDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ExamType { get; set; }
    public string? Category { get; set; }
    public int? Year { get; set; }
    public string? Session { get; set; }
    public string? Level { get; set; }
    public string? Difficulty { get; set; }
    public int? DurationMinutes { get; set; }
    public string? DocumentUrl { get; set; }
    public string? CorrectionUrl { get; set; }
    public bool? IsPublished { get; set; }
    public int? SubjectId { get; set; }
}

public class ExamSearchFilterDto
{
    public string? ExamType { get; set; }
    public string? Category { get; set; }
    public int? Year { get; set; }
    public int? MinYear { get; set; }
    public int? MaxYear { get; set; }
    public string? Level { get; set; }
    public string? Difficulty { get; set; }
    public bool? OnlyPublished { get; set; }
    public string? SearchTerm { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; } = "year";
    public string? SortOrder { get; set; } = "desc";
}
