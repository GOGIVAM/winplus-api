using Backend.Models.DTOs;
using Backend.Models.Entities;

namespace Backend.Services;

public interface ITutorBookingService
{
    /// <summary>Devis serveur d'une séance (lot 9.1) : prix détaillé + disponibilité du créneau.</summary>
    Task<TutorPriceQuoteDto> GetQuoteAsync(TutorPriceQuoteRequestDto request);
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
    Task<TutorBookingDto> ResolveDisputeAsync(int adminUserId, int bookingId, string resolution, string? note, decimal? amountXaf = null);

    /// <summary>Liste des réservations actuellement contestées, pour le panneau admin.</summary>
    Task<List<TutorBookingDto>> GetDisputedBookingsAsync();

    // ── Tâches de fond (TutorBookingLifecycleService, suivi 17.15) ──

    /// <summary>
    /// Séances uniques restées en attente de paiement Mobile Money plus de
    /// 2 h : annulées, créneau libéré. Un encaissement arrivé ensuite est
    /// remboursé au portefeuille du payeur par le webhook. Idempotent.
    /// </summary>
    Task<int> CancelAbandonedPaymentsAsync(DateTime nowUtc);

    /// <summary>
    /// Séances confirmées (uniques ou d'engagement mensuel) non marquées
    /// effectuées 24 h après leur heure de fin : passées en « completed »,
    /// la fenêtre de contestation court à partir de ce passage. Idempotent.
    /// </summary>
    Task<int> AutoCompleteOverdueAsync(DateTime nowUtc);
}
