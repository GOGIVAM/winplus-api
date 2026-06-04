using Backend.Models.DTOs;

namespace Backend.Services;

/// <summary>
/// Interface pour la gestion des Quiz
/// </summary>
public interface IQuizService
{
    /// <summary>
    /// Récupère un quiz par son ID
    /// </summary>
    Task<QuizDto?> GetQuizByIdAsync(int id);

    /// <summary>
    /// Récupère tous les quiz avec pagination
    /// </summary>
    Task<IEnumerable<QuizDto>> GetAllQuizzesAsync(int page = 1, int pageSize = 20);

    /// <summary>
    /// Récupère les quiz filtrés
    /// </summary>
    Task<IEnumerable<QuizDto>> GetQuizzesAsync(QuizSearchFilterDto filter);

    /// <summary>
    /// Récupère les quiz par sujet
    /// </summary>
    Task<IEnumerable<QuizDto>> GetQuizzesBySubjectAsync(string subject, int page = 1, int pageSize = 20);

    /// <summary>
    /// Récupère les quiz par niveau de difficulté
    /// </summary>
    Task<IEnumerable<QuizDto>> GetQuizzesByDifficultyAsync(string difficulty, int page = 1, int pageSize = 20);

    /// <summary>
    /// Recherche des quiz
    /// </summary>
    Task<IEnumerable<QuizDto>> SearchQuizzesAsync(string searchTerm, int page = 1, int pageSize = 20);

    /// <summary>
    /// Récupère les quiz publiés
    /// </summary>
    Task<IEnumerable<QuizDto>> GetPublishedQuizzesAsync(int page = 1, int pageSize = 20);

    /// <summary>
    /// Soumet les réponses d'un quiz et obtient les résultats avec évaluation
    /// </summary>
    Task<QuizResultResponseDto> SubmitQuizAttemptAsync(int quizId, int userId, SubmitQuizAttemptRequestDto request);

    /// <summary>
    /// Récupère les résultats d'une tentative de quiz
    /// </summary>
    Task<QuizAttemptDto?> GetQuizAttemptAsync(int attemptId);

    /// <summary>
    /// Récupère les tentatives d'un utilisateur pour un quiz
    /// </summary>
    Task<IEnumerable<QuizAttemptDto>> GetUserQuizAttemptsAsync(int userId, int quizId);

    /// <summary>
    /// Récupère toutes les tentatives d'un utilisateur
    /// </summary>
    Task<IEnumerable<QuizAttemptDto>> GetUserAllQuizAttemptsAsync(int userId, int page = 1, int pageSize = 20);

    /// <summary>
    /// Crée un nouveau quiz
    /// </summary>
    Task<QuizDto> CreateQuizAsync(CreateQuizRequestDto request);

    /// <summary>
    /// Met à jour un quiz
    /// </summary>
    Task<QuizDto> UpdateQuizAsync(int id, UpdateQuizRequestDto request);

    /// <summary>
    /// Publie un quiz
    /// </summary>
    Task<QuizDto> PublishQuizAsync(int id);

    /// <summary>
    /// D'publie un quiz
    /// </summary>
    Task<QuizDto> UnpublishQuizAsync(int id);

    /// <summary>
    /// Supprime un quiz (soft delete)
    /// </summary>
    Task<bool> DeleteQuizAsync(int id);

    /// <summary>
    /// Récupère les statistiques d'un quiz
    /// </summary>
    Task<object> GetQuizStatsAsync(int id);

    /// <summary>
    /// Obtient le score moyen d'un quiz
    /// </summary>
    Task<double> GetQuizAverageScoreAsync(int id);
}
