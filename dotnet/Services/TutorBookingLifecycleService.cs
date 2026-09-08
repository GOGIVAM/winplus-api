using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Fait avancer automatiquement le cycle de vie des réservations de cours
/// particulier (Module 6, US-REP-05/06) : expire les demandes non traitées
/// dans le délai de préavis du répétiteur, et libère l'escrow simulé 2h après
/// qu'une séance a été marquée "Effectuée" sans contestation. Même pattern
/// que <see cref="PaymentExpirationService"/>, avec un intervalle plus court
/// car les demandes de réservation sont sensibles au délai.
/// </summary>
public class TutorBookingLifecycleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TutorBookingLifecycleService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DisputeWindow = TimeSpan.FromHours(2);
    private static readonly string[] RespondedStatuses = { "pending_tutor_approval" };

    public TutorBookingLifecycleService(IServiceScopeFactory scopeFactory, ILogger<TutorBookingLifecycleService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TutorBookingLifecycleService démarré (intervalle: 15 minutes)");

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

        var pending = await db.TutorBookings
            .Include(b => b.TutorProfile)
            .Where(b => RespondedStatuses.Contains(b.Status))
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var expired = pending.Where(b => now >= GetResponseDeadline(b)).ToList();
        if (expired.Count == 0) return;

        foreach (var booking in expired)
        {
            booking.Status = "expired";
            booking.PaymentStatus = "refunded";
            booking.UpdatedAt = now;

            await ntfy.PublishAsync($"winplus-user-{booking.StudentUserId}", "Demande expirée — remboursement en cours",
                $"Le répétiteur n'a pas répondu à temps pour ta séance du {booking.SessionDate:dd/MM/yyyy}. Tu seras remboursé.",
                userId: booking.StudentUserId, type: "TutorBooking");
            var tutorUserId = booking.TutorProfile?.UserId;
            if (tutorUserId.HasValue)
                await ntfy.PublishAsync($"winplus-user-{tutorUserId.Value}", "Demande expirée",
                    $"Tu n'as pas répondu à temps à la demande du {booking.SessionDate:dd/MM/yyyy} — elle a été annulée.",
                    userId: tutorUserId.Value, type: "TutorBooking");
            await ntfy.PublishAdminAsync("Remboursement manuel requis — demande expirée",
                $"Réservation #{booking.Id} ({booking.PriceXaf} XAF, réf. {booking.NotchpayReference}) : rembourser l'élève #{booking.StudentUserId} via NotchPay/MoMo.",
                tags: new[] { "moneybag" });
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("{Count} demande(s) de réservation expirée(s)", expired.Count);
    }

    private async Task ReleaseEscrowAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();

        var now = DateTime.UtcNow;
        var releaseThreshold = now.Subtract(DisputeWindow);
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
                    $"{booking.PriceXaf} XAF pour la séance du {booking.SessionDate:dd/MM/yyyy} sont maintenant disponibles dans ton solde WinPlus.",
                    userId: tutorUserId.Value, type: "TutorBooking");
            await ntfy.PublishAsync($"winplus-user-{booking.StudentUserId}", "Comment s'est passée ta séance ?",
                "Laisse un avis pour aider les autres élèves.", userId: booking.StudentUserId, type: "TutorBooking");
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("{Count} réservation(s) libérée(s) de l'escrow", toRelease.Count);
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
