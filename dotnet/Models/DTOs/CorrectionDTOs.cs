namespace Backend.Models.DTOs;

public class CreateAssignmentRequestDto
{
    public int TeacherClassId { get; set; }
    public string Title { get; set; } = null!;
    public string? StatementText { get; set; }
    public decimal MaxScore { get; set; } = 20;
    public DateTime? DueDate { get; set; }
    /// <summary>Génère le barème WinAI immédiatement si un énoncé est fourni (US-COR-05).</summary>
    public bool GenerateRubric { get; set; } = true;
}

public class AssignmentDto
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string? StatementText { get; set; }
    public string? RubricJson { get; set; }
    public decimal MaxScore { get; set; }
    public DateTime? DueDate { get; set; }
    public int TeacherClassId { get; set; }
    public string? TeacherClassName { get; set; }
    public int SubmissionCount { get; set; }
    public int PendingCount { get; set; }
    public bool AlreadySubmitted { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TeacherUploadSubmissionRequestDto
{
    public int StudentId { get; set; } = default!;
    public string? Content { get; set; }
    public string? FileUrl { get; set; }
}

public class StudentSubmitRequestDto
{
    public string? Content { get; set; }
    public string? FileUrl { get; set; }
}

/// <summary>Une copie dans la file de correction (US-COR-01), au format attendu par CorrectionQueue.tsx.</summary>
public class PendingCorrectionDto
{
    public int Id { get; set; }
    public int AssignmentId { get; set; }
    public string Assignment { get; set; } = null!;
    public string StudentName { get; set; } = null!;
    public string? Subject { get; set; }
    public string SubmittedDate { get; set; } = null!;
    public int DaysWaiting { get; set; }
    public string? FileUrl { get; set; }
    public string? TextContent { get; set; }
    public string Priority { get; set; } = "normal";
    public string Status { get; set; } = null!;
    public decimal? Score { get; set; }
    public string? Comment { get; set; }
    public bool IsStaleDraft { get; set; }
}

public class GradeSubmissionRequestDto
{
    public decimal? Note { get; set; }
    public string? Comment { get; set; }
    /// <summary>draft | submitted — miroir du contrat déjà utilisé par CorrectionQueue.tsx.</summary>
    public string Status { get; set; } = "submitted";
}

public class SimilarityPairDto
{
    public int SubmissionAId { get; set; }
    public int SubmissionBId { get; set; }
    public string StudentAName { get; set; } = null!;
    public string StudentBName { get; set; } = null!;
    public int SimilarityPercent { get; set; }
}
