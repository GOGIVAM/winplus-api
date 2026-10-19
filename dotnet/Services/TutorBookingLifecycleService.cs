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
/// réglable par <c>TutorBooking:DisputeWindowHours</c>). Depuis le 17.15, il
/// libère aussi le créneau des séances uniques dont le paiement Mobile Money
/// n'est pas confirmé après 2 h, et passe en « effectuée » les séances
/// confirmées non marquées 24 h après leur fin (la contestation court ensuite,
/// puis la libération ci-dessous s'applique). Même pattern que
/// <see cref="PaymentExpirationService"/>, avec un intervalle plus court car
/// les demandes de réservation sont sensibles au délai.
/// </summary>
public class TutorBookingLifecycleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TutorBookingLifecycleService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IPlatformSettings? _platformSettings;

    /// <summary>Relu à chaque passage : le délai de contestation est réglable dans l'admin (lot 9.7a).</summary>
    private TutorPolicySettings _policy => TutorPolicySettings.From(_configuration, _platformSettings);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private static readonly string[] RespondedStatuses = { "pending_tutor_approval" };

    public TutorBookingLifecycleService(IServiceScopeFactory scopeFactory, ILogger<TutorBookingLifecycleService> logger, IConfiguration configuration, IPlatformSettings? platformSettings = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
        _platformSettings = platformSettings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TutorBookingLifecycleService démarré (intervalle: 15 minutes, contestation: {Hours} h)", _policy.DisputeWindowHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 17.15 : paiements Mobile Money abandonnés (2 h) et passage
                // automatique en « effectuée » (24 h après la fin), chacun dans
                // son propre scope : l'échec de l'un n'empêche pas les autres.
                await RunServiceStepAsync("paiements abandonnés", s => s.CancelAbandonedPaymentsAsync(DateTime.UtcNow));
                await ExpireStaleRequestsAsync(stoppingToken);
                await RunServiceStepAsync("séances effectuées automatiquement", s => s.AutoCompleteOverdueAsync(DateTime.UtcNow));
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

    private async Task RunServiceStepAsync(string name, Func<ITutorBookingService, Task<int>> step)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var count = await step(scope.ServiceProvider.GetRequiredService<ITutorBookingService>());
            if (count > 0) _logger.LogInformation("Réservations Répétiteur, {Step} : {Count}", name, count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Réservations Répétiteur : échec de l'étape « {Step} »", name);
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
            // Lot 10.6 : dans la langue du destinataire (User.Locale).
            await ntfy.PublishLocalizedAsync(db, $"winplus-user-{payerId}", new("Demande expirée — remboursement effectué", "Request expired, refund made"),
                decision is { RefundXaf: > 0 }
                    ? new($"Le répétiteur n'a pas répondu à temps pour ta séance du {booking.SessionDate:dd/MM/yyyy}. {decision.RefundXaf:0} XAF ont été crédités sur ton portefeuille WinPlus (remboursement intégral).",
                          $"The tutor did not reply in time for your session on {booking.SessionDate:dd/MM/yyyy}. {decision.RefundXaf:0} XAF have been credited to your WinPlus wallet (full refund).")
                    : new($"Le répétiteur n'a pas répondu à temps pour ta séance du {booking.SessionDate:dd/MM/yyyy}. La demande est annulée.",
                          $"The tutor did not reply in time for your session on {booking.SessionDate:dd/MM/yyyy}. The request is cancelled."),
                userId: payerId, type: "TutorBooking");
            var tutorUserId = booking.TutorProfile?.UserId;
            if (tutorUserId.HasValue)
                await ntfy.PublishLocalizedAsync(db, $"winplus-user-{tutorUserId.Value}", new("Demande expirée", "Request expired"),
                    booking.TutorFeeChargedXaf is > 0
                        ? new($"Tu n'as pas répondu à temps à la demande du {booking.SessionDate:dd/MM/yyyy} : elle a été annulée et les frais Mobile Money ({booking.TutorFeeChargedXaf:0} XAF) ont été retenus sur ton solde.",
                              $"You did not reply in time to the request for {booking.SessionDate:dd/MM/yyyy}: it has been cancelled and the Mobile Money fees ({booking.TutorFeeChargedXaf:0} XAF) have been withheld from your balance.")
                        : new($"Tu n'as pas répondu à temps à la demande du {booking.SessionDate:dd/MM/yyyy} : elle a été annulée.",
                              $"You did not reply in time to the request for {booking.SessionDate:dd/MM/yyyy}: it has been cancelled."),
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
                await ntfy.PublishLocalizedAsync(db, $"winplus-user-{tutorUserId.Value}", new("Fonds libérés", "Funds released"),
                    new($"Ta part de la séance du {booking.SessionDate:dd/MM/yyyy} ({booking.PriceXaf:0} XAF payés, commission déduite) est maintenant disponible dans ton solde WinPlus.",
                        $"Your share of the session on {booking.SessionDate:dd/MM/yyyy} ({booking.PriceXaf:0} XAF paid, commission deducted) is now available in your WinPlus balance."),
                    userId: tutorUserId.Value, type: "TutorBooking");
            await ntfy.PublishLocalizedAsync(db, $"winplus-user-{booking.StudentUserId}", new("Comment s'est passée ta séance ?", "How did your session go?"),
                new("Laisse un avis pour aider les autres élèves.", "Leave a review to help other students."), userId: booking.StudentUserId, type: "TutorBooking");
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
