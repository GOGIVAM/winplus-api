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
    /// Récupère les quiz publiés. Un quiz généré par IA n'est renvoyé qu'à son
    /// propriétaire (viewerUserId) : sans ce filtre, tout quiz IA généré pour
    /// un élève était visible et rejouable par tous les autres via cette
    /// route  seuls les quiz admin (IsAIGenerated=false) restent partagés.
    /// </summary>
    Task<IEnumerable<QuizDto>> GetPublishedQuizzesAsync(int page = 1, int pageSize = 20, int? viewerUserId = null);

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
    /// Récupère (ou génère la première fois, à partir du PDF réel) le quiz
    /// d'évaluation chronométré rattaché à une épreuve précise.
    /// </summary>
    Task<QuizDto> GetOrCreateExamQuizAsync(int examId);

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

    /// <summary>
    /// Génère un quiz d'entraînement personnalisé par IA sur une matière (la
    /// plus faible de l'utilisateur si non précisée) et le publie.
    /// </summary>
    Task<QuizDto> GenerateAIQuizAsync(int userId, string? subject, string? topic, string? difficulty = null);

    /// <summary>Quiz IA générés par cet utilisateur, actifs par défaut (ou masqués si demandé).</summary>
    Task<IEnumerable<QuizDto>> GetMyGeneratedQuizzesAsync(int userId, bool includeHidden = false, int page = 1, int pageSize = 50);

    /// <summary>Masque/restaure un quiz IA de la liste active, sans toucher à ses statistiques.</summary>
    Task HideQuizAsync(int userId, int id, bool hide);

    /// <summary>Enregistre le signalement libre de l'élève sur ce quiz IA.</summary>
    Task SetQuizDifficultyFeedbackAsync(int userId, int id, string feedback);

    /// <summary>Supprime définitivement (IsDeleted) les quiz déjà masqués de cet utilisateur.</summary>
    Task ClearMyQuizHistoryAsync(int userId);
}
