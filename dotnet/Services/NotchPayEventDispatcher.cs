using Backend.Models.DTOs;

namespace Backend.Services;

/// <summary>
/// Aiguillage d'un événement de paiement NotchPay vers le module concerné :
/// réservation Répétiteur (TBK-), session payante (SESS-), engagement mensuel
/// (TEN-), sinon commande catalogue, abonnement ou recharge (PaymentService).
/// Partagé par le webhook NotchPay et par la confirmation des paiements
/// simulés de l'intégration, pour que les deux suivent exactement le même
/// traitement.
/// </summary>
public interface INotchPayEventDispatcher
{
    Task DispatchAsync(string eventId, string eventType, NotchPayWebhookTransaction transaction);
}

public sealed class NotchPayEventDispatcher : INotchPayEventDispatcher
{
    private readonly IPaymentService _paymentService;
    private readonly ITutorBookingService _tutorBookingService;
    private readonly ITeachingSessionService _teachingSessionService;
    private readonly ITutorEngagementService _tutorEngagementService;
    private readonly Backend.Services.Marketing.IConversionTracker? _conversions;

    public NotchPayEventDispatcher(
        IPaymentService paymentService,
        ITutorBookingService tutorBookingService,
        ITeachingSessionService teachingSessionService,
        ITutorEngagementService tutorEngagementService,
        Backend.Services.Marketing.IConversionTracker? conversions = null)
    {
        _paymentService = paymentService;
        _tutorBookingService = tutorBookingService;
        _teachingSessionService = teachingSessionService;
        _tutorEngagementService = tutorEngagementService;
        _conversions = conversions;
    }

    public async Task DispatchAsync(string eventId, string eventType, NotchPayWebhookTransaction transaction)
    {
        // Les réservations Répétiteur (Module 6) portent leur propre paiement,
        // hors du module Order/Payment : référence préfixée "TBK-", distincte
        // du "WP-" utilisé par le checkout catalogue.
        var handledAsBooking = await _tutorBookingService.TryHandleNotchPayWebhookAsync(eventId, eventType, transaction);
        // Lot 10.3 : conversion serveur d'une réservation payée (sans
        // effet si non payée, sans consentement ou déjà enregistrée ;
        // écriture en base seulement, ne lève jamais). Les commandes
        // catalogue et abonnement sont comptées dans PaymentService.
        if (handledAsBooking && _conversions != null && transaction.Reference is { Length: > 0 } bookingRef)
            await _conversions.RecordTutorBookingPurchaseAsync(bookingRef);
        // Module 18 : les inscriptions à une session payante portent leur
        // propre paiement (référence "SESS-"), hors du module Order/Payment,
        // exactement comme les réservations Répétiteur ci-dessus.
        var handledAsSession = !handledAsBooking && await _teachingSessionService.TryHandleNotchPayWebhookAsync(eventId, eventType, transaction);
        // Lot 9.4 : paiement unique d'un engagement mensuel (référence "TEN-").
        var handledAsEngagement = !handledAsBooking && !handledAsSession
            && await _tutorEngagementService.TryHandleNotchPayWebhookAsync(eventId, eventType, transaction);
        if (!handledAsBooking && !handledAsSession && !handledAsEngagement)
            await _paymentService.HandleNotchPayWebhookAsync(eventId, eventType, transaction);
    }
}
