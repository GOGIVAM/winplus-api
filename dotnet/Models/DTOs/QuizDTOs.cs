namespace Backend.Models.DTOs;

/// <summary>
/// DTO pour Quiz - Utilisé pour les requêtes/réponses API
/// </summary>
public class QuizDto
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string Subject { get; set; } = null!;
    public string Difficulty { get; set; } = null!;
    public int? SubjectId { get; set; }
    public int? ExamId { get; set; }
    public int QuestionsCount { get; set; }
    public int? DurationMinutes { get; set; }
    public bool IsPublished { get; set; }
    public int TotalAttempts { get; set; }
    public int PassingAttempts { get; set; }
    public decimal? AverageScore { get; set; }
    public string? Tags { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}

public class QuizQuestionDto
{
    public string Id { get; set; } = null!;
    public string Question { get; set; } = null!;
    public List<string> Options { get; set; } = new();
    public string CorrectAnswer { get; set; } = null!;
    public string? Explanation { get; set; }
    public int? Difficulty { get; set; }
    public string? Topic { get; set; }
}

public class CreateQuizRequestDto
{
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string Subject { get; set; } = null!;
    public int Difficulty { get; set; }
    public List<QuizQuestionDto> Questions { get; set; } = new();
    public int? DurationMinutes { get; set; }
    public int? PassingScore { get; set; }
    public int? AllowedAttempts { get; set; }
    public string? QuizType { get; set; } = "Fixed";
    public int? SubjectId { get; set; }
    public int? ExamId { get; set; }
    public bool IsAIGenerated { get; set; }
}

public class UpdateQuizRequestDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Subject { get; set; }
    public int? Difficulty { get; set; }
    public List<QuizQuestionDto>? Questions { get; set; }
    public int? DurationMinutes { get; set; }
    public int? PassingScore { get; set; }
    public int? AllowedAttempts { get; set; }
}

public class SubmitQuizAttemptRequestDto
{
    public List<QuizAnswerDto> Answers { get; set; } = new();
    public int TimeSpentSeconds { get; set; }
}

public class QuizAnswerDto
{
    public string QuestionId { get; set; } = null!;
    public string Answer { get; set; } = null!;
}

public class QuizAttemptDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int QuizId { get; set; }
    public decimal Score { get; set; }
    public int TimeSpentSeconds { get; set; }
    public bool Passed { get; set; }
    public DateTime CompletedAt { get; set; }
}

public class QuizResultResponseDto
{
    public int AttemptId { get; set; }
    public double Score { get; set; }
    public bool Passed { get; set; }
    public int TotalQuestions { get; set; }
    public int CorrectAnswers { get; set; }
    public int TimeSpentSeconds { get; set; }
    public DateTime CompletedAt { get; set; }
    public List<QuizQuestionResultDto> QuestionResults { get; set; } = new();
}

public class QuizQuestionResultDto
{
    public string QuestionId { get; set; } = null!;
    public string UserAnswer { get; set; } = null!;
    public string CorrectAnswer { get; set; } = null!;
    public bool IsCorrect { get; set; }
    public string? Explanation { get; set; }
    public int Points { get; set; }
}

public class QuizSearchFilterDto
{
    public string? Subject { get; set; }
    public int? Difficulty { get; set; }
    public int? MinDuration { get; set; }
    public int? MaxDuration { get; set; }
    public bool? OnlyPublished { get; set; }
    public string? SearchTerm { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; } = "createdAt";
    public string? SortOrder { get; set; } = "desc";
}
