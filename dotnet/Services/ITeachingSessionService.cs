using Backend.Models.DTOs;

namespace Backend.Services;

/// <summary>
/// Sessions d'enseignement en ligne (Module 5  live/enregistrement/correction).
/// Nom distinct d'ISessionService (déjà pris par les sessions de connexion
/// /appareil, UserSessions) pour éviter toute collision de nom.
/// </summary>
public interface ITeachingSessionService
{
    Task<TeachingSessionDto> CreateAsync(int teacherId, CreateSessionRequestDto request);
    Task<List<TeachingSessionDto>> GetTeacherSessionsAsync(int teacherId, DateTime? from = null, DateTime? to = null);
    Task<TeachingSessionDto?> GetByIdAsync(int id, int? viewerUserId = null);
    Task<TeachingSessionDto> CancelAsync(int teacherId, int sessionId, CancelSessionRequestDto request);
    Task<TeachingSessionDto> EnrollAsync(int studentId, int sessionId, EnrollSessionRequestDto request);
    Task SetSummaryAsync(int teacherId, int sessionId, string summaryText);

    /// <summary>
    /// Module 18 : traite une notification NotchPay dont la référence porte le
    /// préfixe "SESS-" (inscription à une session payante), sur le modèle de
    /// <c>TutorBookingService.TryHandleNotchPayWebhookAsync</c>. Renvoie faux
    /// si la référence n'est pas celle d'une inscription de session (laisse la
    /// chaîne de traitement du webhook essayer les autres gestionnaires).
    /// </summary>
    Task<bool> TryHandleNotchPayWebhookAsync(string eventId, string eventType, NotchPayWebhookTransaction transaction);
}
