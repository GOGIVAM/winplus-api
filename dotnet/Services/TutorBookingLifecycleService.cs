using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Fait avancer automatiquement le cycle de vie des réservations de cours
/// particulier (Module 6, US-REP-05/06) : expire les demandes non traitées
/// dans le délai de préavis du répétiteur (remboursement intégral automatique
/// au portefeuille du payeur, frais retenus sur le tuteur, lot 9.2), et libère
/// l'argent du tuteur une fois la fenêtre de contestation écoulée après la
/// séance marquée "Effectuée" sans contestation (17.8 : 24 h par défaut,
/// réglable par <c>TutorBooking:DisputeWindowHours</c>). Même pattern que
/// <see cref="PaymentExpirationService"/>, avec un intervalle plus court car
/// les demandes de réservation sont sensibles au délai.
/// </summary>
public class TutorBookingLifecycleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TutorBookingLifecycleService> _logger;
    private readonly TutorPolicySettings _policy;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private static readonly string[] RespondedStatuses = { "pending_tutor_approval" };

    public TutorBookingLifecycleService(IServiceScopeFactory scopeFactory, ILogger<TutorBookingLifecycleService> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _policy = TutorPolicySettings.From(configuration);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TutorBookingLifecycleService démarré (intervalle: 15 minutes, contestation: {Hours} h)", _policy.DisputeWindowHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireStaleRequestsAsync(stoppingToken);
                await ReleaseEscrowAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du traitement du cycle de vie des réservations Répétiteur");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ExpireStaleRequestsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();
        var refunds = scope.ServiceProvider.GetRequiredService<ITutorRefundService>();

        var pending = await db.TutorBookings
            .Include(b => b.TutorProfile)
            // Lot 9.4 : une séance d'engagement mensuel n'expire jamais seule ;
            // l'engagement entier expire (TutorEngagementLifecycleService).
            .Where(b => RespondedStatuses.Contains(b.Status) && b.TutorEngagementId == null)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var expired = pending.Where(b => now >= GetResponseDeadline(b)).ToList();
        if (expired.Count == 0) return;

        foreach (var booking in expired)
        {
            // Une réservation à la fois : la décision de remboursement, la
            // retenue de frais du tuteur et le nouveau statut sont validés
            // ensemble (TutorRefundService). Une tâche qui repasse ne retrouve
            // plus cette réservation (statut "expired"), et une décision déjà
            // figée n'est jamais recalculée.
            booking.Status = "expired";
            booking.UpdatedAt = now;
            TutorRefundDecision? decision;
            try
            {
                decision = await refunds.RefundAsync(booking, TutorRefundCause.TutorNoResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Expiration de la réservation {BookingId} non enregistrée", booking.Id);
                db.ChangeTracker.Clear();
                continue;
            }

            var payerId = booking.PayerUserId ?? booking.StudentUserId;
            await ntfy.PublishAsync($"winplus-user-{payerId}", "Demande expirée — remboursement effectué",
                decision is { RefundXaf: > 0 }
                    ? $"Le répétiteur n'a pas répondu à temps pour ta séance du {booking.SessionDate:dd/MM/yyyy}. {decision.RefundXaf:0} XAF ont été crédités sur ton portefeuille WinPlus (remboursement intégral)."
                    : $"Le répétiteur n'a pas répondu à temps pour ta séance du {booking.SessionDate:dd/MM/yyyy}. La demande est annulée.",
                userId: payerId, type: "TutorBooking");
            var tutorUserId = booking.TutorProfile?.UserId;
            if (tutorUserId.HasValue)
                await ntfy.PublishAsync($"winplus-user-{tutorUserId.Value}", "Demande expirée",
                    booking.TutorFeeChargedXaf is > 0
                        ? $"Tu n'as pas répondu à temps à la demande du {booking.SessionDate:dd/MM/yyyy} : elle a été annulée et les frais Mobile Money ({booking.TutorFeeChargedXaf:0} XAF) ont été retenus sur ton solde."
                        : $"Tu n'as pas répondu à temps à la demande du {booking.SessionDate:dd/MM/yyyy} : elle a été annulée.",
                    userId: tutorUserId.Value, type: "TutorBooking");
        }

        _logger.LogInformation("{Count} demande(s) de réservation expirée(s)", expired.Count);
    }

    private async Task ReleaseEscrowAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();

        var now = DateTime.UtcNow;
        var releaseThreshold = now.Subtract(_policy.DisputeWindow);
        var toRelease = await db.TutorBookings
            .Include(b => b.TutorProfile)
            .Where(b => b.Status == "completed" && b.EscrowReleasedAt == null && b.CompletedAt != null && b.CompletedAt <= releaseThreshold)
            .ToListAsync(ct);

        if (toRelease.Count == 0) return;

        foreach (var booking in toRelease)
        {
            booking.EscrowReleasedAt = now;
            booking.UpdatedAt = now;

            var tutorUserId = booking.TutorProfile?.UserId;
            if (tutorUserId.HasValue)
                await ntfy.PublishAsync($"winplus-user-{tutorUserId.Value}", "Fonds libérés",
                    $"Ta part de la séance du {booking.SessionDate:dd/MM/yyyy} ({booking.PriceXaf:0} XAF payés, commission déduite) est maintenant disponible dans ton solde WinPlus.",
                    userId: tutorUserId.Value, type: "TutorBooking");
            await ntfy.PublishAsync($"winplus-user-{booking.StudentUserId}", "Comment s'est passée ta séance ?",
                "Laisse un avis pour aider les autres élèves.", userId: booking.StudentUserId, type: "TutorBooking");
        }

        await db.SaveChangesAsync(ct);
        await SyncWalletAsync(scope, toRelease.Select(b => b.Id));
        _logger.LogInformation("{Count} réservation(s) libérée(s) de l'escrow", toRelease.Count);
    }

    /// <summary>
    /// Lot 2, Module 1 : la libération de l'escrow confirme l'écriture en
    /// attente du journal (seul moment où le revenu entre dans le solde
    /// disponible). Idempotent : une libération rejouée ne confirme pas deux
    /// fois le même revenu.
    /// </summary>
    private async Task SyncWalletAsync(IServiceScope scope, IEnumerable<int> bookingIds)
    {
        var wallet = scope.ServiceProvider.GetRequiredService<IWalletService>();
        foreach (var id in bookingIds)
        {
            try { await wallet.SyncTutorBookingAsync(id); }
            catch (Exception ex) { _logger.LogError(ex, "Écritures de la réservation {BookingId} non mises à jour (réconciliation à venir)", id); }
        }
    }

    /// <summary>Dupliqué depuis TutorBookingService (petit calcul autonome, pas de dépendance circulaire).</summary>
    private static DateTime GetResponseDeadline(Backend.Models.Entities.TutorBooking b)
    {
        var byNotice = b.CreatedAt.AddHours(b.TutorProfile?.NoticeHours ?? 24);
        var sessionStart = b.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(b.StartTime), DateTimeKind.Utc);
        var latest = sessionStart.AddHours(-1);
        return byNotice < latest ? byNotice : latest;
    }
}
