namespace Backend.Models;

// ==================== USER MODELS ====================
public class UserProfile
{
    public int UserId { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Niveau { get; set; } = string.Empty; // débutant, intermédiaire, avancé
    public string Objectif { get; set; } = string.Empty;
}

public class UserStats
{
    public int UserId { get; set; }
    public UserProfile Profile { get; set; } = new();
    public Statistics Statistics { get; set; } = new();
}

public class Statistics
{
    public int TotalInteractions { get; set; }
    public int TotalReussites { get; set; }
    public double TauxReussite { get; set; }
    public double AvgTempsPasseSeconds { get; set; }
    public double AvgClics { get; set; }
    public int ContenusDistincts { get; set; }
}

// ==================== CONTENT MODELS ====================
public class Content
{
    public int ContentId { get; set; }
    public string Titre { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public double Difficulte { get; set; }
    public string Description { get; set; } = string.Empty;
}

// ==================== NLP ANALYSIS ====================
public class AnalyzeContentRequest
{
    public int? ContentId { get; set; }
    public string? Text { get; set; }
    public string? Title { get; set; }
    public bool ComputeEmbedding { get; set; } = false;
}

public class NLPAnalysisResult
{
    public double DifficultyScore { get; set; }
    public string DifficultyLevel { get; set; } = string.Empty; // facile, moyen, difficile
    public int EstimatedDurationMinutes { get; set; }
    public List<string> Tags { get; set; } = new();
    public ComplexityMetrics ComplexityMetrics { get; set; } = new();
    public List<double>? Embedding { get; set; }
}

public class ComplexityMetrics
{
    public int WordCount { get; set; }
    public int SentenceCount { get; set; }
    public double AvgWordLength { get; set; }
    public double AvgSentenceLength { get; set; }
}

// ==================== RECOMMENDATIONS ====================
public class RecommendationRequest
{
    public int UserId { get; set; }
    public int Limit { get; set; } = 10;
}

public class PersonalizedRecommendationRequest
{
    public int UserId { get; set; }
    public string? Theme { get; set; }
    public double[]? DifficultyRange { get; set; } // [min, max]
    public int Limit { get; set; } = 10;
}

public class RecommendationResponse
{
    public int UserId { get; set; }
    public List<RecommendedContent> Recommendations { get; set; } = new();
    public int Count { get; set; }
    public RecommendationFilters? Filters { get; set; }
}

public class RecommendedContent
{
    public int ContentId { get; set; }
    public string Titre { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public double Difficulte { get; set; }
    public double Score { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class RecommendationFilters
{
    public string? Theme { get; set; }
    public double[]? DifficultyRange { get; set; }
}

// ==================== API RESPONSES ====================
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class HealthCheckResponse
{
    public string Status { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

// ==================== AUTHENTICATION MODELS ====================
public class AuthenticationResultDto
{
    public string? AccessToken { get; set; }
    public string? IdToken { get; set; }
    public string? RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
    public string? TokenType { get; set; }
}

// ==================== FLASK AI SERVICE RESPONSES ====================
public class ProgressAnalysisResponse
{
    public int UserId { get; set; }
    public int SubjectId { get; set; }
    public double CompletionPercentage { get; set; }
    public double AverageScore { get; set; }
    public List<string> Weaknesses { get; set; } = new();
    public List<string> Strengths { get; set; } = new();
    public string Recommendation { get; set; } = "";
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
}

public class QuizGenerationResponse
{
    public int QuizId { get; set; }
    public int UserId { get; set; }
    public int SubjectId { get; set; }
    public int TotalQuestions { get; set; }
    public string Difficulty { get; set; } = "";
    public List<QuizQuestion> Questions { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class QuizQuestion
{
    public int QuestionId { get; set; }
    public string QuestionText { get; set; } = "";
    public List<string> Options { get; set; } = new();
    public int CorrectAnswerIndex { get; set; }
}

public class PerformanceMetricsResponse
{
    public int UserId { get; set; }
    public string TimePeriod { get; set; } = "";
    public int TotalCoursesEnrolled { get; set; }
    public int CompletedCourses { get; set; }
    public double AverageScore { get; set; }
    public int TotalHoursLearned { get; set; }
    public List<SubjectPerformance> SubjectPerformances { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class SubjectPerformance
{
    public int SubjectId { get; set; }
    public string SubjectName { get; set; } = "";
    public double Score { get; set; }
    public int HoursSpent { get; set; }
}

public class LearningPathResponse
{
    public int PathId { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<PathStep> Steps { get; set; } = new();
    public double EstimatedDurationDays { get; set; }
    public string DifficultyLevel { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PathStep
{
    public int StepId { get; set; }
    public int SubjectId { get; set; }
    public string SubjectTitle { get; set; } = "";
    public int Order { get; set; }
    public string Status { get; set; } = "pending";
}