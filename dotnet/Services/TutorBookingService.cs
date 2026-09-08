using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Réservation de séances de cours particulier (Module 6). Ferme réellement
/// le calendrier d'un répétiteur une fois son plafond hebdomadaire atteint —
/// contrairement à <see cref="TutorProfileService.UpdateAvailabilityAsync"/>
/// qui ne plafonne que la grille de créneaux *déclarés*, ce service compte
/// les réservations *effectives* de la semaine (US-ELV-01, US-PRO-08).
/// </summary>
public class TutorBookingService : ITutorBookingService
{
    private static readonly string[] ActiveStatuses = { "pending_payment", "pending_tutor_approval", "confirmed" };
    private static readonly TimeSpan DisputeWindow = TimeSpan.FromHours(2);
    private static readonly string[] AllowedModes = { "online", "student_home", "tutor_home", "neutral_place" };
    private const string ReferencePrefix = "TBK";

    private readonly ApplicationDbContext _context;
    private readonly INotchPayService _notchPay;
    private readonly INtfyService _ntfy;
    private readonly ILogger<TutorBookingService> _logger;

    public TutorBookingService(ApplicationDbContext context, INotchPayService notchPay, INtfyService ntfy, ILogger<TutorBookingService> logger)
    {
        _context = context;
        _notchPay = notchPay;
        _ntfy = ntfy;
        _logger = logger;
    }

    public async Task<TutorBookingCreatedResponseDto> CreateBookingAsync(int studentUserId, CreateTutorBookingRequestDto request)
    {
        var mode = AllowedModes.Contains(request.Mode) ? request.Mode : "online";
        var start = TimeSpan.Parse(request.StartTime);
        var end = TimeSpan.Parse(request.EndTime);
        if (end <= start)
            throw new InvalidOperationException("L'heure de fin doit être après l'heure de début.");

        var tutorProfile = await _context.TutorProfiles
            .Include(p => p.AvailabilitySlots)
            .Include(p => p.User)
            .Include(p => p.Subjects)
            .FirstOrDefaultAsync(p => p.UserId == request.TutorUserId)
            ?? throw new InvalidOperationException("Ce répétiteur n'existe pas.");

        if (!tutorProfile.IsActive)
            throw new InvalidOperationException("Ce répétiteur n'est pas actif pour l'instant.");
        if (tutorProfile.IsOnVacation)
            throw new InvalidOperationException("Ce répétiteur a suspendu ses réservations (vacances).");

        var slotMatches = tutorProfile.AvailabilitySlots.Any(s =>
            s.IsActive && s.DayOfWeek == (int)request.SessionDate.DayOfWeek && s.StartTime <= start && s.EndTime >= end);
        if (!slotMatches)
            throw new InvalidOperationException("Ce créneau ne correspond à aucune disponibilité déclarée par le répétiteur.");

        var sessionStartUtc = request.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(start), DateTimeKind.Utc);
        if (sessionStartUtc < DateTime.UtcNow.AddHours(tutorProfile.NoticeHours))
            throw new InvalidOperationException($"Ce répétiteur demande un préavis minimum de {tutorProfile.NoticeHours} heures.");

        var conflict = await _context.TutorBookings.AnyAsync(b =>
            b.TutorProfileId == tutorProfile.Id && b.SessionDate == request.SessionDate &&
            ActiveStatuses.Contains(b.Status) && b.StartTime < end && b.EndTime > start);
        if (conflict)
            throw new InvalidOperationException("Ce créneau vient d'être réservé par un autre élève.");

        if (tutorProfile.MaxSessionsPerWeek is int max)
        {
            var weekCount = await CountActiveBookingsInWeekAsync(tutorProfile.Id, GetWeekStart(request.SessionDate));
            if (weekCount >= max)
                throw new InvalidOperationException("Ce répétiteur a atteint son nombre maximum de séances pour cette semaine. Choisis une autre semaine.");
        }

        var durationHours = (decimal)(end - start).TotalHours;
        var price = Math.Round((tutorProfile.HourlyRateXaf ?? 0) * durationHours, 0);

        var booking = new TutorBooking
        {
            TutorProfileId = tutorProfile.Id,
            StudentUserId = studentUserId,
            Subject = string.IsNullOrWhiteSpace(request.Subject) ? tutorProfile.Subjects.FirstOrDefault()?.Subject : request.Subject,
            SessionDate = request.SessionDate,
            StartTime = start,
            EndTime = end,
            Mode = mode,
            PriceXaf = price,
            Status = "pending_payment",
            PhoneNumber = request.Phone,
        };
        _context.TutorBookings.Add(booking);
        await _context.SaveChangesAsync();

        var student = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == studentUserId);
        var studentEmail = student?.Email ?? $"user{studentUserId}@winplus.cm";
        var studentName = $"{student?.FirstName} {student?.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(studentName)) studentName = studentEmail.Split('@')[0];

        try
        {
            // Le web et le mobile n'envoient pas le numéro dans le même format
            // (avec ou sans indicatif 237) — on normalise ici plutôt que
            // d'imposer une convention aux deux front-ends.
            var e164Phone = NormalizePhoneToE164(request.Phone);
            var channel = DetectChannelFromPhone(e164Phone);
            var tutorName = $"{tutorProfile.User?.FirstName} {tutorProfile.User?.LastName}".Trim();
            var result = await _notchPay.InitiatePaymentAsync(
                e164Phone, price, booking.Id,
                $"WinPlus — Cours particulier avec {tutorName}", studentEmail, studentName, channel, ReferencePrefix);

            booking.NotchpayReference = result.Transaction?.Reference;
            booking.PaymentStatus = MapNotchPayStatus(result.Transaction?.Status) ?? "pending";
            booking.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return new TutorBookingCreatedResponseDto
            {
                Booking = await MapToDtoAsync(booking),
                NotchpayAuthorizationUrl = result.AuthorizationUrl,
            };
        }
        catch (Exception ex)
        {
            booking.PaymentStatus = "failed";
            booking.Status = "cancelled";
            booking.CancellationReason = "Échec de l'initiation du paiement.";
            booking.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _logger.LogError(ex, "Échec initiation NotchPay pour réservation répétiteur {BookingId}", booking.Id);
            throw new InvalidOperationException("Impossible d'initier le paiement pour cette réservation.");
        }
    }

    public async Task<TutorBookingDto> GetByIdAsync(int userId, int bookingId)
    {
        var booking = await _context.TutorBookings
            .Include(b => b.TutorProfile).ThenInclude(p => p!.User)
            .Include(b => b.Student)
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new InvalidOperationException("Réservation introuvable.");

        if (booking.StudentUserId != userId && booking.TutorProfile?.UserId != userId)
            throw new InvalidOperationException("Tu n'as pas accès à cette réservation.");

        return MapToDto(booking);
    }

    public async Task<List<TutorBookingDto>> GetMyBookingsAsStudentAsync(int studentUserId)
    {
        var bookings = await _context.TutorBookings
            .Include(b => b.TutorProfile).ThenInclude(p => p!.User)
            .Where(b => b.StudentUserId == studentUserId)
            .OrderByDescending(b => b.SessionDate).ThenByDescending(b => b.StartTime)
            .ToListAsync();
        return bookings.Select(MapToDto).ToList();
    }

    public async Task<List<TutorBookingDto>> GetMyBookingsAsTutorAsync(int tutorUserId)
    {
        var bookings = await _context.TutorBookings
            .Include(b => b.TutorProfile).ThenInclude(p => p!.User)
            .Include(b => b.Student)
            .Where(b => b.TutorProfile!.UserId == tutorUserId)
            .OrderByDescending(b => b.SessionDate).ThenByDescending(b => b.StartTime)
            .ToListAsync();
        return bookings.Select(MapToDto).ToList();
    }

    public async Task<TutorBookingDto> CancelBookingAsync(int userId, int bookingId, string? reason)
    {
        var booking = await _context.TutorBookings
            .Include(b => b.TutorProfile).ThenInclude(p => p!.User)
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new InvalidOperationException("Réservation introuvable.");

        var isStudent = booking.StudentUserId == userId;
        var isTutor = booking.TutorProfile?.UserId == userId;
        if (!isStudent && !isTutor)
            throw new InvalidOperationException("Tu ne peux pas annuler cette réservation.");
        if (booking.Status is "cancelled" or "completed")
            throw new InvalidOperationException("Cette réservation ne peut plus être annulée.");

        booking.Status = "cancelled";
        booking.CancelledByUserId = userId;
        booking.CancelledAt = DateTime.UtcNow;
        booking.CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason;
        booking.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var notifiedUserId = isStudent ? booking.TutorProfile?.UserId : booking.StudentUserId;
        if (notifiedUserId.HasValue)
        {
            await _ntfy.PublishAsync($"winplus-user-{notifiedUserId.Value}", "Séance annulée",
                $"Une séance du {booking.SessionDate:dd/MM/yyyy} a été annulée.", userId: notifiedUserId.Value, type: "TutorBooking");
        }

        return MapToDto(booking);
    }

    public async Task<TutorBookingDto> ConfirmBookingAsync(int tutorUserId, int bookingId)
    {
        var booking = await LoadForTutorActionAsync(tutorUserId, bookingId);
        if (booking.Status != "pending_tutor_approval")
            throw new InvalidOperationException("Cette demande n'est plus en attente.");

        booking.Status = "confirmed";
        booking.ConfirmedAt = DateTime.UtcNow;
        booking.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _ntfy.PublishAsync($"winplus-user-{booking.StudentUserId}", "Séance confirmée !",
            $"Le répétiteur a accepté ta demande du {booking.SessionDate:dd/MM/yyyy}.", userId: booking.StudentUserId, type: "TutorBooking");
        return MapToDto(booking);
    }

    public async Task<TutorBookingDto> DeclineBookingAsync(int tutorUserId, int bookingId, string? reason)
    {
        var booking = await LoadForTutorActionAsync(tutorUserId, bookingId);
        if (booking.Status != "pending_tutor_approval")
            throw new InvalidOperationException("Cette demande n'est plus en attente.");

        booking.Status = "rejected";
        booking.CancellationReason = string.IsNullOrWhiteSpace(reason) ? "Refusée par le répétiteur." : reason;
        booking.UpdatedAt = DateTime.UtcNow;
        await SimulateRefundAsync(booking, "refusée par le répétiteur");
        return MapToDto(booking);
    }

    public async Task<TutorBookingDto> CompleteBookingAsync(int tutorUserId, int bookingId)
    {
        var booking = await LoadForTutorActionAsync(tutorUserId, bookingId);
        if (booking.Status != "confirmed")
            throw new InvalidOperationException("Seule une séance confirmée peut être marquée effectuée.");

        var sessionEndUtc = booking.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(booking.EndTime), DateTimeKind.Utc);
        if (DateTime.UtcNow < sessionEndUtc)
            throw new InvalidOperationException("Tu ne peux marquer cette séance effectuée qu'après son heure de fin prévue.");

        booking.Status = "completed";
        booking.CompletedAt = DateTime.UtcNow;
        booking.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _ntfy.PublishAsync($"winplus-user-{booking.StudentUserId}", "Comment s'est passée ta séance ?",
            "Laisse un avis pour ce répétiteur — tes fonds seront libérés sous 2h si tu ne contestes pas.",
            userId: booking.StudentUserId, type: "TutorBooking");
        return MapToDto(booking);
    }

    public async Task<TutorBookingDto> DisputeBookingAsync(int studentUserId, int bookingId, string reason)
    {
        var booking = await _context.TutorBookings
            .Include(b => b.TutorProfile).ThenInclude(p => p!.User)
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new InvalidOperationException("Réservation introuvable.");
        if (booking.StudentUserId != studentUserId)
            throw new InvalidOperationException("Cette réservation ne t'appartient pas.");
        if (booking.Status != "completed" || booking.CompletedAt == null)
            throw new InvalidOperationException("Cette séance ne peut pas être contestée.");
        if (DateTime.UtcNow > booking.CompletedAt.Value.Add(DisputeWindow))
            throw new InvalidOperationException("La fenêtre de contestation de 2h est dépassée.");

        booking.Status = "disputed";
        booking.DisputedAt = DateTime.UtcNow;
        booking.DisputeReason = reason;
        booking.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var tutorUserId = booking.TutorProfile?.UserId;
        if (tutorUserId.HasValue)
            await _ntfy.PublishAsync($"winplus-user-{tutorUserId.Value}", "Séance contestée",
                $"L'élève conteste la séance du {booking.SessionDate:dd/MM/yyyy}. Le support WinPlus va examiner le dossier.",
                priority: "high", userId: tutorUserId.Value, type: "TutorBooking");
        await _ntfy.PublishAdminAsync("Litige cours particulier",
            $"Réservation #{booking.Id} contestée par l'élève #{booking.StudentUserId} — motif : {reason}", tags: new[] { "warning" });

        return MapToDto(booking);
    }

    public async Task<List<TutorPendingBookingDto>> GetPendingRequestsAsync(int tutorUserId)
    {
        var bookings = await _context.TutorBookings
            .Include(b => b.TutorProfile).ThenInclude(p => p!.User)
            .Include(b => b.Student)
            .Where(b => b.TutorProfile!.UserId == tutorUserId && b.Status == "pending_tutor_approval")
            .OrderBy(b => b.CreatedAt)
            .ToListAsync();

        var now = DateTime.UtcNow;
        return bookings.Select(b => new TutorPendingBookingDto
        {
            Booking = MapToDto(b),
            MinutesRemaining = Math.Max(0, (int)(GetResponseDeadline(b).Subtract(now)).TotalMinutes),
        }).ToList();
    }

    private async Task<TutorBooking> LoadForTutorActionAsync(int tutorUserId, int bookingId)
    {
        var booking = await _context.TutorBookings
            .Include(b => b.TutorProfile).ThenInclude(p => p!.User)
            .Include(b => b.Student)
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new InvalidOperationException("Réservation introuvable.");
        if (booking.TutorProfile?.UserId != tutorUserId)
            throw new InvalidOperationException("Cette réservation ne t'appartient pas.");
        return booking;
    }

    /// <summary>
    /// Le répétiteur doit répondre dans son délai de préavis configuré, compté
    /// depuis la demande — sans jamais dépasser 1h avant le début de la
    /// séance (répondre après coup n'a pas de sens).
    /// </summary>
    private static DateTime GetResponseDeadline(TutorBooking b)
    {
        var byNotice = b.CreatedAt.AddHours(b.TutorProfile?.NoticeHours ?? 24);
        var sessionStart = b.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(b.StartTime), DateTimeKind.Utc);
        var latest = sessionStart.AddHours(-1);
        return byNotice < latest ? byNotice : latest;
    }

    /// <summary>
    /// Marque la réservation remboursée et prévient élève + admin. Aucun appel
    /// de remboursement NotchPay réel n'existe dans ce projet — voir la note
    /// sur PaymentService.RefundPaymentAsync (flip de statut uniquement) — donc
    /// le virement Mobile Money réel reste un geste manuel à faire côté admin.
    /// </summary>
    private async Task SimulateRefundAsync(TutorBooking booking, string reasonLabel)
    {
        booking.PaymentStatus = "refunded";
        await _context.SaveChangesAsync();

        await _ntfy.PublishAsync($"winplus-user-{booking.StudentUserId}", "Réservation " + reasonLabel,
            $"Ta séance du {booking.SessionDate:dd/MM/yyyy} a été {reasonLabel}. Remboursement en cours.",
            userId: booking.StudentUserId, type: "TutorBooking");
        await _ntfy.PublishAdminAsync("Remboursement manuel requis — cours particulier",
            $"Réservation #{booking.Id} ({booking.PriceXaf} XAF, réf. {booking.NotchpayReference}) : rembourser l'élève #{booking.StudentUserId} via NotchPay/MoMo.",
            tags: new[] { "moneybag" });
    }

    public async Task<List<TutorAvailabilityOccurrenceDto>> GetAvailabilityCalendarAsync(int tutorUserId, DateOnly weekStart)
    {
        weekStart = GetWeekStart(weekStart);
        var tutorProfile = await _context.TutorProfiles
            .Include(p => p.AvailabilitySlots)
            .FirstOrDefaultAsync(p => p.UserId == tutorUserId && p.IsActive)
            ?? throw new InvalidOperationException("Répétiteur introuvable ou non actif.");

        var weekEnd = weekStart.AddDays(6);
        var weekBookings = await _context.TutorBookings
            .Where(b => b.TutorProfileId == tutorProfile.Id && b.SessionDate >= weekStart && b.SessionDate <= weekEnd && ActiveStatuses.Contains(b.Status))
            .ToListAsync();

        var weekClosed = tutorProfile.MaxSessionsPerWeek is int max && weekBookings.Count >= max;
        var result = new List<TutorAvailabilityOccurrenceDto>();

        for (var i = 0; i < 7; i++)
        {
            var date = weekStart.AddDays(i);
            var dayOfWeek = (int)date.DayOfWeek;
            foreach (var slot in tutorProfile.AvailabilitySlots.Where(s => s.IsActive && s.DayOfWeek == dayOfWeek))
            {
                var alreadyBooked = weekBookings.Any(b => b.SessionDate == date && b.StartTime < slot.EndTime && b.EndTime > slot.StartTime);
                var sessionStartUtc = date.ToDateTime(TimeOnly.FromTimeSpan(slot.StartTime), DateTimeKind.Utc);
                var respectsNotice = sessionStartUtc >= DateTime.UtcNow.AddHours(tutorProfile.NoticeHours);

                result.Add(new TutorAvailabilityOccurrenceDto
                {
                    Date = date,
                    StartTime = slot.StartTime.ToString(@"hh\:mm"),
                    EndTime = slot.EndTime.ToString(@"hh\:mm"),
                    IsBookable = !weekClosed && !alreadyBooked && respectsNotice && !tutorProfile.IsOnVacation,
                });
            }
        }

        return result.OrderBy(o => o.Date).ThenBy(o => o.StartTime).ToList();
    }

    public async Task<bool> TryHandleNotchPayWebhookAsync(string eventId, string eventType, NotchPayWebhookTransaction transaction)
    {
        if (string.IsNullOrEmpty(transaction.Reference) || !transaction.Reference.StartsWith($"{ReferencePrefix}-"))
            return false;

        var booking = await _context.TutorBookings
            .Include(b => b.TutorProfile).ThenInclude(p => p!.User)
            .FirstOrDefaultAsync(b => b.NotchpayReference == transaction.Reference);
        if (booking == null)
        {
            _logger.LogWarning("Réservation répétiteur introuvable pour référence {Ref}", transaction.Reference);
            return true;
        }

        var wasAlreadyFinal = booking.PaymentStatus is "completed" or "failed";
        booking.PaymentStatus = MapNotchPayStatus(transaction.Status) ?? booking.PaymentStatus;
        booking.UpdatedAt = DateTime.UtcNow;

        if (!wasAlreadyFinal && booking.PaymentStatus == "completed")
        {
            // Le paiement réussi ne confirme pas la séance : le répétiteur doit
            // encore accepter (US-REP-05). Les fonds restent "en attente"
            // (aucun mouvement réel — voir la note sur l'escrow simulé) jusqu'à
            // l'acceptation, l'expiration ou le refus.
            booking.Status = "pending_tutor_approval";
            await _context.SaveChangesAsync();

            var tutorUserId = booking.TutorProfile?.UserId;
            if (tutorUserId.HasValue)
                await _ntfy.PublishAsync($"winplus-user-{tutorUserId.Value}", "Nouvelle demande de réservation",
                    $"Séance du {booking.SessionDate:dd/MM/yyyy} de {booking.StartTime:hh\\:mm} à {booking.EndTime:hh\\:mm} — réponds avant expiration du délai.",
                    priority: "high", userId: tutorUserId.Value, type: "TutorBooking");
            await _ntfy.PublishAsync($"winplus-user-{booking.StudentUserId}", "Paiement reçu — en attente du répétiteur",
                $"Ta demande du {booking.SessionDate:dd/MM/yyyy} est payée et en attente d'acceptation par le répétiteur.",
                userId: booking.StudentUserId, type: "TutorBooking");
        }
        else if (!wasAlreadyFinal && booking.PaymentStatus == "failed")
        {
            booking.Status = "cancelled";
            booking.CancellationReason = "Paiement échoué.";
            await _context.SaveChangesAsync();
        }
        else
        {
            await _context.SaveChangesAsync();
        }

        return true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<int> CountActiveBookingsInWeekAsync(int tutorProfileId, DateOnly weekStart)
    {
        var weekEnd = weekStart.AddDays(6);
        return await _context.TutorBookings.CountAsync(b =>
            b.TutorProfileId == tutorProfileId && b.SessionDate >= weekStart && b.SessionDate <= weekEnd && ActiveStatuses.Contains(b.Status));
    }

    private static DateOnly GetWeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>9 chiffres locaux → préfixe "237" ajouté ; déjà préfixé (237… ou +237…) → inchangé.</summary>
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

    private static string? MapNotchPayStatus(string? notchpayStatus) => notchpayStatus?.ToLowerInvariant() switch
    {
        "complete" or "completed" or "success" or "successful" => "completed",
        "failed" or "failure" or "canceled" or "cancelled" or "rejected" => "failed",
        "pending" or "processing" or "initiated" => "pending",
        "expired" or "timeout" => "failed",
        _ => null
    };

    private static TutorBookingDto MapToDto(TutorBooking b)
    {
        var now = DateTime.UtcNow;
        var sessionEndUtc = b.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(b.EndTime), DateTimeKind.Utc);
        return new TutorBookingDto
        {
            Id = b.Id,
            TutorUserId = b.TutorProfile?.UserId ?? 0,
            TutorName = b.TutorProfile?.User != null ? $"{b.TutorProfile.User.FirstName} {b.TutorProfile.User.LastName}".Trim() : null,
            TutorAvatarUrl = b.TutorProfile?.User?.AvatarUrl,
            StudentUserId = b.StudentUserId,
            StudentName = b.Student != null ? $"{b.Student.FirstName} {b.Student.LastName}".Trim() : null,
            SessionDate = b.SessionDate,
            StartTime = b.StartTime.ToString(@"hh\:mm"),
            EndTime = b.EndTime.ToString(@"hh\:mm"),
            Mode = b.Mode,
            PriceXaf = b.PriceXaf,
            Status = b.Status,
            PaymentStatus = b.PaymentStatus,
            NotchpayReference = b.NotchpayReference,
            CancellationReason = b.CancellationReason,
            CompletedAt = b.CompletedAt,
            EscrowReleasedAt = b.EscrowReleasedAt,
            DisputedAt = b.DisputedAt,
            DisputeReason = b.DisputeReason,
            DisputeResolution = b.DisputeResolution,
            DisputeResolutionNote = b.DisputeResolutionNote,
            DisputeResolvedAt = b.DisputeResolvedAt,
            Subject = b.Subject,
            SummaryText = b.SummaryText,
            CanMarkCompleted = b.Status == "confirmed" && now >= sessionEndUtc,
            CanDispute = b.Status == "completed" && b.CompletedAt.HasValue && now <= b.CompletedAt.Value.Add(DisputeWindow),
            CreatedAt = b.CreatedAt,
        };
    }

    private async Task<TutorBookingDto> MapToDtoAsync(TutorBooking b)
    {
        if (b.TutorProfile?.User == null)
            await _context.Entry(b).Reference(x => x.TutorProfile!).Query().Include(p => p.User).LoadAsync();
        return MapToDto(b);
    }

    public async Task<TutorBookingDto> SetSummaryAsync(int tutorUserId, int bookingId, string summaryText)
    {
        var booking = await LoadForTutorActionAsync(tutorUserId, bookingId);
        if (booking.Status is not ("completed" or "disputed"))
            throw new InvalidOperationException("Le compte-rendu n'est disponible qu'après la séance.");
        booking.SummaryText = summaryText;
        booking.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return MapToDto(booking);
    }

    private static readonly string[] ValidDisputeResolutions = { "refunded_full", "refunded_partial", "released_to_tutor" };

    public async Task<TutorBookingDto> ResolveDisputeAsync(int adminUserId, int bookingId, string resolution, string? note)
    {
        if (!ValidDisputeResolutions.Contains(resolution))
            throw new InvalidOperationException("Décision invalide.");

        var booking = await _context.TutorBookings
            .Include(b => b.TutorProfile).ThenInclude(p => p!.User)
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new InvalidOperationException("Réservation introuvable.");
        if (booking.Status != "disputed")
            throw new InvalidOperationException("Cette réservation n'est pas en litige.");

        booking.DisputeResolution = resolution;
        booking.DisputeResolutionNote = note;
        booking.DisputeResolvedByUserId = adminUserId;
        booking.DisputeResolvedAt = DateTime.UtcNow;
        booking.Status = resolution == "released_to_tutor" ? "completed" : "cancelled";
        booking.UpdatedAt = DateTime.UtcNow;

        if (resolution == "released_to_tutor")
        {
            booking.EscrowReleasedAt = DateTime.UtcNow;
        }
        else
        {
            booking.PaymentStatus = "refunded";
            await _ntfy.PublishAdminAsync("Remboursement manuel requis — litige cours particulier",
                $"Réservation #{booking.Id} ({booking.PriceXaf} XAF, réf. {booking.NotchpayReference}) : " +
                $"{(resolution == "refunded_full" ? "remboursement total" : "remboursement partiel")} décidé par le support — effectuer le virement NotchPay/MoMo.",
                tags: new[] { "moneybag" });
        }
        await _context.SaveChangesAsync();

        var decisionLabel = resolution switch
        {
            "refunded_full" => "un remboursement total à l'élève",
            "refunded_partial" => "un remboursement partiel à l'élève",
            _ => "la libération des fonds au répétiteur",
        };
        await _ntfy.PublishAsync($"winplus-user-{booking.StudentUserId}", "Décision sur ton litige",
            $"Le support WinPlus a tranché : {decisionLabel} pour la séance du {booking.SessionDate:dd/MM/yyyy}.",
            userId: booking.StudentUserId, type: "TutorBooking");
        var tutorUserId = booking.TutorProfile?.UserId;
        if (tutorUserId.HasValue)
            await _ntfy.PublishAsync($"winplus-user-{tutorUserId.Value}", "Décision sur le litige",
                $"Le support WinPlus a tranché : {decisionLabel} pour la séance du {booking.SessionDate:dd/MM/yyyy}.",
                userId: tutorUserId.Value, type: "TutorBooking");

        return MapToDto(booking);
    }

    public async Task<List<TutorBookingDto>> GetDisputedBookingsAsync()
    {
        var bookings = await _context.TutorBookings
            .Include(b => b.TutorProfile).ThenInclude(p => p!.User)
            .Include(b => b.Student)
            .Where(b => b.Status == "disputed")
            .OrderBy(b => b.DisputedAt)
            .ToListAsync();
        return bookings.Select(MapToDto).ToList();
    }
}
