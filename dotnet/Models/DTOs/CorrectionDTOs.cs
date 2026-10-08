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
    /// <summary>Module 9  corrigé de référence du professeur, saisi ou collé.</summary>
    public string? ReferenceAnswerText { get; set; }
    /// <summary>Module 9  corrigé de référence du professeur, déposé en fichier.</summary>
    public string? ReferenceAnswerFileUrl { get; set; }
    /// <summary>
    /// Module 11  quand renseigné, ce devoir assigne ce quiz/épreuve (déjà
    /// existant au catalogue, ou obtenu via POST /quizzes/exam/{examId} pour
    /// une épreuve) à la classe, au lieu d'un énoncé libre.
    /// </summary>
    public int? QuizId { get; set; }
}

public class AssignmentDto
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string? StatementText { get; set; }
    public string? RubricJson { get; set; }
    public string? ReferenceAnswerText { get; set; }
    public string? ReferenceAnswerFileUrl { get; set; }
    public int? QuizId { get; set; }
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
    /// <summary>Module 9  note maximale réelle du devoir (20 par défaut, 100 pour un quiz assigné) : l'écran ne doit plus supposer /20.</summary>
    public decimal MaxScore { get; set; } = 20;
    /// <summary>Module 9  corrigé de référence du professeur, pour enrichir l'analyse WinAI côté écran.</summary>
    public string? ReferenceAnswerText { get; set; }
    public string? ReferenceAnswerFileUrl { get; set; }
    /// <summary>Module 11  vrai si la copie provient d'une réponse à un quiz/épreuve assigné (QuizAttempt), pas d'un dépôt libre.</summary>
    public bool IsQuizSubmission { get; set; }
}

public class GradeSubmissionRequestDto
{
    public decimal? Note { get; set; }
    public string? Comment { get; set; }
    /// <summary>draft | submitted  miroir du contrat déjà utilisé par CorrectionQueue.tsx.</summary>
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
