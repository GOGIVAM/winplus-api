using Backend.Models.DTOs;

namespace Backend.Services;

/// <summary>
/// Interface pour la gestion des Révisions/Guides d'Étude
/// </summary>
public interface IRevisionService
{
    /// <summary>
    /// Récupère une révision par son ID
    /// </summary>
    Task<RevisionDto?> GetRevisionByIdAsync(int id);

    /// <summary>
    /// Récupère toutes les révisions avec pagination
    /// </summary>
    Task<IEnumerable<RevisionDto>> GetAllRevisionsAsync(int page = 1, int pageSize = 20);

    /// <summary>
    /// Récupère les révisions filtrées
    /// </summary>
    Task<IEnumerable<RevisionDto>> GetRevisionsAsync(RevisionSearchFilterDto filter);

    /// <summary>
    /// Récupère les révisions par sujet
    /// </summary>
    Task<IEnumerable<RevisionDto>> GetRevisionsBySubjectAsync(string subject, int page = 1, int pageSize = 20);

    /// <summary>
    /// Récupère les révisions assignées à un utilisateur
    /// </summary>
    Task<IEnumerable<RevisionDto>> GetAssignedRevisionsAsync(int userId, int page = 1, int pageSize = 20);

    /// <summary>
    /// Recherche des révisions
    /// </summary>
    Task<IEnumerable<RevisionDto>> SearchRevisionsAsync(string searchTerm, int page = 1, int pageSize = 20);

    /// <summary>
    /// Récupère les révisions publiées
    /// </summary>
    Task<IEnumerable<RevisionDto>> GetPublishedRevisionsAsync(int page = 1, int pageSize = 20);

    /// <summary>
    /// Démarre une révision pour un utilisateur
    /// </summary>
    Task<RevisionEnrollmentDto> StartRevisionAsync(int revisionId, int userId, StartRevisionRequestDto request);

    /// <summary>
    /// Complète une révision pour un utilisateur
    /// </summary>
    Task<RevisionEnrollmentDto> CompleteRevisionAsync(int revisionId, int userId, CompleteRevisionRequestDto request);

    /// <summary>
    /// Récupère la progression d'un utilisateur dans une révision
    /// </summary>
    Task<RevisionProgressResponseDto?> GetRevisionProgressAsync(int revisionId, int userId);

    /// <summary>
    /// Crée une nouvelle révision
    /// </summary>
    Task<RevisionDto> CreateRevisionAsync(CreateRevisionRequestDto request);

    /// <summary>
    /// Met à jour une révision
    /// </summary>
    Task<RevisionDto> UpdateRevisionAsync(int id, UpdateRevisionRequestDto request);

    /// <summary>
    /// Publie une révision
    /// </summary>
    Task<RevisionDto> PublishRevisionAsync(int id);

    /// <summary>
    /// D'publie une révision
    /// </summary>
    Task<RevisionDto> UnpublishRevisionAsync(int id);

    /// <summary>
    /// Supprime une révision (soft delete)
    /// </summary>
    Task<bool> DeleteRevisionAsync(int id);

    /// <summary>
    /// Assigne automatiquement une révision à un utilisateur basé sur ses résultats
    /// </summary>
    Task<RevisionEnrollmentDto> AutoAssignRevisionAsync(int userId, int revisionId);

    /// <summary>
    /// Récupère les statistiques d'une révision
    /// </summary>
    Task<object> GetRevisionStatsAsync(int id);

    /// <summary>
    /// Analyse les scores par sujet et assigne les révisions appropriées
    /// </summary>
    Task<IEnumerable<RevisionEnrollmentDto>> AssignRevisionsBasedOnScoresAsync(int userId);

    /// <summary>
    /// Génère une fiche de révision personnalisée par IA (erreurs de quiz
    /// récentes, épreuves téléchargées, objectifs actifs) et l'attribue à
    /// l'utilisateur. Si <paramref name="subject"/> est omis, la matière la
    /// plus faible de l'utilisateur est utilisée.
    /// </summary>
    Task<RevisionDto> GenerateAIRevisionAsync(int userId, string? subject, string? topic, string? difficulty = null);

    /// <summary>Fiches IA générées pour cet utilisateur, actives par défaut (ou masquées si demandé).</summary>
    Task<IEnumerable<RevisionDto>> GetMyGeneratedRevisionsAsync(int userId, bool includeHidden = false, int page = 1, int pageSize = 50);

    /// <summary>Masque/restaure une fiche IA de la liste active, sans toucher à sa progression.</summary>
    Task HideRevisionAsync(int userId, int id, bool hide);

    /// <summary>Enregistre le signalement libre de l'élève sur cette fiche IA.</summary>
    Task SetRevisionContentFeedbackAsync(int userId, int id, string feedback);

    /// <summary>Supprime définitivement (IsDeleted) les fiches déjà masquées de cet utilisateur.</summary>
    Task ClearMyRevisionHistoryAsync(int userId);

    /// <summary>Dates de complétion ascendantes  graphique "Fiches terminées dans le temps".</summary>
    Task<IEnumerable<DateTime>> GetCompletionTimelineAsync(int userId);
}
