namespace Backend.Models.DTOs;

/// <summary>
/// DTO pour Revision - Utilisé pour les requêtes/réponses API
/// </summary>
public class RevisionDto
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string Subject { get; set; } = null!;
    public string Topic { get; set; } = null!;
    public string RevisionType { get; set; } = "Theory";
    public string? Content { get; set; }
    public string? VideoUrl { get; set; }
    public string? DocumentUrl { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsPublished { get; set; }
    public int EnrolledCount { get; set; }
    public string? Tags { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}

public class CreateRevisionRequestDto
{
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string Subject { get; set; } = null!;
    public string Topic { get; set; } = null!;
    public string RevisionType { get; set; } = "Theory";
    public string? Content { get; set; }
    public string? VideoUrl { get; set; }
    public string? DocumentUrl { get; set; }
    public int DurationMinutes { get; set; }
}

public class UpdateRevisionRequestDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Subject { get; set; }
    public string? Topic { get; set; }
    public string? RevisionType { get; set; }
    public string? Content { get; set; }
    public string? VideoUrl { get; set; }
    public string? DocumentUrl { get; set; }
    public int? DurationMinutes { get; set; }
}

public class RevisionEnrollmentDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int RevisionId { get; set; }
    public decimal? OriginalScore { get; set; }
    public string Status { get; set; } = "Assigned";
    public double Progress { get; set; }
    public decimal? FinalScore { get; set; }
    public decimal? ScoreImprovement { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class StartRevisionRequestDto
{
    public double? OriginalScore { get; set; }
}

public class CompleteRevisionRequestDto
{
    public double FinalScore { get; set; }
}

public class RevisionProgressResponseDto
{
    public int RevisionId { get; set; }
    public int UserId { get; set; }
    public string Status { get; set; } = null!;
    public double ProgressPercentage { get; set; }
    public double? OriginalScore { get; set; }
    public double? FinalScore { get; set; }
    public double? ScoreImprovement { get; set; }
    public DateTime? EstimatedCompletionTime { get; set; }
    public DateTime? StartedAt { get; set; }
}

public class AssignRevisionRequestDto
{
    public int UserId { get; set; }
    public decimal? OriginalScore { get; set; }
    public int? TriggeredByLearningHistoryId { get; set; }
}

public class RevisionSearchFilterDto
{
    public string? Subject { get; set; }
    public string? Topic { get; set; }
    public string? RevisionType { get; set; }
    public bool? OnlyPublished { get; set; }
    public string? SearchTerm { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; } = "createdAt";
    public string? SortOrder { get; set; } = "desc";
}
