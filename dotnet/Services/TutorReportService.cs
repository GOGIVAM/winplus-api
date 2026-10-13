using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public interface ITutorReportService
{
    List<TutorReportReasonDto> GetReasons();
    Task<TutorReportCreatedDto> SubmitAsync(int reporterUserId, CreateTutorReportDto request);

    // ── Admin ──
    Task<List<AdminTutorReportDto>> GetReportsAsync(string? status, int? tutorProfileId);
    Task<AdminTutorReportDto> MarkReviewedAsync(int adminUserId, int reportId, string? note);
    Task<List<AdminSuspendedTutorDto>> GetSuspendedAsync();
    Task<AdminTutorSafetyFileDto> GetSafetyFileAsync(int tutorProfileId);
    Task<AdminTutorSafetyFileDto> LiftSuspensionAsync(int adminUserId, int tutorProfileId, string? reason);
}

/// <summary>
/// Droits de noter et de signaler une séance (lot 9.5, suivi 17.12 et 17.16),
/// partagés par les avis, les signalements et l'affichage des réservations.
/// </summary>
public static class TutorFeedbackRules
{
    /// <summary>Noter : séance effectuée (y compris contestée après coup).</summary>
    public static readonly string[] ReviewableStatuses = { "completed", "disputed" };
    /// <summary>Signaler : séance effectuée, annulée ou en cours (confirmée).</summary>
    public static readonly string[] ReportableStatuses = { "confirmed", "completed", "disputed", "cancelled" };

    public static bool IsParticipant(TutorBooking b, int userId) =>
        b.StudentUserId == userId || b.Participants.Any(p => p.StudentUserId == userId);

    /// <summary>Payeur : portefeuille payeur, compte qui a réservé, ou compte de l'institution payeuse.</summary>
    public static bool IsPayer(TutorBooking b, int userId, int? viewerInstitutionId) =>
        (b.PayerUserId ?? b.StudentUserId) == userId
        || b.BookedByUserId == userId
        || (viewerInstitutionId != null && b.PayerInstitutionId == viewerInstitutionId);

    /// <summary>participant | payer | null (aucun droit). Le tuteur de la séance n'a jamais ce droit.</summary>
    public static string? RoleOf(TutorBooking b, int userId, int? viewerInstitutionId)
    {
        if (b.TutorProfile?.UserId == userId) return null;
        if (IsParticipant(b, userId)) return "participant";
        if (IsPayer(b, userId, viewerInstitutionId)) return "payer";
        return null;
    }
}

/// <summary>
/// Signalements de tuteurs et suspension automatique du présentiel (lot 9.5,
/// suivi 17.12) : chaque signalement crée une alerte admin persistée et
/// notifiée (INtfyService.PublishAdminAsync) ; au-delà de
/// TutorSafety:ReportThreshold signalements (3) en TutorSafety:ReportWindowDays
/// jours (30), le présentiel est suspendu jusqu'à l'examen d'un admin. Les
/// séances en ligne restent possibles ; les séances présentielles futures déjà
/// réservées (confirmées) ne sont PAS annulées, elles sont listées à l'admin ;
/// les demandes présentielles encore en attente du tuteur sont refusées et
/// remboursées intégralement (17.17, TutorInPersonRequestCloser). Seuls les
/// signalements postérieurs à la dernière levée comptent pour le seuil.
/// </summary>
public class TutorReportService : ITutorReportService
{
    private readonly ApplicationDbContext _db;
    private readonly INtfyService _ntfy;
    private readonly ITutorVerificationService _verification;
    private readonly ILogger<TutorReportService> _logger;
    private readonly TutorVerificationSettings _settings;
    private readonly ITutorRefundService _refunds;

    public TutorReportService(ApplicationDbContext db, INtfyService ntfy, ITutorVerificationService verification,
        ILogger<TutorReportService> logger, IConfiguration configuration, ITutorRefundService refunds)
    {
        _db = db;
        _refunds = refunds;
        _ntfy = ntfy;
        _verification = verification;
        _logger = logger;
        _settings = TutorVerificationSettings.From(configuration);
    }

    public List<TutorReportReasonDto> GetReasons() =>
        TutorReportReasons.Labels.Select(kv => new TutorReportReasonDto { Code = kv.Key, Label = kv.Value }).ToList();

    private async Task<int?> ViewerInstitutionAsync(int userId) =>
        await _db.Users.AsNoTracking().Where(u => u.Id == userId && u.Role == "institution").Select(u => u.InstitutionId).FirstOrDefaultAsync();

    private static string? DisplayName(User? u) =>
        u == null ? null : ($"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.Email.Split('@')[0]);

    public async Task<TutorReportCreatedDto> SubmitAsync(int reporterUserId, CreateTutorReportDto request)
    {
        var reason = (request.Reason ?? "").Trim();
        if (!TutorReportReasons.Labels.ContainsKey(reason))
            throw new InvalidOperationException("Choisis un motif dans la liste.");
        var description = (request.Description ?? "").Trim();
        if (reason == TutorReportReasons.Other && description.Length < 10)
            throw new InvalidOperationException("Décris ce qui s'est passé (10 caractères au moins).");
        if (description.Length > 1000) description = description[..1000];

        var booking = await _db.TutorBookings
            .Include(b => b.Participants)
            .Include(b => b.TutorProfile)
            .FirstOrDefaultAsync(b => b.Id == request.TutorBookingId)
            ?? throw new InvalidOperationException("Réservation introuvable.");
        var role = TutorFeedbackRules.RoleOf(booking, reporterUserId, await ViewerInstitutionAsync(reporterUserId))
            ?? throw new UnauthorizedAccessException("Seuls les participants et le payeur de cette séance peuvent la signaler.");
        if (!TutorFeedbackRules.ReportableStatuses.Contains(booking.Status))
            throw new InvalidOperationException("Cette séance ne peut pas être signalée (elle n'a pas encore été acceptée par le répétiteur).");
        if (await _db.TutorReports.AnyAsync(r => r.TutorBookingId == booking.Id && r.ReporterUserId == reporterUserId))
            throw new InvalidOperationException("Tu as déjà signalé cette séance.");

        var report = new TutorReport
        {
            TutorProfileId = booking.TutorProfileId,
            TutorBookingId = booking.Id,
            ReporterUserId = reporterUserId,
            ReporterRole = role,
            Reason = reason,
            Description = description.Length == 0 ? null : description,
            BookingStatus = booking.Status,
        };
        _db.TutorReports.Add(report);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Double envoi : l'index unique séance + personne fait foi.
            throw new InvalidOperationException("Tu as déjà signalé cette séance.");
        }
        _db.TutorVerificationEvents.Add(new TutorVerificationEvent
        {
            TutorProfileId = booking.TutorProfileId,
            Category = "report",
            Action = "reported",
            ActorUserId = reporterUserId,
            ActorRole = role,
            Reason = TutorReportReasons.Labels[reason],
            RelatedId = report.Id,
        });
        await _db.SaveChangesAsync();

        await _ntfy.PublishAdminAsync("Signalement d'un répétiteur",
            $"Séance #{booking.Id} du {booking.SessionDate:dd/MM/yyyy} ({booking.Mode}, {booking.Status}) signalée par un {(role == "payer" ? "payeur" : "participant")} : {TutorReportReasons.Labels[reason]}."
            + (report.Description != null ? $" « {Shorten(report.Description, 200)} »" : ""),
            priority: reason is TutorReportReasons.SafetyConcern or TutorReportReasons.Harassment ? "urgent" : "high",
            tags: new[] { "warning" }, relatedEntityType: "TutorReport", relatedEntityId: report.Id);

        await ApplyThresholdAsync(booking.TutorProfileId);

        return new TutorReportCreatedDto
        {
            Id = report.Id,
            Message = "Merci : ton signalement a été transmis à l'équipe WinPlus, qui va l'examiner.",
        };
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    /// <summary>Signalements qui comptent pour le seuil : fenêtre glissante, postérieurs à la dernière levée.</summary>
    private async Task<int> ReportsInWindowAsync(TutorProfile profile, DateTime nowUtc)
    {
        var since = nowUtc - _settings.ReportWindow;
        if (profile.InPersonSuspensionLiftedAt is DateTime lifted && lifted > since) since = lifted;
        return await _db.TutorReports.CountAsync(r => r.TutorProfileId == profile.Id && r.CreatedAt > since);
    }

    private async Task ApplyThresholdAsync(int tutorProfileId)
    {
        var profile = await _db.TutorProfiles.AsNoTracking().FirstAsync(p => p.Id == tutorProfileId);
        if (profile.InPersonSuspendedAt != null) return;
        var now = DateTime.UtcNow;
        var count = await ReportsInWindowAsync(profile, now);
        if (count <= _settings.ReportThreshold) return;

        var reason = $"{count} signalements en {_settings.ReportWindowDays} jours (seuil : {_settings.ReportThreshold}).";
        // Mise à jour conditionnelle : deux signalements simultanés ne suspendent qu'une fois.
        var updated = await _db.TutorProfiles
            .Where(p => p.Id == tutorProfileId && p.InPersonSuspendedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.InPersonSuspendedAt, now)
                .SetProperty(p => p.InPersonSuspensionReason, reason)
                .SetProperty(p => p.UpdatedAt, now));
        if (updated == 0) return;

        _db.TutorVerificationEvents.Add(new TutorVerificationEvent
        {
            TutorProfileId = tutorProfileId,
            Category = "suspension",
            Action = "suspended",
            ActorRole = "system",
            Reason = reason,
        });
        await _db.SaveChangesAsync();

        // 17.17 : les demandes présentielles encore en attente du tuteur ne
        // peuvent plus être acceptées : refusées d'office, remboursées
        // intégralement, élèves participants et payeur prévenus.
        var refused = 0;
        try
        {
            refused = await new TutorInPersonRequestCloser(_db, _refunds, _ntfy, _logger).CloseAllPendingAsync(tutorProfileId);
        }
        catch (Exception ex)
        {
            // La garde à l'acceptation reste en place : le tuteur ne pourra pas accepter.
            _logger.LogError(ex, "Refus des demandes présentielles en attente non terminé (profil répétiteur {ProfileId})", tutorProfileId);
        }

        var upcoming = await UpcomingInPersonAsync(tutorProfileId);
        await _ntfy.PublishAdminAsync("Présentiel suspendu automatiquement",
            $"Profil répétiteur #{tutorProfileId} : {reason} Les séances en ligne restent possibles. "
            + (refused > 0 ? $"{refused} demande(s) présentielle(s) en attente refusée(s) d'office et remboursée(s) intégralement. " : "")
            + (upcoming.Count == 0
                ? "Aucune séance présentielle à venir."
                : $"{upcoming.Count} séance(s) présentielle(s) à venir déjà réservée(s), NON annulée(s) : "
                  + string.Join(", ", upcoming.Take(10).Select(b => $"#{b.BookingId} le {b.SessionDate:dd/MM}")) + (upcoming.Count > 10 ? "…" : "") + ". À examiner."),
            priority: "urgent", tags: new[] { "rotating_light" }, relatedEntityType: "TutorProfile", relatedEntityId: tutorProfileId);
        await _ntfy.PublishAsync($"winplus-user-{profile.UserId}", "Présentiel suspendu",
            "Suite à plusieurs signalements, tes séances présentielles sont suspendues le temps qu'un membre de l'équipe WinPlus examine la situation. Tes séances en ligne restent possibles."
            + (refused > 0 ? $" Tes {refused} demande(s) présentielle(s) en attente ont été refusées et les payeurs remboursés intégralement, sans frais pour toi." : ""),
            userId: profile.UserId, type: "TutorVerification", relatedEntityType: "TutorProfile", relatedEntityId: tutorProfileId);
        _logger.LogWarning("Présentiel suspendu pour le profil répétiteur {ProfileId} : {Reason}", tutorProfileId, reason);
    }

    private async Task<List<AdminInPersonBookingDto>> UpcomingInPersonAsync(int tutorProfileId)
    {
        var rows = await TutorVerificationService.UpcomingInPersonQuery(_db)
            .Where(b => b.TutorProfileId == tutorProfileId)
            .Include(b => b.TutorProfile).ThenInclude(p => p!.User).Include(b => b.Payer).Include(b => b.Student)
            .OrderBy(b => b.SessionDate).ThenBy(b => b.StartTime)
            .ToListAsync();
        return rows.Select(TutorVerificationService.InPersonBookingDto).ToList();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Admin
    // ═════════════════════════════════════════════════════════════════════

    private static AdminTutorSummaryDto TutorSummary(TutorProfile p) => new()
    {
        TutorProfileId = p.Id,
        TutorUserId = p.UserId,
        Name = DisplayName(p.User),
        Email = p.User?.Email,
        AvatarUrl = p.User?.AvatarUrl,
        IsInPersonVerified = TutorInPersonPolicy.IsVerified(p),
        IsSuspended = TutorInPersonPolicy.IsSuspended(p),
        Routes = string.IsNullOrEmpty(p.InPersonVerificationRoutes) ? new List<string>() : p.InPersonVerificationRoutes.Split(',').ToList(),
    };

    private IQueryable<TutorReport> ReportsWithDetails() => _db.TutorReports.AsNoTracking()
        .Include(r => r.TutorProfile).ThenInclude(p => p!.User)
        .Include(r => r.TutorBooking)
        .Include(r => r.Reporter);

    private async Task<List<AdminTutorReportDto>> MapAsync(List<TutorReport> rows)
    {
        var reviewerIds = rows.Where(r => r.ReviewedByUserId != null).Select(r => r.ReviewedByUserId!.Value).Distinct().ToList();
        var reviewers = reviewerIds.Count == 0 ? new Dictionary<int, string?>()
            : (await _db.Users.AsNoTracking().IgnoreQueryFilters().Where(u => reviewerIds.Contains(u.Id)).ToListAsync())
                .ToDictionary(u => u.Id, u => DisplayName(u));
        return rows.Select(r => new AdminTutorReportDto
        {
            Id = r.Id,
            Tutor = TutorSummary(r.TutorProfile!),
            TutorBookingId = r.TutorBookingId,
            SessionDate = r.TutorBooking?.SessionDate ?? default,
            SessionMode = r.TutorBooking?.Mode ?? "",
            BookingStatus = r.BookingStatus,
            ReporterUserId = r.ReporterUserId,
            ReporterName = DisplayName(r.Reporter),
            ReporterRole = r.ReporterRole,
            Reason = r.Reason,
            ReasonLabel = TutorReportReasons.Labels.GetValueOrDefault(r.Reason, r.Reason),
            Description = r.Description,
            Status = r.Status,
            CreatedAt = r.CreatedAt,
            ReviewedAt = r.ReviewedAt,
            ReviewedByName = r.ReviewedByUserId is int id ? reviewers.GetValueOrDefault(id) : null,
            AdminNote = r.AdminNote,
        }).ToList();
    }

    public async Task<List<AdminTutorReportDto>> GetReportsAsync(string? status, int? tutorProfileId)
    {
        var query = ReportsWithDetails();
        status = string.IsNullOrWhiteSpace(status) ? "open" : status;
        if (status != "all") query = query.Where(r => r.Status == status);
        if (tutorProfileId != null) query = query.Where(r => r.TutorProfileId == tutorProfileId);
        return await MapAsync(await query.OrderByDescending(r => r.CreatedAt).Take(300).ToListAsync());
    }

    public async Task<AdminTutorReportDto> MarkReviewedAsync(int adminUserId, int reportId, string? note)
    {
        var report = await _db.TutorReports.FirstOrDefaultAsync(r => r.Id == reportId)
            ?? throw new InvalidOperationException("Signalement introuvable.");
        if (report.Status == "reviewed")
            throw new InvalidOperationException("Ce signalement a déjà été examiné.");
        report.Status = "reviewed";
        report.ReviewedAt = DateTime.UtcNow;
        report.ReviewedByUserId = adminUserId;
        report.AdminNote = string.IsNullOrWhiteSpace(note) ? null : (note.Trim().Length > 500 ? note.Trim()[..500] : note.Trim());
        await _db.SaveChangesAsync();
        return (await MapAsync(await ReportsWithDetails().Where(r => r.Id == reportId).ToListAsync()))[0];
    }

    public async Task<List<AdminSuspendedTutorDto>> GetSuspendedAsync()
    {
        var profiles = await _db.TutorProfiles.AsNoTracking().Include(p => p.User)
            .Where(p => p.InPersonSuspendedAt != null)
            .OrderBy(p => p.InPersonSuspendedAt).ToListAsync();
        var now = DateTime.UtcNow;
        var result = new List<AdminSuspendedTutorDto>();
        foreach (var p in profiles)
        {
            result.Add(new AdminSuspendedTutorDto
            {
                Tutor = TutorSummary(p),
                SuspendedAt = p.InPersonSuspendedAt!.Value,
                Reason = p.InPersonSuspensionReason,
                ReportsInWindow = await ReportsInWindowAsync(p, now),
                OpenReports = await _db.TutorReports.CountAsync(r => r.TutorProfileId == p.Id && r.Status == "open"),
                UpcomingInPersonBookings = await UpcomingInPersonAsync(p.Id),
            });
        }
        return result;
    }

    public async Task<AdminTutorSafetyFileDto> GetSafetyFileAsync(int tutorProfileId)
    {
        var p = await _db.TutorProfiles.AsNoTracking().Include(x => x.User).FirstOrDefaultAsync(x => x.Id == tutorProfileId)
            ?? throw new InvalidOperationException("Profil répétiteur introuvable.");
        var endorsements = await _db.TutorInstitutionEndorsements.AsNoTracking()
            .Include(e => e.Institution).Include(e => e.TutorProfile).ThenInclude(x => x!.User)
            .Where(e => e.TutorProfileId == tutorProfileId).OrderByDescending(e => e.CreatedAt).ToListAsync();
        var valid = await TutorVerificationService.ValidEndorsements(_db).Where(e => e.TutorProfileId == tutorProfileId).Select(e => e.Id).ToListAsync();
        return new AdminTutorSafetyFileDto
        {
            Tutor = TutorSummary(p),
            SuspendedAt = p.InPersonSuspendedAt,
            SuspensionReason = p.InPersonSuspensionReason,
            SuspensionLiftedAt = p.InPersonSuspensionLiftedAt,
            Reports = await GetReportsAsync("all", tutorProfileId),
            History = await _verification.GetHistoryAsync(tutorProfileId, forTutor: false),
            Endorsements = endorsements.Select(e => new TutorEndorsementDto
            {
                Id = e.Id,
                TutorProfileId = e.TutorProfileId,
                TutorUserId = p.UserId,
                TutorName = DisplayName(p.User),
                InstitutionId = e.InstitutionId,
                InstitutionName = e.Institution?.Name,
                Statement = e.Statement,
                VerifierName = e.VerifierName,
                VerifiedOn = e.VerifiedOn,
                Status = e.Status,
                IsValid = valid.Contains(e.Id),
                CreatedAt = e.CreatedAt,
                RevokedAt = e.RevokedAt,
                RevocationReason = e.RevocationReason,
            }).ToList(),
            UpcomingInPersonBookings = await UpcomingInPersonAsync(tutorProfileId),
        };
    }

    public async Task<AdminTutorSafetyFileDto> LiftSuspensionAsync(int adminUserId, int tutorProfileId, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new InvalidOperationException("Indique le motif de la levée (examen réalisé, conclusions).");
        var profile = await _db.TutorProfiles.FirstOrDefaultAsync(p => p.Id == tutorProfileId)
            ?? throw new InvalidOperationException("Profil répétiteur introuvable.");
        if (profile.InPersonSuspendedAt == null)
            throw new InvalidOperationException("Le présentiel de ce répétiteur n'est pas suspendu.");

        var now = DateTime.UtcNow;
        var motive = reason.Trim().Length > 500 ? reason.Trim()[..500] : reason.Trim();
        profile.InPersonSuspendedAt = null;
        profile.InPersonSuspensionReason = null;
        profile.InPersonSuspensionLiftedAt = now;
        profile.UpdatedAt = now;
        // Examen fait : les signalements encore ouverts de ce tuteur sont clos avec le même motif.
        var open = await _db.TutorReports.Where(r => r.TutorProfileId == tutorProfileId && r.Status == "open").ToListAsync();
        foreach (var r in open)
        {
            r.Status = "reviewed";
            r.ReviewedAt = now;
            r.ReviewedByUserId = adminUserId;
            r.AdminNote ??= $"Examiné lors de la levée de la suspension : {motive}";
            if (r.AdminNote.Length > 500) r.AdminNote = r.AdminNote[..500];
        }
        _db.TutorVerificationEvents.Add(new TutorVerificationEvent
        {
            TutorProfileId = tutorProfileId,
            Category = "suspension",
            Action = "lifted",
            ActorUserId = adminUserId,
            ActorRole = "admin",
            Reason = motive,
        });
        await _db.SaveChangesAsync();

        await _ntfy.PublishAsync($"winplus-user-{profile.UserId}", "Présentiel rétabli",
            TutorInPersonPolicy.IsVerified(profile)
                ? "L'équipe WinPlus a examiné les signalements : tes séances présentielles sont de nouveau possibles."
                : "L'équipe WinPlus a levé la suspension. Fais vérifier ton profil pour proposer des séances présentielles.",
            userId: profile.UserId, type: "TutorVerification", relatedEntityType: "TutorProfile", relatedEntityId: tutorProfileId);
        return await GetSafetyFileAsync(tutorProfileId);
    }
}
