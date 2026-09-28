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
        // Module 23 : GetRecommendationsAsync, AnalyzeProgressAsync et
        // GetPerformanceMetricsAsync ont été retirées avec leurs équivalents de
        // FastApiClient (transport de paramètre erroné, objets sans
        // correspondance avec Python, route de performance inexistante).
        // AIController relaie désormais directement la réponse Python.

        /// <summary>
        /// Parcours personnalisé. Renvoie null si Python n'a pas pu le calculer
        /// (données insuffisantes, service indisponible) : l'appelant doit
        /// présenter un état d'erreur, pas un parcours vide.
        /// </summary>
        Task<LearningPathResponse?> GeneratePersonalizedPathAsync(int userId, string goalSubject, int weeks, int hoursPerWeek);
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

        public async Task<LearningPathResponse?> GeneratePersonalizedPathAsync(int userId, string goalSubject, int weeks, int hoursPerWeek)
        {
            if (userId <= 0) throw new ArgumentException("Invalid user ID");
            if (weeks <= 0 || weeks > 52) throw new ArgumentException("Weeks must be between 1 and 52");
            if (hoursPerWeek <= 0 || hoursPerWeek > 168) throw new ArgumentException("Hours per week must be between 1 and 168");
            // goalSubject, weeks et hoursPerWeek restent validés (contrat des
            // routes /ai/personalized-path et /ai/study-plan) mais ne sont pas
            // transmis : le calcul Python ne s'appuie que sur les performances
            // réelles de l'élève (GET /api/learning-path/{user_id}).
            _logger.LogInformation("Generating learning path for user {UserId}", userId);
            return await _fastapiClient.GenerateLearningPathAsync(userId);
        }
    }
