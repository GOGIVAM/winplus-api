using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Sessions d'enseignement en ligne (Module 5 — live/enregistrement/correction).
/// Annulation et remboursement suivent le même schéma honnête que
/// TutorBookingService (Module 6) : pas d'intégration de virement automatique
/// réel dans ce projet, le remboursement est "simulé" (statut + notification)
/// et le geste bancaire reste manuel côté admin.
/// </summary>
public class TeachingSessionService : ITeachingSessionService
{
    private readonly ApplicationDbContext _context;
    private readonly INotchPayService _notchPay;
    private readonly INtfyService _ntfy;
    private readonly IEmailService _email;
    private readonly ILogger<TeachingSessionService> _logger;

    public TeachingSessionService(ApplicationDbContext context, INotchPayService notchPay, INtfyService ntfy, IEmailService email, ILogger<TeachingSessionService> logger)
    {
        _context = context;
        _notchPay = notchPay;
        _ntfy = ntfy;
        _email = email;
        _logger = logger;
    }

    public async Task<TeachingSessionDto> CreateAsync(int teacherId, CreateSessionRequestDto request)
    {
        if (request.Title.Trim().Length < 3) throw new InvalidOperationException("Le titre doit contenir au moins 3 caractères.");
        if (request.Type is not ("live" or "recording" or "correction")) throw new InvalidOperationException("Type de session invalide.");
        var duration = Math.Clamp((request.DurationMinutes / 15) * 15, 30, 180);
        if (!request.IsFree && (request.PriceXaf is null || request.PriceXaf <= 0))
            throw new InvalidOperationException("Indique un prix pour une session payante.");
        if (request.MaxParticipants is < 1 or > 100) request.MaxParticipants = null;

        var session = new Session
        {
            Title = request.Title.Trim(),
            Description = request.Description,
            Type = request.Type,
            Subject = request.Subject,
            Level = request.Level,
            StartDate = request.StartDate,
            EndDate = request.StartDate.AddMinutes(duration),
            DurationMinutes = duration,
            MaxParticipants = request.MaxParticipants,
            IsFree = request.IsFree,
            PriceXaf = request.IsFree ? null : request.PriceXaf,
            ExternalLink = request.ExternalLink,
            CreatedBy = teacherId,
            Status = "scheduled",
        };
        _context.Sessions.Add(session);
        await _context.SaveChangesAsync();

        return await MapToDtoAsync(session);
    }

    public async Task<List<TeachingSessionDto>> GetTeacherSessionsAsync(int teacherId, DateTime? from = null, DateTime? to = null)
    {
        var query = _context.Sessions.AsNoTracking().Where(s => s.CreatedBy == teacherId && !s.IsDeleted);
        if (from.HasValue) query = query.Where(s => s.EndDate >= from.Value);
        if (to.HasValue) query = query.Where(s => s.StartDate <= to.Value);

        var sessions = await query.OrderBy(s => s.StartDate).ToListAsync();
        var result = new List<TeachingSessionDto>();
        foreach (var s in sessions) result.Add(await MapToDtoAsync(s));
        return result;
    }

    public async Task<TeachingSessionDto?> GetByIdAsync(int id, int? viewerUserId = null)
    {
        var session = await _context.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);
        return session == null ? null : await MapToDtoAsync(session);
    }

    public async Task<TeachingSessionDto> CancelAsync(int teacherId, int sessionId, CancelSessionRequestDto request)
    {
        var session = await _context.Sessions.Include(s => s.Enrollments).ThenInclude(e => e.Student)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.CreatedBy == teacherId && !s.IsDeleted)
            ?? throw new KeyNotFoundException("Session introuvable ou non autorisée.");

        if (session.Status == "cancelled") throw new InvalidOperationException("Cette session est déjà annulée.");

        session.Status = "cancelled";
        session.CancelledAt = DateTime.UtcNow;
        session.CancelledByUserId = teacherId;
        session.CancellationReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason;
        await _context.SaveChangesAsync();

        // Notification (push + email) à tous les inscrits, remboursement pour
        // les inscriptions payées — US-SES-03.
        foreach (var enrollment in session.Enrollments)
        {
            _ = _ntfy.PublishAsync($"winplus-user-{enrollment.StudentId}", "Session annulée",
                $"La session « {session.Title} » du {session.StartDate:dd/MM/yyyy HH:mm} a été annulée par le professeur.",
                userId: enrollment.StudentId, type: "SessionCancelled");

            if (enrollment.Student?.Email is { Length: > 0 } email)
            {
                _ = _email.SendGenericEmailAsync(email, "Session annulée — WinPlus",
                    $"<p>Bonjour,</p><p>La session <strong>{session.Title}</strong> prévue le " +
                    $"{session.StartDate:dd/MM/yyyy à HH:mm} a été annulée par le professeur." +
                    (enrollment.PaymentStatus == "paid" ? " Le remboursement de ta place est en cours." : "") +
                    "</p>");
            }

            if (enrollment.PaymentStatus == "paid")
            {
                enrollment.PaymentStatus = "refunded";
                await _ntfy.PublishAdminAsync("Remboursement manuel requis — session annulée",
                    $"Session #{session.Id} annulée : rembourser l'élève #{enrollment.StudentId} " +
                    $"({enrollment.PriceChargedXaf} XAF, réf. {enrollment.NotchpayReference}) via NotchPay/MoMo.",
                    tags: new[] { "moneybag" });
            }
        }
        await _context.SaveChangesAsync();

        _logger.LogInformation("Session {SessionId} annulée par {TeacherId}, {Count} inscrit(s) notifié(s)", sessionId, teacherId, session.Enrollments.Count);
        return await MapToDtoAsync(session);
    }

    public async Task<TeachingSessionDto> EnrollAsync(int studentId, int sessionId, EnrollSessionRequestDto request)
    {
        var session = await _context.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId && !s.IsDeleted)
            ?? throw new KeyNotFoundException("Session introuvable.");
        if (session.Status != "scheduled") throw new InvalidOperationException("Cette session n'accepte plus d'inscriptions.");

        var already = await _context.SessionEnrollments.AnyAsync(e => e.SessionId == sessionId && e.StudentId == studentId);
        if (already) throw new InvalidOperationException("Tu es déjà inscrit à cette session.");

        if (session.MaxParticipants.HasValue)
        {
            var count = await _context.SessionEnrollments.CountAsync(e => e.SessionId == sessionId);
            if (count >= session.MaxParticipants.Value) throw new InvalidOperationException("Cette session est complète.");
        }

        var enrollment = new SessionEnrollment { SessionId = sessionId, StudentId = studentId };

        if (session.IsFree || session.PriceXaf is not > 0)
        {
            enrollment.PaymentStatus = "free";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Phone))
                throw new InvalidOperationException("Numéro Mobile Money requis pour une session payante.");

            var student = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == studentId);
            var email = student?.Email ?? $"user{studentId}@winplus.cm";
            var name = $"{student?.FirstName} {student?.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(name)) name = email.Split('@')[0];
            var e164Phone = NormalizePhoneToE164(request.Phone);
            var channel = DetectChannelFromPhone(e164Phone);

            try
            {
                var result = await _notchPay.InitiatePaymentAsync(
                    e164Phone, session.PriceXaf!.Value, sessionId,
                    $"WinPlus — {session.Title}", email, name, channel, "SESS");
                enrollment.NotchpayReference = result.Transaction?.Reference;
                enrollment.PaymentStatus = "pending";
                enrollment.PriceChargedXaf = session.PriceXaf;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec initiation paiement pour l'inscription session {SessionId}", sessionId);
                throw new InvalidOperationException("Impossible d'initier le paiement pour cette session.");
            }
        }

        _context.SessionEnrollments.Add(enrollment);
        await _context.SaveChangesAsync();
        return await MapToDtoAsync(session);
    }

    public async Task SetSummaryAsync(int teacherId, int sessionId, string summaryText)
    {
        var session = await _context.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.CreatedBy == teacherId)
            ?? throw new KeyNotFoundException("Session introuvable ou non autorisée.");
        session.SummaryText = summaryText;
        await _context.SaveChangesAsync();
    }

    private static string NormalizePhoneToE164(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 9) digits = "237" + digits;
        return digits;
    }

    private static string DetectChannelFromPhone(string phone)
    {
        var s = phone.TrimStart('+');
        if (s.StartsWith("237") && s.Length > 9) s = s[3..];
        if (s.Length < 3) return "cm.mtn";

        var pfx = s[..3];
        string[] orangePrefixes = ["655", "656", "657", "658", "659", "690", "691", "692", "693", "694", "695", "696", "697", "698", "699"];
        return Array.IndexOf(orangePrefixes, pfx) >= 0 ? "cm.orange" : "cm.mtn";
    }

    private async Task<TeachingSessionDto> MapToDtoAsync(Session s)
    {
        var enrollments = await _context.SessionEnrollments.AsNoTracking().Where(e => e.SessionId == s.Id).ToListAsync();
        return new TeachingSessionDto
        {
            Id = s.Id,
            Title = s.Title,
            Description = s.Description,
            Type = s.Type,
            Subject = s.Subject,
            Level = s.Level,
            StartDate = s.StartDate,
            EndDate = s.EndDate,
            DurationMinutes = s.DurationMinutes,
            MaxParticipants = s.MaxParticipants,
            IsFree = s.IsFree,
            PriceXaf = s.PriceXaf,
            ExternalLink = s.ExternalLink,
            Status = s.Status,
            CancellationReason = s.CancellationReason,
            EnrolledCount = enrollments.Count,
            RevenueXaf = enrollments.Where(e => e.PaymentStatus == "paid").Sum(e => e.PriceChargedXaf ?? 0),
            TranscriptText = s.TranscriptText,
            SummaryText = s.SummaryText,
            CreatedAt = s.CreatedAt,
        };
    }
}
