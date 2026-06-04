using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Backend.Models.DTOs;

namespace Backend.Services;

/// <summary>
/// Interface for AI service operations
/// </summary>
    public interface IAIService
    {
        Task<RecommendationResponse> GetRecommendationsAsync(int userId, int count, string preferenceLevel, string category);
        Task<ProgressAnalysisResponse> AnalyzeProgressAsync(int userId, int subjectId, string depth);
        Task<QuizGenerationResponse> GenerateQuizAsync(int userId, int subjectId, int questionCount, string difficulty);
        Task<PerformanceMetricsResponse> GetPerformanceMetricsAsync(int userId, string timePeriod = "7days");
        Task<LearningPathResponse> GeneratePersonalizedPathAsync(int userId, string goalSubject, int weeks, int hoursPerWeek);
    }

    /// <summary>
    /// Service for AI-powered features
    /// Orchestrates FastAPI client
    /// </summary>
    public class AIService : IAIService
    {
        private readonly IFastApiClient _fastapiClient;
        private readonly ILogger<AIService> _logger;

        public AIService(
            IFastApiClient fastapiClient,
            ILogger<AIService> logger)
        {
            _fastapiClient = fastapiClient ?? throw new ArgumentNullException(nameof(fastapiClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<RecommendationResponse> GetRecommendationsAsync(int userId, int count, string preferenceLevel, string category)
        {
            if (userId <= 0) throw new ArgumentException("Invalid user ID");
            _logger.LogInformation($"Getting {count} recommendations for user {userId}");
            return await _fastapiClient.GetRecommendationsAsync(userId, preferenceLevel, category);
        }

        public async Task<ProgressAnalysisResponse> AnalyzeProgressAsync(int userId, int subjectId, string depth)
        {
            if (userId <= 0) throw new ArgumentException("Invalid user ID");
            _logger.LogInformation($"Analyzing progress for user {userId}, subject {subjectId}");
            return await _fastapiClient.AnalyzeProgressAsync(userId, subjectId, depth);
        }

        public async Task<QuizGenerationResponse> GenerateQuizAsync(int userId, int subjectId, int questionCount, string difficulty)
        {
            if (userId <= 0) throw new ArgumentException("Invalid user ID");
            if (questionCount <= 0 || questionCount > 100) throw new ArgumentException("Question count must be between 1 and 100");
            _logger.LogInformation($"Generating {questionCount} quiz questions for user {userId}, subject {subjectId}");
            return await _fastapiClient.GenerateQuizAsync(userId, subjectId, questionCount, difficulty);
        }

        public async Task<PerformanceMetricsResponse> GetPerformanceMetricsAsync(int userId, string timePeriod = "7days")
        {
            if (userId <= 0) throw new ArgumentException("Invalid user ID");
            _logger.LogInformation($"Getting performance metrics for user {userId}");
            return await _fastapiClient.GetPerformanceAsync(userId, timePeriod);
        }

        public async Task<LearningPathResponse> GeneratePersonalizedPathAsync(int userId, string goalSubject, int weeks, int hoursPerWeek)
        {
            if (userId <= 0) throw new ArgumentException("Invalid user ID");
            if (weeks <= 0 || weeks > 52) throw new ArgumentException("Weeks must be between 1 and 52");
            if (hoursPerWeek <= 0 || hoursPerWeek > 168) throw new ArgumentException("Hours per week must be between 1 and 168");
            _logger.LogInformation($"Generating learning path for user {userId}: {weeks} weeks, {hoursPerWeek} hours/week");
            return await _fastapiClient.GenerateLearningPathAsync(userId, goalSubject, weeks, hoursPerWeek);
        }
    }
