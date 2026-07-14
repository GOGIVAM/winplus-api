using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs
{
    // Request/Response DTOs for AI features
    
    #region Recommendations
    
    /// <summary>
    /// Request to get course recommendations for a user
    /// </summary>
    public class RecommendationRequest
    {
        [Required(ErrorMessage = "User ID is required")]
        public int UserId { get; set; }

        [Range(1, 20, ErrorMessage = "Number of recommendations must be between 1 and 20")]
        public int NumberOfRecommendations { get; set; } = 5;

        [MaxLength(50)]
        public string PreferenceLevel { get; set; } = "all"; // beginner, intermediate, advanced, all

        [MaxLength(100)]
        public string SubjectCategory { get; set; } = string.Empty; // optional category filter
    }

    /// <summary>
    /// Single recommendation response
    /// </summary>
    public class RecommendationItem
    {
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public decimal MatchScore { get; set; } // 0-1 score
        public string Reason { get; set; }
        public int EstimatedDurationHours { get; set; }
    }

    /// <summary>
    /// Response containing recommendations for user
    /// </summary>
    public class RecommendationResponse
    {
        public int UserId { get; set; }
        public List<RecommendationItem> Recommendations { get; set; } = new();
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    #endregion

    #region Progress Analysis

    /// <summary>
    /// Request to analyze student progress
    /// </summary>
    public class ProgressAnalysisRequest
    {
        [Required(ErrorMessage = "User ID is required")]
        public int UserId { get; set; }

        [Required(ErrorMessage = "Subject ID is required")]
        public int SubjectId { get; set; }

        [MaxLength(50)]
        public string AnalysisDepth { get; set; } = "standard"; // quick, standard, detailed
    }

    /// <summary>
    /// Response containing progress analysis
    /// </summary>
    public class ProgressAnalysisResponse
    {
        public int UserId { get; set; }
        public int SubjectId { get; set; }
        public int CompletionPercentage { get; set; } // 0-100
        public string ProgressTrend { get; set; } // improving, stable, declining
        public DateTime EstimatedCompletionDate { get; set; }
        public List<string> WeakAreas { get; set; } = new();
        public List<string> Strengths { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    #endregion

    #region Quiz Generation

    /// <summary>
    /// Request to generate quiz questions
    /// </summary>
    public class QuizGenerationRequest
    {
        [Required(ErrorMessage = "User ID is required")]
        public int UserId { get; set; }

        [Required(ErrorMessage = "Subject ID is required")]
        public int SubjectId { get; set; }

        [Range(1, 50, ErrorMessage = "Number of questions must be between 1 and 50")]
        public int NumberOfQuestions { get; set; } = 10;

        [MaxLength(50)]
        public string Difficulty { get; set; } = "intermediate"; // easy, intermediate, hard
    }

    /// <summary>
    /// Single quiz question
    /// </summary>
    public class QuizQuestion
    {
        public int QuestionId { get; set; }
        public string QuestionText { get; set; }
        public string QuestionType { get; set; } // multiple-choice, true-false, short-answer
        public List<string> Options { get; set; } = new();
        public string Difficulty { get; set; }
        public string CorrectAnswer { get; set; }
        public string Explanation { get; set; }
    }

    /// <summary>
    /// Response containing generated quiz
    /// </summary>
    public class QuizGenerationResponse
    {
        public int QuizId { get; set; }
        public int UserId { get; set; }
        public int SubjectId { get; set; }
        public List<QuizQuestion> Questions { get; set; } = new();
        public int EstimatedDurationMinutes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    #endregion

    #region Performance Metrics

    /// <summary>
    /// Response containing user performance metrics
    /// </summary>
    public class PerformanceMetricsResponse
    {
        public int UserId { get; set; }
        public decimal PerformanceScore { get; set; } // 0-100
        public decimal LearningRate { get; set; } // topics per week
        public int CompletionRate { get; set; } // 0-100 percentage
        public int EngagementScore { get; set; } // 0-100

        public ClassComparison CompareToAverage { get; set; }

        public string TimePeriod { get; set; } = "7days";
        public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Comparison with class average
    /// </summary>
    public class ClassComparison
    {
        public decimal YourScore { get; set; }
        public decimal ClassAverage { get; set; }
        public int Percentile { get; set; } // 0-100
    }

    #endregion

    #region Learning Path

    /// <summary>
    /// Request to generate personalized learning path
    /// </summary>
    public class LearningPathRequest
    {
        [Required(ErrorMessage = "User ID is required")]
        public int UserId { get; set; }

        [Required(ErrorMessage = "Goal subject is required")]
        [MaxLength(200)]
        public string GoalSubject { get; set; }

        [Range(1, 52, ErrorMessage = "Timeframe must be between 1 and 52 weeks")]
        public int TimeframeWeeks { get; set; } = 8;

        [Range(1, 168, ErrorMessage = "Available hours must be between 1 and 168 per week")]
        public int AvailableHoursPerWeek { get; set; } = 10;
    }

    /// <summary>
    /// Single week in learning path
    /// </summary>
    public class LearningPathWeek
    {
        public int WeekNumber { get; set; }
        public List<string> Topics { get; set; } = new();
        public int EstimatedHours { get; set; }
        public List<LearningResource> Resources { get; set; } = new();
    }

    /// <summary>
    /// Learning resource for path
    /// </summary>
    public class LearningResource
    {
        public int ResourceId { get; set; }
        public string ResourceName { get; set; }
        public string Type { get; set; } // video, article, exercise, project
        public string Url { get; set; }
    }

    /// <summary>
    /// Response containing personalized learning path
    /// </summary>
    public class LearningPathResponse
    {
        public int UserId { get; set; }
        public int PathId { get; set; }
        public string GoalSubject { get; set; }
        public List<LearningPathWeek> Weeks { get; set; } = new();
        public DateTime CompletionEstimate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    #endregion

    #region Predict Success

    /// <summary>
    /// Request to predict user success probability for a subject
    /// </summary>
    public class PredictSuccessRequest
    {
        [Required(ErrorMessage = "Subject ID is required")]
        public int SubjectId { get; set; }

        public int UserId { get; set; }
        public string UserSkillLevel { get; set; } = "intermediate"; // beginner, intermediate, advanced
    }

    #endregion

    #region Study Plan

    /// <summary>
    /// Request to generate a personalized study plan
    /// </summary>
    public class StudyPlanRequest
    {
        [Required(ErrorMessage = "User ID is required")]
        public int UserId { get; set; }

        [Required(ErrorMessage = "Subject name is required")]
        [MaxLength(200)]
        public string SubjectName { get; set; }

        [Range(1, 52, ErrorMessage = "Duration must be between 1 and 52 weeks")]
        public int DurationWeeks { get; set; } = 8;

        [Range(1, 168, ErrorMessage = "Hours per week must be between 1 and 168")]
        public int HoursPerWeek { get; set; } = 10;

        public string LearningStyle { get; set; } = "mixed"; // visual, auditory, kinesthetic, mixed
    }

    #endregion

    #region Quiz Explanation

    /// <summary>
    /// Request to get a WinAI explanation for a wrong quiz answer
    /// </summary>
    public class ExplainErrorRequest
    {
        [Required(ErrorMessage = "Question text is required")]
        [MaxLength(2000)]
        public string QuestionText { get; set; } = string.Empty;

        [Required(ErrorMessage = "Wrong answer is required")]
        [MaxLength(500)]
        public string WrongAnswer { get; set; } = string.Empty;

        [Required(ErrorMessage = "Correct answer is required")]
        [MaxLength(500)]
        public string CorrectAnswer { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Subject { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Level { get; set; } = string.Empty;
    }

    #endregion

    // NOTE: ChatRequest et ChatResponse ont été déplacés vers ChatbotDTOs.cs
    // pour éviter la duplication et centraliser les DTOs du chatbot

    #region Study Habits

    /// <summary>
    /// Response containing user study habits analysis
    /// </summary>
    public class StudyHabitsResponse
    {
        public double AverageDailyHours { get; set; }
        public string PreferredStudyTime { get; set; } // Morning, Afternoon, Evening, Night
        public string MostActiveDay { get; set; } // Day of week
        public double CompletionRate { get; set; } // 0-1
        public DateTime LastStudySession { get; set; }
        public int TotalStudySessionsThisMonth { get; set; }
        public string LearningPattern { get; set; } // Consistent, Irregular, Sporadic
        public List<string> Strengths { get; set; } = new();
        public List<string> AreasForImprovement { get; set; } = new();
    }

    #endregion
}