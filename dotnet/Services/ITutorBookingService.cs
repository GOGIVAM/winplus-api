using Backend.Models.DTOs;
using Backend.Models.Entities;

namespace Backend.Services;

public interface ITutorBookingService
{
    Task<TutorBookingCreatedResponseDto> CreateBookingAsync(int studentUserId, CreateTutorBookingRequestDto request);
    Task<TutorBookingDto> GetByIdAsync(int userId, int bookingId);
    Task<List<TutorBookingDto>> GetMyBookingsAsStudentAsync(int studentUserId);
    Task<List<TutorBookingDto>> GetMyBookingsAsTutorAsync(int tutorUserId);
    Task<TutorBookingDto> CancelBookingAsync(int userId, int bookingId, string? reason);
    Task<TutorBookingDto> ConfirmBookingAsync(int tutorUserId, int bookingId);
    Task<TutorBookingDto> DeclineBookingAsync(int tutorUserId, int bookingId, string? reason);
    Task<TutorBookingDto> CompleteBookingAsync(int tutorUserId, int bookingId);
    Task<TutorBookingDto> DisputeBookingAsync(int studentUserId, int bookingId, string reason);
    Task<List<TutorPendingBookingDto>> GetPendingRequestsAsync(int tutorUserId);
    Task<List<TutorAvailabilityOccurrenceDto>> GetAvailabilityCalendarAsync(int tutorUserId, DateOnly weekStart);

    /// <summary>true si la référence appartient à une réservation Répétiteur (préfixe "TBK-") et a été traitée.</summary>
    Task<bool> TryHandleNotchPayWebhookAsync(string eventId, string eventType, NotchPayWebhookTransaction transaction);

    /// <summary>Enregistre le compte-rendu WinAI édité par le répétiteur (US-REP-07).</summary>
    Task<TutorBookingDto> SetSummaryAsync(int tutorUserId, int bookingId, string summaryText);

    /// <summary>Décision support sur un litige (US-REP-09) : rembourse tout/partie ou libère les fonds au répétiteur.</summary>
    Task<TutorBookingDto> ResolveDisputeAsync(int adminUserId, int bookingId, string resolution, string? note);

    /// <summary>Liste des réservations actuellement contestées, pour le panneau admin.</summary>
    Task<List<TutorBookingDto>> GetDisputedBookingsAsync();
}
