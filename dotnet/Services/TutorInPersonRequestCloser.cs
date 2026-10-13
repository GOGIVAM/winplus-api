using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Lot 9.5, arbitrage 17.17 : une demande PRÉSENTIELLE en attente du tuteur
/// (<c>pending_tutor_approval</c>, séance unique ou engagement mensuel) ne
/// peut plus être acceptée quand le présentiel du tuteur est suspendu
/// (seuil de signalements). Elle est refusée d'office et remboursée
/// intégralement au portefeuille du payeur (cause
/// <see cref="TutorRefundCause.InPersonBlocked"/> : frais Mobile Money
/// absorbés par la plateforme), et les élèves participants, le payeur et le
/// compte qui a réservé sont prévenus par notification ; le tuteur aussi.
///
/// Appelé à la suspension (TutorReportService), en garde à l'acceptation
/// (TutorBookingService, TutorEngagementService) et à l'encaissement tardif
/// d'un paiement Mobile Money lancé avant la suspension. Idempotent : une
/// demande qui n'est plus en attente n'est pas touchée, et un remboursement
/// déjà figé n'est jamais recalculé (TutorRefundService).
/// </summary>
public sealed class TutorInPersonRequestCloser
{
    private readonly ApplicationDbContext _db;
    private readonly ITutorRefundService _refunds;
    private readonly INtfyService _ntfy;
    private readonly ILogger _logger;

    public TutorInPersonRequestCloser(ApplicationDbContext db, ITutorRefundService refunds, INtfyService ntfy, ILogger logger)
    {
        _db = db;
        _refunds = refunds;
        _ntfy = ntfy;
        _logger = logger;
    }

    private const string Pending = "pending_tutor_approval";

    /// <summary>Motif posé sur la demande refusée d'office.</summary>
    public static string ReasonFor(TutorProfile profile) =>
        "Présentiel du répétiteur suspendu : demande refusée d'office et remboursée intégralement.";

    /// <summary>
    /// Vrai si cette demande présentielle ne peut pas être acceptée : présentiel
    /// du tuteur SUSPENDU (17.17). Un tuteur simplement non vérifié n'est pas
    /// concerné : les demandes présentielles prises avant le 9.5 ne sont pas
    /// annulées (décision 9.5), et il ne peut plus en recevoir de nouvelles.
    /// </summary>
    public static bool MustClose(TutorProfile? profile, string? mode) =>
        profile != null && TutorInPersonPolicy.IsInPerson(mode) && TutorInPersonPolicy.IsSuspended(profile);

    /// <summary>Refuse et rembourse toutes les demandes présentielles en attente du tuteur. Renvoie le nombre de demandes refusées.</summary>
    public async Task<int> CloseAllPendingAsync(int tutorProfileId)
    {
        var modes = TutorInPersonPolicy.InPersonModes;
        var bookingIds = await _db.TutorBookings.AsNoTracking()
            .Where(b => b.TutorProfileId == tutorProfileId && b.Status == Pending && b.TutorEngagementId == null && modes.Contains(b.Mode))
            .Select(b => b.Id).ToListAsync();
        var engagementIds = await _db.TutorEngagements.AsNoTracking()
            .Where(e => e.TutorProfileId == tutorProfileId && e.Status == TutorEngagementStatuses.PendingTutorApproval && modes.Contains(e.Mode))
            .Select(e => e.Id).ToListAsync();

        var closed = 0;
        foreach (var id in bookingIds)
            if (await CloseBookingAsync(id) != null) closed++;
        foreach (var id in engagementIds)
            if (await CloseEngagementAsync(id) != null) closed++;
        return closed;
    }

    /// <summary>
    /// Refuse et rembourse une séance unique présentielle en attente. Renvoie
    /// le montant crédité au payeur, ou null si la séance n'est pas (ou plus)
    /// concernée.
    /// </summary>
    public async Task<decimal?> CloseBookingAsync(int bookingId)
    {
        var b = await _db.TutorBookings
            .Include(x => x.TutorProfile)
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.Id == bookingId);
        if (b?.TutorProfile != null) await FreshAsync(b.TutorProfile);
        if (b == null || b.Status != Pending || b.TutorEngagementId != null || !MustClose(b.TutorProfile, b.Mode))
            return null;

        var now = DateTime.UtcNow;
        b.Status = "rejected";
        b.CancellationReason = ReasonFor(b.TutorProfile!);
        b.UpdatedAt = now;
        var decision = await _refunds.RefundAsync(b, TutorRefundCause.InPersonBlocked);
        var refunded = decision?.RefundXaf ?? 0m;

        var recipients = new HashSet<int>(b.Participants.Select(p => p.StudentUserId)) { b.StudentUserId, b.PayerUserId ?? b.StudentUserId };
        if (b.BookedByUserId is int bookedBy) recipients.Add(bookedBy);
        var money = refunded > 0
            ? $" {refunded:0} XAF ont été crédités sur le portefeuille WinPlus du payeur (remboursement intégral)."
            : "";
        await NotifyAsync(recipients, "Demande de séance refusée",
            $"Le présentiel de ce répétiteur n'est plus disponible pour le moment : la demande de séance du {b.SessionDate:dd/MM/yyyy} est refusée.{money} Tu peux réserver une séance en ligne ou choisir un autre répétiteur.",
            "TutorBooking", b.Id);
        if (b.TutorProfile?.UserId is int tutor)
            await NotifyAsync(new[] { tutor }, "Demande présentielle refusée d'office",
                $"Ton présentiel n'est pas disponible : la demande du {b.SessionDate:dd/MM/yyyy} a été refusée et le payeur remboursé intégralement, sans frais pour toi.",
                "TutorBooking", b.Id);
        return refunded;
    }

    /// <summary>Refuse et rembourse un engagement mensuel présentiel en attente, séance par séance.</summary>
    public async Task<decimal?> CloseEngagementAsync(int engagementId)
    {
        var e = await _db.TutorEngagements
            .Include(x => x.TutorProfile)
            .Include(x => x.Sessions).ThenInclude(s => s.Participants)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == engagementId);
        if (e?.TutorProfile != null) await FreshAsync(e.TutorProfile);
        if (e == null || e.Status != TutorEngagementStatuses.PendingTutorApproval || !MustClose(e.TutorProfile, e.Mode))
            return null;

        var now = DateTime.UtcNow;
        var reason = ReasonFor(e.TutorProfile!);
        e.Status = TutorEngagementStatuses.Rejected;
        e.RespondedAt ??= now;
        e.AutoRenew = false;
        e.CancellationReason = reason;
        e.UpdatedAt = now;
        await _db.SaveChangesAsync();

        var total = 0m;
        foreach (var s in e.Sessions.Where(s => s.Status is Pending or "pending_payment").OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime))
        {
            s.Status = "rejected";
            s.CancellationReason = reason;
            s.UpdatedAt = now;
            var decision = await _refunds.RefundAsync(s, TutorRefundCause.InPersonBlocked);
            total += decision?.RefundXaf ?? 0m;
        }
        await _db.SaveChangesAsync();

        var recipients = new HashSet<int>(e.Sessions.SelectMany(s => s.Participants).Select(p => p.StudentUserId))
            { e.StudentUserId, e.PayerUserId, e.BookedByUserId };
        var money = total > 0
            ? $" {total:0} XAF ont été crédités sur le portefeuille WinPlus du payeur (remboursement intégral)."
            : "";
        await NotifyAsync(recipients, "Engagement mensuel refusé",
            $"Le présentiel de ce répétiteur n'est plus disponible pour le moment : l'engagement du {e.StartDate:dd/MM/yyyy} au {e.EndDate:dd/MM/yyyy} est refusé.{money} Tu peux réserver en ligne ou choisir un autre répétiteur.",
            "TutorEngagement", e.Id);
        if (e.TutorProfile?.UserId is int tutor)
            await NotifyAsync(new[] { tutor }, "Engagement présentiel refusé d'office",
                $"Ton présentiel n'est pas disponible : la demande d'engagement du {e.StartDate:dd/MM/yyyy} a été refusée et le payeur remboursé intégralement, sans frais pour toi.",
                "TutorEngagement", e.Id);
        return total;
    }

    /// <summary>
    /// Statut du présentiel relu en base : la suspension est posée par une mise
    /// à jour directe (ExecuteUpdate) qui ne rafraîchit pas un profil déjà suivi
    /// par le contexte.
    /// </summary>
    private async Task FreshAsync(TutorProfile profile)
    {
        var entry = _db.Entry(profile);
        if (entry.State == EntityState.Unchanged) await entry.ReloadAsync();
    }

    private async Task NotifyAsync(IEnumerable<int> userIds, string title, string message, string entityType, int entityId)
    {
        foreach (var id in userIds.Distinct())
        {
            try
            {
                await _ntfy.PublishAsync($"winplus-user-{id}", title, message, priority: "high", userId: id, type: "TutorBooking",
                    relatedEntityType: entityType, relatedEntityId: entityId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notification de refus présentiel ({Type} #{Id}) non envoyée au compte {User}", entityType, entityId, id);
            }
        }
    }
}
