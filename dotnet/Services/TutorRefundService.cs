using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Remboursements automatiques des séances de tutorat (lot 9.2, suivi 17.9).
///
/// Remplace l'ancien remboursement simulé (changement de statut + alerte admin
/// « remboursement manuel requis ») : le remboursement est crédité tout de
/// suite au portefeuille du payeur, quel que soit le moyen de paiement
/// d'origine, selon <see cref="TutorRefundRules"/>.
///
/// Déroulé, en deux temps :
/// <list type="number">
///   <item>la décision (montants, qui supporte les frais) est figée sur la
///   réservation, avec la retenue de frais du tuteur, dans une seule
///   transaction sous le verrou du portefeuille du tuteur : le statut, la
///   décision et la retenue sont validés ensemble ou pas du tout ;</item>
///   <item>les écritures (crédit du payeur, part conservée, frais) sont posées
///   par <see cref="IWalletService.SyncTutorBookingAsync"/>, idempotent et
///   rejoué par la réconciliation périodique si le crédit échoue.</item>
/// </list>
/// Une décision déjà figée (<see cref="TutorBooking.RefundedAt"/>) n'est jamais
/// recalculée : un double clic, un webhook rejoué ou une tâche de fond qui
/// repasse ne remboursent pas deux fois.
/// </summary>
public interface ITutorRefundService
{
    /// <summary>
    /// Fige la décision de remboursement de <paramref name="booking"/> (entité
    /// suivie, statut déjà changé par l'appelant, non encore enregistré),
    /// enregistre, puis crédite le payeur. Sans paiement encaissé, enregistre
    /// seulement et renvoie null.
    /// </summary>
    Task<TutorRefundDecision?> RefundAsync(TutorBooking booking, TutorRefundCause cause, decimal? disputeAmountXaf = null, int? overridePercent = null);
}

public class TutorRefundService : ITutorRefundService
{
    private readonly ApplicationDbContext _db;
    private readonly IWalletService _wallet;
    private readonly INtfyService _ntfy;
    private readonly ILogger<TutorRefundService> _logger;
    private readonly TutorPolicySettings _settings;

    public TutorRefundService(ApplicationDbContext db, IWalletService wallet, INtfyService ntfy, ILogger<TutorRefundService> logger, IConfiguration configuration)
    {
        _db = db;
        _wallet = wallet;
        _ntfy = ntfy;
        _logger = logger;
        _settings = TutorPolicySettings.From(configuration);
    }

    public static string CauseCode(TutorRefundCause cause) => cause switch
    {
        TutorRefundCause.PayerCancelled => "payer_cancelled",
        TutorRefundCause.TutorCancelled => "tutor_cancelled",
        TutorRefundCause.TutorDeclined => "tutor_declined",
        TutorRefundCause.TutorNoResponse => "tutor_no_response",
        TutorRefundCause.DisputeRefundFull => "dispute_full",
        TutorRefundCause.DisputeRefundPartial => "dispute_partial",
        TutorRefundCause.InPersonBlocked => "in_person_blocked",
        _ => "other",
    };

    /// <summary>Frais du paiement d'origine : figés à la confirmation, sinon recalculés au taux courant (réservations antérieures au 9.2).</summary>
    private decimal OriginalFee(TutorBooking booking) =>
        TutorPaymentMethods.IsWallet(booking.PaymentMethod)
            ? 0m
            : booking.PaymentFeeXaf ?? TutorRefundRules.NotchPayFee(booking.PriceXaf, _settings.NotchPayFeePercent);

    public async Task<TutorRefundDecision?> RefundAsync(TutorBooking booking, TutorRefundCause cause, decimal? disputeAmountXaf = null, int? overridePercent = null)
    {
        if (booking.RefundedAt != null || !string.Equals(booking.PaymentStatus, "completed", StringComparison.OrdinalIgnoreCase))
        {
            await _db.SaveChangesAsync();
            return null;
        }

        if (booking.TutorProfile == null)
            await _db.Entry(booking).Reference(b => b.TutorProfile).LoadAsync();
        var tutorUserId = booking.TutorProfile?.UserId;

        var percent = 100;
        if (cause == TutorRefundCause.PayerCancelled && overridePercent is int forced)
        {
            percent = Math.Clamp(forced, 0, 100);
        }
        else if (cause == TutorRefundCause.PayerCancelled)
        {
            var sessionStartUtc = booking.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(booking.StartTime), DateTimeKind.Utc);
            percent = TutorRefundRules.RefundPercentFor((sessionStartUtc - DateTime.UtcNow).TotalHours, TutorPolicy.Effective(booking.TutorProfile));
        }

        var decision = TutorRefundRules.Compute(cause, booking.PriceXaf, OriginalFee(booking), percent, disputeAmountXaf);
        var now = DateTime.UtcNow;
        booking.RefundedAt = now;
        booking.RefundCause = CauseCode(cause);
        booking.RefundPercent = decision.RefundPercent;
        booking.RefundAmountXaf = decision.RefundXaf;
        booking.RefundFeeXaf = decision.FeeRetainedXaf;
        booking.RefundFeeBorneBy = decision.FeeBorneBy;
        booking.TutorRetainedXaf = decision.TutorRetainedXaf;
        booking.TutorFeeChargedXaf = 0m;
        booking.PaymentStatus = decision.RefundXaf > 0 ? "refunded" : "forfeited";
        booking.UpdatedAt = now;

        if (decision.FeeBorneBy == TutorFeeBearer.Tutor && decision.FeeRetainedXaf > 0 && tutorUserId.HasValue)
        {
            // Retenue sur le tuteur, bornée par son solde disponible lu sous
            // son verrou (jamais de solde négatif : un solde négatif est une
            // anomalie du registre). Le reste éventuel est absorbé par la
            // plateforme. Le statut de la réservation, la décision et la
            // retenue sont validés dans la même transaction.
            await _wallet.RunLockedAsync(tutorUserId.Value, async () =>
            {
                var available = await _wallet.GetAvailableAsync(tutorUserId.Value);
                var charged = Math.Min(decision.FeeRetainedXaf, Math.Max(0m, RevenueSplit.Xaf(available)));
                booking.TutorFeeChargedXaf = charged;
                await _db.SaveChangesAsync();
                if (charged > 0)
                    await _wallet.PostAsync(new WalletEntry(tutorUserId.Value, WalletEntryTypes.TutoringFeeCharge, -charged,
                        WalletService.TutoringFeeChargeKey(booking.Id),
                        $"Frais Mobile Money retenus — séance du {booking.SessionDate:dd/MM/yyyy} annulée de ton fait",
                        "TutorBooking", booking.Id, OccurredAt: now));
                return charged;
            });
        }
        else
        {
            await _db.SaveChangesAsync();
        }

        await CreditAsync(booking, decision);
        return decision;
    }

    /// <summary>
    /// Pose les écritures du remboursement. Un échec technique ne perd rien :
    /// la décision est figée, la réconciliation périodique rejoue la pose ;
    /// l'admin est seulement alerté pour suivre le cas.
    /// </summary>
    private async Task CreditAsync(TutorBooking booking, TutorRefundDecision decision)
    {
        try
        {
            await _wallet.SyncTutorBookingAsync(booking.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Crédit du remboursement de la réservation {BookingId} non posé (réconciliation à venir)", booking.Id);
            try
            {
                await _ntfy.PublishAdminAsync("Échec technique du crédit de remboursement — cours particulier",
                    $"Réservation #{booking.Id} : le remboursement de {decision.RefundXaf:0} XAF au portefeuille du compte #{booking.PayerUserId ?? booking.StudentUserId} " +
                    "n'a pas pu être posé. La décision est enregistrée ; la réconciliation réessaie toutes les 30 min. Vérifier le registre si l'alerte se répète.",
                    tags: new[] { "warning" });
            }
            catch (Exception notifyEx)
            {
                _logger.LogError(notifyEx, "Alerte admin d'échec de crédit non envoyée (réservation {BookingId})", booking.Id);
            }
        }
    }
}
