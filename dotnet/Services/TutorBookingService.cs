using System.Data;
using System.Text.Json;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Services;

/// <summary>
/// Réservation de séances de cours particulier (Module 6). Ferme réellement
/// le calendrier d'un répétiteur une fois son plafond hebdomadaire atteint 
/// contrairement à <see cref="TutorProfileService.UpdateAvailabilityAsync"/>
/// qui ne plafonne que la grille de créneaux *déclarés*, ce service compte
/// les réservations *effectives* de la semaine (US-ELV-01, US-PRO-08).
/// </summary>
public class TutorBookingService : ITutorBookingService
{
    private static readonly string[] ActiveStatuses = { "pending_payment", "pending_tutor_approval", "confirmed" };
    private static readonly string[] AllowedModes = { "online", "student_home", "tutor_home", "neutral_place" };
    private const string ReferencePrefix = "TBK";
    private static readonly JsonSerializerOptions QuoteJson = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _context;
    private readonly INotchPayService _notchPay;
    private readonly INtfyService _ntfy;
    private readonly ILogger<TutorBookingService> _logger;
    private readonly IWalletService _wallet;
    private readonly ITutorRefundService _refunds;

    /// <summary>
    /// Délai de contestation (17.8, défaut 24 h) et taux de frais NotchPay
    /// (17.9) : voir <see cref="TutorPolicySettings"/>.
    /// </summary>
    private readonly TutorPolicySettings _policy;

    /// <summary>
    /// Borne haute d'une séance unique (lot 9.1). Le suivi 17.2 limite déjà la
    /// durée à la plage libre en continu ; cette borne de sécurité évite
    /// qu'une plage déclarée « 08:00-20:00 » permette une séance de 12 h par
    /// erreur. Réglable par <c>TutorBooking:MaxSessionMinutes</c> (défaut 360,
    /// soit 6 h, ramené à un multiple de 30 entre 30 et 720).
    /// </summary>
    private readonly int _maxSessionMinutes;
    public const int DefaultMaxSessionMinutes = 360;

    /// <summary>Contrôles payeur / participants (lot 9.3).</summary>
    private readonly TutorBookingPartyResolver _parties;

    public TutorBookingService(ApplicationDbContext context, INotchPayService notchPay, INtfyService ntfy, ILogger<TutorBookingService> logger, IWalletService wallet, IConfiguration configuration, ITutorRefundService refunds)
    {
        _wallet = wallet;
        _refunds = refunds;
        _parties = new TutorBookingPartyResolver(context);
        _policy = TutorPolicySettings.From(configuration);
        _context = context;
        _notchPay = notchPay;
        _ntfy = ntfy;
        _logger = logger;
        _maxSessionMinutes = MaxSessionMinutesFrom(configuration);
    }

    private IQueryable<TutorProfile> ProfilesWithPricing() => _context.TutorProfiles
        .Include(p => p.AvailabilitySlots)
        .Include(p => p.User)
        .Include(p => p.Subjects)
        .Include(p => p.Rates)
        .Include(p => p.RateModifiers)
        .Include(p => p.DurationDiscounts)
        .Include(p => p.GroupDiscounts);

    /// <summary>Réservation avec tout ce que l'affichage et les contrôles d'accès lisent (lot 9.3 : participants, payeur, groupe).</summary>
    private IQueryable<TutorBooking> BookingsWithDetails() => _context.TutorBookings
        .Include(b => b.TutorProfile).ThenInclude(p => p!.User)
        .Include(b => b.Topics)
        .Include(b => b.Student)
        .Include(b => b.Participants).ThenInclude(p => p.Student)
        .Include(b => b.Payer)
        .Include(b => b.StudyGroup);

    /// <summary>Institution du compte s'il s'agit d'un compte institution (lot 9.3), sinon null.</summary>
    private async Task<int?> ViewerInstitutionAsync(int userId) =>
        await _context.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.Role == "institution")
            .Select(u => u.InstitutionId)
            .FirstOrDefaultAsync();

    /// <summary>
    /// Côté payeur (lot 9.3) : le portefeuille payeur, le compte qui a réservé,
    /// ou tout compte de l'institution payeuse. Avant le 9.3, l'élève.
    /// </summary>
    private static bool IsPayerSide(TutorBooking b, int userId, int? viewerInstitutionId) =>
        (b.PayerUserId ?? b.StudentUserId) == userId
        || b.BookedByUserId == userId
        || (viewerInstitutionId != null && b.PayerInstitutionId == viewerInstitutionId);

    private static bool IsParticipant(TutorBooking b, int userId) =>
        b.StudentUserId == userId || b.Participants.Any(p => p.StudentUserId == userId);

    private static IEnumerable<int> ParticipantIds(TutorBooking b) =>
        b.Participants.Count > 0 ? b.Participants.Select(p => p.StudentUserId) : new[] { b.StudentUserId };

    /// <summary>
    /// Contrôles communs au devis et à la réservation, hors chevauchement (lu
    /// séparément, dans la transaction Serializable pour la réservation) :
    /// profil actif, tranches de 30 min, durée bornée, séance contenue dans une
    /// plage de disponibilité continue, préavis respecté. Renvoie la matière
    /// retenue (canonique, telle que déclarée par le répétiteur).
    /// </summary>
    private string? ValidateRequestedSession(TutorProfile tutorProfile, DateOnly date, TimeSpan start, int durationMinutes, string? subject) =>
        ValidateSession(tutorProfile, date, start, durationMinutes, subject, _maxSessionMinutes);

    /// <summary>Durée maximale d'une séance selon la configuration (TutorBooking:MaxSessionMinutes).</summary>
    public static int MaxSessionMinutesFrom(IConfiguration configuration)
    {
        var configured = configuration.GetValue<int?>("TutorBooking:MaxSessionMinutes") ?? DefaultMaxSessionMinutes;
        return Math.Clamp(configured / TutorPricing.SliceMinutes * TutorPricing.SliceMinutes, TutorPricing.SliceMinutes, 720);
    }

    /// <summary>
    /// Contrôles d'une séance hors chevauchement, partagés avec l'engagement
    /// mensuel (lot 9.4) : une seule source pour les règles d'agenda.
    /// </summary>
    public static string? ValidateSession(TutorProfile tutorProfile, DateOnly date, TimeSpan start, int durationMinutes, string? subject, int maxSessionMinutes)
    {
        var _maxSessionMinutes = maxSessionMinutes;
        if (!tutorProfile.IsActive)
            throw new InvalidOperationException("Ce répétiteur n'est pas actif pour l'instant.");
        if (tutorProfile.IsOnVacation)
            throw new InvalidOperationException("Ce répétiteur a suspendu ses réservations (vacances).");

        if (!TutorPricing.IsAlignedOnSlice(start))
            throw new InvalidOperationException("Une séance commence à l'heure pile ou à la demie (tranches de 30 min).");
        if (durationMinutes < TutorPricing.SliceMinutes || durationMinutes % TutorPricing.SliceMinutes != 0)
            throw new InvalidOperationException("La durée d'une séance doit être un multiple de 30 minutes (30 minutes minimum).");
        if (durationMinutes > _maxSessionMinutes)
            throw new InvalidOperationException($"Une séance ne peut pas dépasser {_maxSessionMinutes / 60.0:0.#} h.");

        var end = start + TimeSpan.FromMinutes(durationMinutes);
        var inRange = TutorPricing.MergedRanges(tutorProfile.AvailabilitySlots, (int)date.DayOfWeek)
            .Any(r => r.Start <= start && r.End >= end);
        if (!inRange)
            throw new InvalidOperationException("Ce créneau ne correspond à aucune disponibilité déclarée par le répétiteur.");

        var sessionStartUtc = date.ToDateTime(TimeOnly.FromTimeSpan(start), DateTimeKind.Utc);
        if (sessionStartUtc < DateTime.UtcNow.AddHours(tutorProfile.NoticeHours))
            throw new InvalidOperationException($"Ce répétiteur demande un préavis minimum de {tutorProfile.NoticeHours} heures.");

        if (string.IsNullOrWhiteSpace(subject))
            return null;
        var canonical = tutorProfile.Subjects
            .FirstOrDefault(s => string.Equals(s.Subject.Trim(), subject.Trim(), StringComparison.OrdinalIgnoreCase))?.Subject;
        return canonical ?? throw new InvalidOperationException("Ce répétiteur n'enseigne pas cette matière.");
    }

    public async Task<TutorPriceQuoteDto> GetQuoteAsync(TutorPriceQuoteRequestDto request)
    {
        var mode = AllowedModes.Contains(request.Mode) ? request.Mode : "online";
        if (!TimeSpan.TryParse(request.StartTime, out var start))
            throw new InvalidOperationException("Heure de début invalide.");
        // Endpoint anonyme : la borne de durée est vérifiée avant tout calcul,
        // pour qu'une durée démesurée ne fasse pas boucler le découpage en tranches.
        if (request.DurationMinutes > _maxSessionMinutes)
            throw new InvalidOperationException($"Une séance ne peut pas dépasser {_maxSessionMinutes / 60.0:0.#} h.");
        if (request.ParticipantCount < 1 || request.ParticipantCount > TutorPricing.MaxGroupSizeCap)
            throw new InvalidOperationException($"Une séance compte de 1 à {TutorPricing.MaxGroupSizeCap} élèves.");

        var tutorProfile = await ProfilesWithPricing().AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == request.TutorUserId)
            ?? throw new InvalidOperationException("Ce répétiteur n'existe pas.");

        // Le prix se calcule même si le créneau n'est plus libre, pour que
        // l'élève voie le tarif ; la disponibilité est signalée à part.
        var subject = string.IsNullOrWhiteSpace(request.Subject) ? tutorProfile.Subjects.FirstOrDefault()?.Subject : request.Subject;
        string? unavailable = null;
        try
        {
            subject = ValidateRequestedSession(tutorProfile, request.SessionDate, start, request.DurationMinutes, subject) ?? subject;
            var end = start + TimeSpan.FromMinutes(request.DurationMinutes);
            var conflict = await _context.TutorBookings.AsNoTracking().AnyAsync(b =>
                b.TutorProfileId == tutorProfile.Id && b.SessionDate == request.SessionDate &&
                ActiveStatuses.Contains(b.Status) && b.StartTime < end && b.EndTime > start);
            if (conflict) unavailable = "Ce créneau chevauche une séance déjà réservée.";
        }
        catch (InvalidOperationException ex) when (ex.Message != "Ce répétiteur n'enseigne pas cette matière.")
        {
            unavailable = ex.Message;
        }

        var quote = TutorPricing.Compute(TutorPricing.Grid.FromProfile(tutorProfile), request.SessionDate, start, request.DurationMinutes, mode, subject, request.ParticipantCount);
        quote.IsAvailable = unavailable == null;
        quote.UnavailableReason = unavailable;
        return quote;
    }

    public async Task<TutorBookingCreatedResponseDto> CreateBookingAsync(int actingUserId, CreateTutorBookingRequestDto request)
    {
        var mode = AllowedModes.Contains(request.Mode) ? request.Mode : "online";
        if (!TimeSpan.TryParse(request.StartTime, out var start))
            throw new InvalidOperationException("Heure de début invalide.");

        // Lot 9.1 : durée explicite (multiple de 30 min). Les anciens clients
        // envoient encore début + fin : la durée en est déduite.
        int durationMinutes;
        if (request.DurationMinutes is int d)
            durationMinutes = d;
        else if (TimeSpan.TryParse(request.EndTime, out var legacyEnd) && legacyEnd > start)
            durationMinutes = (int)(legacyEnd - start).TotalMinutes;
        else
            throw new InvalidOperationException("L'heure de fin doit être après l'heure de début.");
        var end = start + TimeSpan.FromMinutes(durationMinutes);

        var tutorProfile = await ProfilesWithPricing()
            .FirstOrDefaultAsync(p => p.UserId == request.TutorUserId)
            ?? throw new InvalidOperationException("Ce répétiteur n'existe pas.");

        var subject = ValidateRequestedSession(tutorProfile, request.SessionDate, start, durationMinutes, request.Subject)
            ?? tutorProfile.Subjects.FirstOrDefault()?.Subject;

        // Lot 9.3 (17.4) : payeur et participants vérifiés côté serveur (rôle
        // réel du compte, liens parent/institution/groupe en base). Jamais
        // d'élève ajouté sans lien vérifié avec le payeur.
        var parties = await _parties.ResolveAsync(actingUserId, request.PayerType, request.ParticipantUserIds,
            request.StudyGroupId, request.TutorAssignmentId, tutorProfile.Id);

        // Prix toujours recalculé ici, jamais repris du client (17.3), pour le
        // nombre réel de participants (prix de groupe 9.3).
        var quote = TutorPricing.Compute(TutorPricing.Grid.FromProfile(tutorProfile), request.SessionDate, start, durationMinutes, mode, subject, parties.ParticipantIds.Count);
        var price = quote.TotalXaf;
        var topics = TutorPricing.SanitizeTopics(request.Topics);

        // Lot 9.2 (17.6) : Mobile Money (flux NotchPay inchangé) ou solde du
        // portefeuille du payeur. Lot 9.3 : le payeur est l'élève, le parent
        // (portefeuille parent : dotation puis recharges), le titulaire du
        // portefeuille institution, ou le membre du groupe qui réserve.
        var payByWallet = TutorPaymentMethods.IsWallet(request.PaymentMethod);
        var payerUserId = parties.PayerUserId;
        if (payByWallet)
        {
            if (price <= 0)
                throw new InvalidOperationException("Montant de séance invalide.");
            // Contrôle anticipé pour un message clair ; le contrôle qui fait foi
            // est le débit sous verrou, dans la transaction ci-dessous.
            var available = await _wallet.GetAvailableAsync(payerUserId);
            if (available < price)
                throw new InsufficientWalletBalanceException(Math.Max(0, available), price);
        }
        else if (string.IsNullOrWhiteSpace(request.Phone))
        {
            throw new InvalidOperationException("Indique ton numéro Mobile Money.");
        }

        var booking = new TutorBooking
        {
            TutorProfileId = tutorProfile.Id,
            // Élève principal = premier participant (avis, historique élève, anciens clients).
            StudentUserId = parties.ParticipantIds[0],
            PayerType = parties.PayerType,
            BookedByUserId = parties.BookedByUserId,
            PayerInstitutionId = parties.InstitutionId,
            StudyGroupId = parties.StudyGroupId,
            ParticipantCount = parties.ParticipantIds.Count,
            TutorAssignmentId = parties.AssignmentId,
            ObjectType = TutorBookingObjectTypes.SingleSession,
            Participants = parties.ParticipantIds.Select(id => new TutorBookingParticipant { StudentUserId = id }).ToList(),
            Subject = subject,
            SessionDate = request.SessionDate,
            StartTime = start,
            EndTime = end,
            Mode = mode,
            PriceXaf = price,
            PriceBreakdownJson = JsonSerializer.Serialize(quote, QuoteJson),
            // Payé par solde : directement en attente du tuteur, comme un
            // paiement NotchPay confirmé (aucun frais d'encaissement).
            Status = payByWallet ? "pending_tutor_approval" : "pending_payment",
            PaymentStatus = payByWallet ? "completed" : "pending",
            PaymentMethod = payByWallet ? TutorPaymentMethods.Wallet : TutorPaymentMethods.MobileMoney,
            PaymentFeeXaf = payByWallet ? 0m : null,
            PayerUserId = payerUserId,
            PhoneNumber = payByWallet ? null : request.Phone,
            Topics = topics.Select(t => new TutorBookingTopic { Label = t }).ToList(),
        };

        // Vérifier le conflit puis insérer n'est pas atomique : sans protection,
        // deux élèves tapant "Réserver" sur le même créneau au même instant
        // passent tous les deux le contrôle avant que l'un des deux n'ait
        // encore écrit sa ligne, et les deux réservations sont créées. Une
        // transaction Serializable détecte cette collision au COMMIT (Postgres
        // lève 40001) plutôt que d'écrire silencieusement un double-booking.
        // Lot 9.1 : le test de chevauchement porte sur des intervalles
        // [début, fin[ quelconques (durées variables, multiples de 30 min) ;
        // deux séances qui se suivent (14:00-15:30 puis 15:30-16:00) ne se
        // chevauchent pas, deux séances qui se recouvrent même d'une tranche
        // sont refusées.
        await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
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

            _context.TutorBookings.Add(booking);
            await _context.SaveChangesAsync();

            // Paiement par solde atomique avec la réservation : le débit est
            // posé dans la même transaction Serializable, sous le verrou du
            // portefeuille du payeur (RunLockedAsync réutilise la transaction
            // en cours). Solde insuffisant → exception → rollback : ni débit
            // sans réservation, ni réservation sans débit.
            if (payByWallet)
            {
                var label = $"Cours particulier du {booking.SessionDate:dd/MM/yyyy} ({booking.StartTime:hh\\:mm}-{booking.EndTime:hh\\:mm})"
                            + (booking.ParticipantCount > 1 ? $", {booking.ParticipantCount} élèves" : "");
                if (parties.PayerType == TutorPayerTypes.Parent)
                {
                    // Portefeuille parent (Module 14) : dotation mensuelle
                    // encore valide d'abord, puis part permanente (recharges).
                    await _wallet.RunLockedAsync(payerUserId, async () =>
                    {
                        await _wallet.DebitParentWalletAsync(payerUserId, price, WalletService.TutoringPaymentKey(booking.Id), label, "TutorBooking", booking.Id);
                        return true;
                    });
                }
                else
                {
                    await _wallet.RunLockedAsync(payerUserId, () => _wallet.DebitAsync(new WalletEntry(
                        payerUserId, WalletEntryTypes.TutoringPayment, price, WalletService.TutoringPaymentKey(booking.Id),
                        label, "TutorBooking", booking.Id)));
                }
            }

            await tx.CommitAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "40001" })
        {
            throw new InvalidOperationException("Ce créneau vient d'être réservé par un autre élève.");
        }
        catch (PostgresException ex) when (ex.SqlState == "40001")
        {
            throw new InvalidOperationException("Ce créneau vient d'être réservé par un autre élève.");
        }

        if (payByWallet)
        {
            await SyncWalletAsync(booking.Id);
            await NotifyPaymentConfirmedAsync(booking);
            return new TutorBookingCreatedResponseDto { Booking = await MapToDtoAsync(booking) };
        }

        // Le compte qui réserve règle le paiement Mobile Money (parent,
        // institution ou élève) ; le remboursement éventuel ira au payeur.
        var student = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actingUserId);
        var studentEmail = student?.Email ?? $"user{actingUserId}@winplus.cm";
        var studentName = $"{student?.FirstName} {student?.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(studentName)) studentName = studentEmail.Split('@')[0];

        try
        {
            // Le web et le mobile n'envoient pas le numéro dans le même format
            // (avec ou sans indicatif 237)  on normalise ici plutôt que
            // d'imposer une convention aux deux front-ends.
            var e164Phone = NormalizePhoneToE164(request.Phone!);
            var channel = DetectChannelFromPhone(e164Phone);
            var tutorName = $"{tutorProfile.User?.FirstName} {tutorProfile.User?.LastName}".Trim();
            var result = await _notchPay.InitiatePaymentAsync(
                e164Phone, price, booking.Id,
                $"WinPlus  Cours particulier avec {tutorName}", studentEmail, studentName, channel, ReferencePrefix);

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

            // NotchPayService distingue déjà un rejet 4xx (numéro invalide,
            // opérateur non supporté…) d'une panne d'infra (5xx/réseau) via
            // le type d'exception. Auparavant les deux remontaient le même
            // message générique côté élève  impossible de savoir s'il fallait
            // corriger son numéro ou juste réessayer plus tard.
            if (ex is InvalidOperationException notchPayRejection)
                throw new InvalidOperationException(
                    $"Le paiement a été refusé par l'opérateur mobile money. Vérifie ton numéro et l'opérateur choisi. ({notchPayRejection.Message})");

            throw new InvalidOperationException("Le service de paiement est momentanément indisponible. Réessaie dans quelques minutes.");
        }
    }

    public async Task<TutorBookingDto> GetByIdAsync(int userId, int bookingId)
    {
        var booking = await BookingsWithDetails()
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new InvalidOperationException("Réservation introuvable.");

        // Lot 9.3 : tuteur, participants, et côté payeur (parent, comptes de
        // l'institution payeuse, membre du groupe qui a réservé).
        var viewerInstitution = await ViewerInstitutionAsync(userId);
        if (booking.TutorProfile?.UserId != userId && !IsParticipant(booking, userId) && !IsPayerSide(booking, userId, viewerInstitution))
            throw new InvalidOperationException("Tu n'as pas accès à cette réservation.");

        return (await MapManyAsync(new List<TutorBooking> { booking }, userId, viewerInstitution))[0];
    }

    /// <summary>
    /// Réservations du compte connecté côté élève ou payeur (lot 9.3) : celles
    /// où il participe (un enfant voit les séances réservées pour lui par son
    /// parent), celles qu'il paie ou a réservées, et pour un compte
    /// institution toutes celles payées par son institution.
    /// </summary>
    public async Task<List<TutorBookingDto>> GetMyBookingsAsStudentAsync(int userId)
    {
        var viewerInstitution = await ViewerInstitutionAsync(userId);
        var bookings = await BookingsWithDetails()
            .Where(b => b.StudentUserId == userId
                     || b.Participants.Any(p => p.StudentUserId == userId)
                     || b.PayerUserId == userId
                     || b.BookedByUserId == userId
                     || (viewerInstitution != null && b.PayerInstitutionId == viewerInstitution))
            .OrderByDescending(b => b.SessionDate).ThenByDescending(b => b.StartTime)
            .ToListAsync();
        return await MapManyAsync(bookings, userId, viewerInstitution);
    }

    public async Task<List<TutorBookingDto>> GetMyBookingsAsTutorAsync(int tutorUserId)
    {
        var bookings = await BookingsWithDetails()
            .Where(b => b.TutorProfile!.UserId == tutorUserId)
            .OrderByDescending(b => b.SessionDate).ThenByDescending(b => b.StartTime)
            .ToListAsync();
        return await MapManyAsync(bookings, tutorUserId, null);
    }

    public async Task<TutorBookingDto> CancelBookingAsync(int userId, int bookingId, string? reason)
    {
        var booking = await BookingsWithDetails()
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new InvalidOperationException("Réservation introuvable.");

        // Lot 9.3 : seul le côté payeur (il récupère l'argent) ou le tuteur
        // annule. Un participant qui n'a pas payé (enfant, membre du groupe)
        // demande l'annulation à celui qui a réservé.
        var isPayerSide = IsPayerSide(booking, userId, await ViewerInstitutionAsync(userId));
        var isTutor = booking.TutorProfile?.UserId == userId;
        if (!isPayerSide && !isTutor)
            throw new InvalidOperationException(IsParticipant(booking, userId)
                ? "Cette séance a été réservée et payée par quelqu'un d'autre : demande-lui de l'annuler."
                : "Tu ne peux pas annuler cette réservation.");
        var isStudent = isPayerSide;
        // Module 21 (décision §4.H) : un litige ouvert doit être tranché par
        // l'administrateur (Module 29) avant tout nouveau changement de
        // statut — sans cette garde, l'élève pouvait annuler une réservation
        // en litige, la rendant irrésolvable et invisible pour l'administrateur.
        if (booking.Status is "cancelled" or "completed" or "disputed" or "rejected" or "expired")
            throw new InvalidOperationException(booking.Status == "disputed"
                ? "Cette réservation est en litige : elle doit être tranchée par le support avant toute annulation."
                : "Cette réservation ne peut plus être annulée.");
        // Lot 9.4 : une séance d'engagement mensuel s'annule seule selon les
        // mêmes règles (politique du tuteur appliquée à son heure de début),
        // sauf tant que le paiement unique du mois n'est pas encaissé : c'est
        // alors l'engagement entier qui s'annule.
        if (booking.TutorEngagementId != null && booking.Status == "pending_payment")
            throw new InvalidOperationException("Le paiement de cet engagement mensuel est en cours : annule l'engagement entier.");
        if (booking.TutorEngagementId != null
            && booking.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(booking.StartTime), DateTimeKind.Utc) <= DateTime.UtcNow)
            throw new InvalidOperationException("Cette séance du mois a déjà commencé : elle reste due (conteste-la après coup si elle n'a pas eu lieu).");

        booking.Status = "cancelled";
        booking.CancelledByUserId = userId;
        booking.CancelledAt = DateTime.UtcNow;
        booking.CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason;
        booking.UpdatedAt = DateTime.UtcNow;

        // Politique d'annulation (17.9) : appliquée seulement quand le payeur
        // annule un paiement encaissé (frais NotchPay déduits) ; une
        // annulation du tuteur est toujours remboursée intégralement, frais
        // retenus sur le tuteur. Remboursement crédité tout de suite au
        // portefeuille du payeur (TutorRefundService).
        var decision = await _refunds.RefundAsync(booking, isTutor ? TutorRefundCause.TutorCancelled : TutorRefundCause.PayerCancelled);
        await SyncWalletAsync(booking.Id);
        if (decision != null)
            await NotifyRefundAsync(booking, decision, "annulée");
        if (booking.TutorEngagementId is int engagementId)
            await TutorEngagementService.RefreshStatusAsync(_context, engagementId);

        // Lot 9.3 : le tuteur si le payeur annule ; le payeur et tous les
        // participants si le tuteur annule (participants prévenus dans les
        // deux cas : un enfant voit sa séance disparaître).
        var notified = new HashSet<int>(ParticipantIds(booking));
        if (isStudent && booking.TutorProfile?.UserId is int tutorId) notified.Add(tutorId);
        if (isTutor) notified.Add(booking.PayerUserId ?? booking.StudentUserId);
        notified.Remove(userId);
        foreach (var id in notified)
            await _ntfy.PublishAsync($"winplus-user-{id}", "Séance annulée",
                $"Une séance du {booking.SessionDate:dd/MM/yyyy} a été annulée.", userId: id, type: "TutorBooking");

        return MapToDto(booking);
    }

    /// <summary>Prévient les participants (et le payeur s'il n'en fait pas partie) d'un événement de la séance (lot 9.3).</summary>
    private async Task NotifyParticipantsAndPayerAsync(TutorBooking booking, string title, string message, bool includePayer = true)
    {
        var ids = new HashSet<int>(ParticipantIds(booking));
        if (includePayer) ids.Add(booking.PayerUserId ?? booking.StudentUserId);
        foreach (var id in ids)
            await _ntfy.PublishAsync($"winplus-user-{id}", title, message, userId: id, type: "TutorBooking");
    }

    /// <summary>Lot 9.4 : le tuteur accepte ou refuse l'engagement mensuel entier, jamais une de ses séances isolément.</summary>
    private static void EnsureNotMonthlyRequest(TutorBooking booking)
    {
        if (booking.TutorEngagementId != null)
            throw new InvalidOperationException("Cette séance fait partie d'un engagement mensuel : accepte ou refuse l'engagement entier.");
    }

    public async Task<TutorBookingDto> ConfirmBookingAsync(int tutorUserId, int bookingId)
    {
        var booking = await LoadForTutorActionAsync(tutorUserId, bookingId);
        EnsureNotMonthlyRequest(booking);
        if (booking.Status != "pending_tutor_approval")
            throw new InvalidOperationException("Cette demande n'est plus en attente.");

        booking.Status = "confirmed";
        booking.ConfirmedAt = DateTime.UtcNow;
        booking.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await NotifyParticipantsAndPayerAsync(booking, "Séance confirmée !",
            $"Le répétiteur a accepté la séance du {booking.SessionDate:dd/MM/yyyy}.");
        return MapToDto(booking);
    }

    public async Task<TutorBookingDto> DeclineBookingAsync(int tutorUserId, int bookingId, string? reason)
    {
        var booking = await LoadForTutorActionAsync(tutorUserId, bookingId);
        EnsureNotMonthlyRequest(booking);
        if (booking.Status != "pending_tutor_approval")
            throw new InvalidOperationException("Cette demande n'est plus en attente.");

        booking.Status = "rejected";
        booking.CancellationReason = string.IsNullOrWhiteSpace(reason) ? "Refusée par le répétiteur." : reason;
        booking.UpdatedAt = DateTime.UtcNow;
        var decision = await _refunds.RefundAsync(booking, TutorRefundCause.TutorDeclined);
        if (decision != null)
            await NotifyRefundAsync(booking, decision, "refusée par le répétiteur");
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

        await NotifyParticipantsAndPayerAsync(booking, "Comment s'est passée la séance ?",
            $"Laisse un avis pour ce répétiteur. Tu as {_policy.DisputeWindowHours} h pour contester la séance si elle ne s'est pas bien passée ; ensuite le paiement est versé au répétiteur.");
        return MapToDto(booking);
    }

    public async Task<TutorBookingDto> DisputeBookingAsync(int studentUserId, int bookingId, string reason)
    {
        var booking = await BookingsWithDetails()
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new InvalidOperationException("Réservation introuvable.");
        // Lot 9.3 : un participant (qui a suivi la séance) ou le côté payeur
        // (dont l'argent est en jeu) peut contester ; le remboursement éventuel
        // va toujours au payeur.
        if (!IsParticipant(booking, studentUserId) && !IsPayerSide(booking, studentUserId, await ViewerInstitutionAsync(studentUserId)))
            throw new InvalidOperationException("Cette réservation ne t'appartient pas.");
        if (booking.Status != "completed" || booking.CompletedAt == null)
            throw new InvalidOperationException("Cette séance ne peut pas être contestée.");
        if (DateTime.UtcNow > booking.CompletedAt.Value.Add(_policy.DisputeWindow))
            throw new InvalidOperationException($"La fenêtre de contestation de {_policy.DisputeWindowHours} h est dépassée.");

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
            $"Réservation #{booking.Id} contestée par le compte #{studentUserId} (payeur {booking.PayerType ?? "student"} #{booking.PayerUserId ?? booking.StudentUserId})  motif : {reason}", tags: new[] { "warning" });

        return MapToDto(booking);
    }

    public async Task<List<TutorPendingBookingDto>> GetPendingRequestsAsync(int tutorUserId)
    {
        var bookings = await BookingsWithDetails()
            // Lot 9.4 : les séances d'un engagement mensuel sont proposées
            // ensemble, par les demandes d'engagement (TutorEngagementService).
            .Where(b => b.TutorProfile!.UserId == tutorUserId && b.Status == "pending_tutor_approval" && b.TutorEngagementId == null)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var dtos = await MapManyAsync(bookings, tutorUserId, null);
        return bookings.Select((b, i) => new TutorPendingBookingDto
        {
            Booking = dtos[i],
            MinutesRemaining = Math.Max(0, (int)(GetResponseDeadline(b).Subtract(now)).TotalMinutes),
        }).ToList();
    }

    private async Task<TutorBooking> LoadForTutorActionAsync(int tutorUserId, int bookingId)
    {
        var booking = await BookingsWithDetails()
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new InvalidOperationException("Réservation introuvable.");
        if (booking.TutorProfile?.UserId != tutorUserId)
            throw new InvalidOperationException("Cette réservation ne t'appartient pas.");
        return booking;
    }

    /// <summary>
    /// Le répétiteur doit répondre dans son délai de préavis configuré, compté
    /// depuis la demande  sans jamais dépasser 1h avant le début de la
    /// séance (répondre après coup n'a pas de sens).
    /// </summary>
    public static DateTime GetResponseDeadline(TutorBooking b)
    {
        var byNotice = b.CreatedAt.AddHours(b.TutorProfile?.NoticeHours ?? 24);
        var sessionStart = b.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(b.StartTime), DateTimeKind.Utc);
        var latest = sessionStart.AddHours(-1);
        return byNotice < latest ? byNotice : latest;
    }

    /// <summary>
    /// Prévient le payeur (et le tuteur s'il supporte des frais) d'un
    /// remboursement déjà crédité au portefeuille (lot 9.2). Plus d'alerte
    /// admin « remboursement manuel requis » : seul un échec technique du
    /// crédit alerte l'admin (TutorRefundService).
    /// </summary>
    private async Task NotifyRefundAsync(TutorBooking booking, TutorRefundDecision decision, string reasonLabel)
    {
        var payerId = booking.PayerUserId ?? booking.StudentUserId;
        var date = $"{booking.SessionDate:dd/MM/yyyy}";
        string message;
        if (decision.RefundXaf <= 0)
            message = decision.RefundPercent == 0
                ? $"Ta séance du {date} a été {reasonLabel}. Politique d'annulation du répétiteur : aucun remboursement pour une annulation aussi tardive."
                : $"Ta séance du {date} a été {reasonLabel}. Après déduction des frais Mobile Money ({decision.FeeRetainedXaf:0} XAF), il ne reste rien à rembourser.";
        else if (decision.FeeBorneBy == TutorFeeBearer.Payer)
            message = $"Ta séance du {date} a été {reasonLabel}. {decision.RefundXaf:0} XAF ont été crédités sur ton portefeuille WinPlus " +
                      $"({decision.RefundPercent} % selon la politique du répétiteur, moins {decision.FeeRetainedXaf:0} XAF de frais Mobile Money).";
        else if (decision.Cause == TutorRefundCause.PayerCancelled && decision.RefundPercent < 100)
            message = $"Ta séance du {date} a été {reasonLabel}. {decision.RefundXaf:0} XAF ont été crédités sur ton portefeuille WinPlus ({decision.RefundPercent} % selon la politique du répétiteur).";
        else
            message = $"Ta séance du {date} a été {reasonLabel}. {decision.RefundXaf:0} XAF ont été crédités sur ton portefeuille WinPlus (remboursement intégral).";

        await _ntfy.PublishAsync($"winplus-user-{payerId}", "Réservation " + reasonLabel,
            message, userId: payerId, type: "TutorBooking");

        var tutorUserId = booking.TutorProfile?.UserId;
        if (tutorUserId.HasValue && booking.TutorFeeChargedXaf is > 0)
            await _ntfy.PublishAsync($"winplus-user-{tutorUserId.Value}", "Frais Mobile Money retenus",
                $"La séance du {date} a été {reasonLabel} de ton fait : l'élève est remboursé intégralement et les frais Mobile Money " +
                $"({booking.TutorFeeChargedXaf:0} XAF) ont été retenus sur ton solde, comme le prévoit la politique de tutorat.",
                userId: tutorUserId.Value, type: "TutorBooking");
    }

    /// <summary>Pas entre deux points de départ réservables, et unité de durée (17.2 : tranches de 30 min).</summary>
    private static readonly TimeSpan OccurrenceStep = TimeSpan.FromMinutes(TutorPricing.SliceMinutes);

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
            var dayBookings = weekBookings.Where(b => b.SessionDate == date).ToList();

            // Lot 9.1 : plages fusionnées (deux lignes contiguës = une plage
            // continue), un point de départ par tranche de 30 min, et depuis
            // chacun toute durée multiple de 30 min jusqu'à la prochaine
            // séance déjà réservée ou la fin de la plage (bornée par
            // _maxSessionMinutes). Une plage accueille ainsi plusieurs
            // réservations de personnes différentes à la suite.
            foreach (var range in TutorPricing.MergedRanges(tutorProfile.AvailabilitySlots, dayOfWeek))
            {
                for (var start = TutorPricing.CeilToSlice(range.Start); start + OccurrenceStep <= range.End; start += OccurrenceStep)
                {
                    var s0 = start;
                    if (dayBookings.Any(b => b.StartTime <= s0 && b.EndTime > s0)) continue;

                    var freeUntil = range.End;
                    var nextBooking = dayBookings.Where(b => b.StartTime > s0).Select(b => b.StartTime).DefaultIfEmpty(range.End).Min();
                    if (nextBooking < freeUntil) freeUntil = nextBooking;
                    var maxMinutes = (int)(freeUntil - start).TotalMinutes / TutorPricing.SliceMinutes * TutorPricing.SliceMinutes;
                    maxMinutes = Math.Min(maxMinutes, _maxSessionMinutes);
                    if (maxMinutes < TutorPricing.SliceMinutes) continue;

                    var availableDurations = Enumerable.Range(1, maxMinutes / TutorPricing.SliceMinutes)
                        .Select(n => n * TutorPricing.SliceMinutes)
                        .ToList();

                    var sessionStartUtc = date.ToDateTime(TimeOnly.FromTimeSpan(start), DateTimeKind.Utc);
                    var respectsNotice = sessionStartUtc >= DateTime.UtcNow.AddHours(tutorProfile.NoticeHours);

                    result.Add(new TutorAvailabilityOccurrenceDto
                    {
                        Date = date,
                        StartTime = start.ToString(@"hh\:mm"),
                        AvailableDurationsMinutes = availableDurations,
                        MaxDurationMinutes = maxMinutes,
                        IsBookable = !weekClosed && respectsNotice && !tutorProfile.IsOnVacation,
                    });
                }
            }
        }

        return result.OrderBy(o => o.Date).ThenBy(o => o.StartTime).ToList();
    }

    public async Task<bool> TryHandleNotchPayWebhookAsync(string eventId, string eventType, NotchPayWebhookTransaction transaction)
    {
        if (string.IsNullOrEmpty(transaction.Reference) || !transaction.Reference.StartsWith($"{ReferencePrefix}-"))
            return false;

        var booking = await BookingsWithDetails()
            .FirstOrDefaultAsync(b => b.NotchpayReference == transaction.Reference);
        if (booking == null)
        {
            _logger.LogWarning("Réservation répétiteur introuvable pour référence {Ref}", transaction.Reference);
            return true;
        }

        var wasAlreadyFinal = booking.PaymentStatus is "completed" or "failed";

        // Défense en profondeur (même règle que PaymentService, Module 17) : un
        // paiement confirmé pour un autre montant que le prix figé de la séance
        // ne la confirme pas ; l'administrateur est alerté.
        if (!wasAlreadyFinal && MapNotchPayStatus(transaction.Status) == "completed" && transaction.Amount is decimal notified
            && decimal.Round(notified, 0, MidpointRounding.AwayFromZero) != decimal.Round(booking.PriceXaf, 0, MidpointRounding.AwayFromZero))
        {
            _logger.LogError("Webhook NotchPay {Ref} rejeté : montant notifié {Notified} XAF ≠ prix de la séance {Price} XAF (réservation {BookingId})",
                transaction.Reference, notified, booking.PriceXaf, booking.Id);
            try
            {
                await _ntfy.PublishAdminAsync("Écart de montant sur un paiement de séance",
                    $"Réservation #{booking.Id} : montant notifié {notified:0} XAF, prix {booking.PriceXaf:0} XAF. La séance n'a pas été confirmée, vérification manuelle requise.",
                    tags: new[] { "rotating_light", "moneybag" });
            }
            catch (Exception ex) { _logger.LogError(ex, "Alerte d'écart de montant non envoyée (réservation {BookingId})", booking.Id); }
            return true;
        }

        booking.PaymentStatus = MapNotchPayStatus(transaction.Status) ?? booking.PaymentStatus;
        booking.UpdatedAt = DateTime.UtcNow;

        if (!wasAlreadyFinal && booking.PaymentStatus == "completed")
        {
            // Frais NotchPay figés au moment de l'encaissement (17.9) : un
            // changement ultérieur du taux configuré ne modifie pas le
            // remboursement d'une séance déjà payée.
            booking.PaymentMethod ??= TutorPaymentMethods.MobileMoney;
            booking.PaymentFeeXaf ??= TutorRefundRules.NotchPayFee(booking.PriceXaf, _policy.NotchPayFeePercent);

            if (booking.Status == "cancelled")
            {
                // Paiement confirmé après une annulation par l'élève (ou après
                // l'abandon de l'écran de paiement) : la séance ne reprend pas
                // vie, le montant payé est rendu au portefeuille, frais
                // Mobile Money déduits comme pour toute annulation du payeur.
                await _refunds.RefundAsync(booking, TutorRefundCause.PayerCancelled, overridePercent: 100);
                await SyncWalletAsync(booking.Id);
                return true;
            }

            // Le paiement réussi ne confirme pas la séance : le répétiteur doit
            // encore accepter (US-REP-05). Le revenu du tuteur reste « en
            // attente » au registre jusqu'à la fin de la fenêtre de contestation.
            booking.Status = "pending_tutor_approval";
            await _context.SaveChangesAsync();
            await NotifyPaymentConfirmedAsync(booking);
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

        await SyncWalletAsync(booking.Id);
        return true;
    }

    /// <summary>Paiement encaissé (NotchPay ou solde) : la demande part au tuteur.</summary>
    private async Task NotifyPaymentConfirmedAsync(TutorBooking booking)
    {
        if (booking.TutorProfile == null)
            await _context.Entry(booking).Reference(b => b.TutorProfile).LoadAsync();
        var tutorUserId = booking.TutorProfile?.UserId;
        var topicsText = booking.Topics.Count > 0 ? $" Sujets : {string.Join(", ", booking.Topics.Select(t => t.Label))}." : "";
        if (tutorUserId.HasValue)
            await _ntfy.PublishAsync($"winplus-user-{tutorUserId.Value}", "Nouvelle demande de réservation",
                $"Séance du {booking.SessionDate:dd/MM/yyyy} de {booking.StartTime:hh\\:mm} à {booking.EndTime:hh\\:mm}{(booking.Subject != null ? $" ({booking.Subject})" : "")}.{topicsText} Réponds avant expiration du délai.",
                priority: "high", userId: tutorUserId.Value, type: "TutorBooking");
        var payerId = booking.PayerUserId ?? booking.StudentUserId;
        await _ntfy.PublishAsync($"winplus-user-{payerId}", "Paiement reçu — en attente du répétiteur",
            $"Ta demande du {booking.SessionDate:dd/MM/yyyy} est payée et en attente d'acceptation par le répétiteur.",
            userId: payerId, type: "TutorBooking");

        // Lot 9.3 : chaque élève pour qui la séance a été réservée (par un
        // parent, une institution, un membre du groupe) en est prévenu.
        foreach (var participantId in ParticipantIds(booking).Where(id => id != payerId && id != booking.BookedByUserId).Distinct())
            await _ntfy.PublishAsync($"winplus-user-{participantId}", "Une séance a été réservée pour toi",
                $"Séance du {booking.SessionDate:dd/MM/yyyy} de {booking.StartTime:hh\\:mm} à {booking.EndTime:hh\\:mm}, en attente d'acceptation du répétiteur. Retrouve-la dans Mes séances.",
                userId: participantId, type: "TutorBooking");
    }

    /// <summary>
    /// Lot 2, Module 1 : traduit l'état de la réservation en écritures du
    /// journal (revenu en attente au paiement, confirmé à la libération de
    /// l'escrow, annulé au remboursement). Idempotent ; un échec est rattrapé
    /// par WalletReconciliationService et n'interrompt jamais le flux.
    /// </summary>
    private async Task SyncWalletAsync(int bookingId)
    {
        try { await _wallet.SyncTutorBookingAsync(bookingId); }
        catch (Exception ex) { _logger.LogError(ex, "Écritures de la réservation {BookingId} non posées (réconciliation à venir)", bookingId); }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<int> CountActiveBookingsInWeekAsync(int tutorProfileId, DateOnly weekStart)
    {
        var weekEnd = weekStart.AddDays(6);
        return await _context.TutorBookings.CountAsync(b =>
            b.TutorProfileId == tutorProfileId && b.SessionDate >= weekStart && b.SessionDate <= weekEnd && ActiveStatuses.Contains(b.Status));
    }

    public static DateOnly GetWeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

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

    private static string? DisplayName(User? u) =>
        u == null ? null : ($"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.Email?.Split('@')[0]);

    /// <summary>
    /// Conversion d'une liste (lot 9.3) : charge en une requête les noms des
    /// institutions payeuses et renseigne les indicateurs du compte connecté.
    /// </summary>
    private async Task<List<TutorBookingDto>> MapManyAsync(List<TutorBooking> bookings, int? viewerUserId, int? viewerInstitutionId)
    {
        var institutionIds = bookings.Where(b => b.PayerInstitutionId != null).Select(b => b.PayerInstitutionId!.Value).Distinct().ToList();
        var names = institutionIds.Count == 0
            ? new Dictionary<int, string>()
            : await _context.Institutions.AsNoTracking().IgnoreQueryFilters()
                .Where(i => institutionIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name);
        return bookings.Select(b =>
        {
            var dto = MapToDto(b);
            if (b.PayerInstitutionId is int iid && names.TryGetValue(iid, out var n)) dto.PayerName = n;
            if (viewerUserId is int v)
            {
                dto.ViewerIsPayer = IsPayerSide(b, v, viewerInstitutionId);
                dto.ViewerIsParticipant = IsParticipant(b, v);
                // Contester : participant ou payeur ; annuler : payeur seulement (CancelBookingAsync).
                if (!dto.ViewerIsPayer && !dto.ViewerIsParticipant) dto.CanDispute = false;
            }
            return dto;
        }).ToList();
    }

    private TutorBookingDto MapToDto(TutorBooking b)
    {
        var now = DateTime.UtcNow;
        var participants = b.Participants.Count > 0
            ? b.Participants.OrderBy(p => p.StudentUserId == b.StudentUserId ? 0 : 1).ThenBy(p => p.Id)
                .Select(p => new TutorBookingParticipantDto { UserId = p.StudentUserId, Name = DisplayName(p.Student), AvatarUrl = p.Student?.AvatarUrl }).ToList()
            : new List<TutorBookingParticipantDto> { new() { UserId = b.StudentUserId, Name = DisplayName(b.Student), AvatarUrl = b.Student?.AvatarUrl } };
        var sessionEndUtc = b.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(b.EndTime), DateTimeKind.Utc);
        var disputeDeadline = b.CompletedAt?.Add(_policy.DisputeWindow);
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
            Topics = b.Topics.Select(t => t.Label).ToList(),
            DurationMinutes = (int)(b.EndTime - b.StartTime).TotalMinutes,
            PriceBreakdown = ParseBreakdown(b.PriceBreakdownJson),
            SummaryText = b.SummaryText,
            CanMarkCompleted = b.Status == "confirmed" && now >= sessionEndUtc,
            CanDispute = b.Status == "completed" && disputeDeadline.HasValue && b.EscrowReleasedAt == null && now <= disputeDeadline.Value,
            DisputeDeadline = b.Status == "completed" && b.EscrowReleasedAt == null ? disputeDeadline : null,
            DisputeWindowHours = _policy.DisputeWindowHours,
            PaymentMethod = b.PaymentMethod ?? (b.NotchpayReference != null ? TutorPaymentMethods.MobileMoney : null),
            RefundedAt = b.RefundedAt,
            RefundCause = b.RefundCause,
            RefundPercent = b.RefundPercent,
            RefundAmountXaf = b.RefundAmountXaf,
            RefundFeeXaf = b.RefundFeeXaf,
            RefundFeeBorneBy = b.RefundFeeBorneBy,
            TutorFeeChargedXaf = b.TutorFeeChargedXaf,
            TutorRetainedXaf = b.TutorRetainedXaf,
            PayerType = b.PayerType ?? TutorPayerTypes.Student,
            PayerUserId = b.PayerUserId ?? b.StudentUserId,
            PayerName = b.PayerUserId == null || b.PayerUserId == b.StudentUserId ? DisplayName(b.Student) ?? DisplayName(b.Payer) : DisplayName(b.Payer),
            BookedByUserId = b.BookedByUserId,
            PayerInstitutionId = b.PayerInstitutionId,
            StudyGroupId = b.StudyGroupId,
            StudyGroupName = b.StudyGroup?.Name,
            ParticipantCount = Math.Max(b.ParticipantCount, participants.Count),
            Participants = participants,
            TutorAssignmentId = b.TutorAssignmentId,
            ObjectType = b.ObjectType,
            TutorEngagementId = b.TutorEngagementId,
            CreatedAt = b.CreatedAt,
        };
    }

    private static TutorPriceQuoteDto? ParseBreakdown(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<TutorPriceQuoteDto>(json, QuoteJson); }
        catch (JsonException) { return null; }
    }

    private async Task<TutorBookingDto> MapToDtoAsync(TutorBooking b)
    {
        if (b.TutorProfile?.User == null)
            await _context.Entry(b).Reference(x => x.TutorProfile!).Query().Include(p => p.User).LoadAsync();
        // Lot 9.3 : noms des participants, du payeur et du groupe (réservation tout juste créée).
        await _context.Entry(b).Collection(x => x.Participants).Query().Include(p => p.Student).LoadAsync();
        if (b.PayerUserId != null && b.Payer == null) await _context.Entry(b).Reference(x => x.Payer).LoadAsync();
        if (b.StudyGroupId != null && b.StudyGroup == null) await _context.Entry(b).Reference(x => x.StudyGroup).LoadAsync();
        var viewer = b.BookedByUserId ?? b.PayerUserId ?? b.StudentUserId;
        return (await MapManyAsync(new List<TutorBooking> { b }, viewer, b.PayerInstitutionId))[0];
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

    public async Task<TutorBookingDto> ResolveDisputeAsync(int adminUserId, int bookingId, string resolution, string? note, decimal? amountXaf = null)
    {
        if (!ValidDisputeResolutions.Contains(resolution))
            throw new InvalidOperationException("Décision invalide.");

        var booking = await BookingsWithDetails()
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new InvalidOperationException("Réservation introuvable.");
        if (booking.Status != "disputed")
            throw new InvalidOperationException("Cette réservation n'est pas en litige.");

        // Module 29 : le remboursement partiel exige un montant explicite,
        // borné par le montant réellement bloqué en escrow — jamais davantage.
        decimal? refundAmount = null;
        if (resolution == "refunded_partial")
        {
            if (amountXaf is not > 0)
                throw new InvalidOperationException("Indique le montant remboursé pour un remboursement partiel.");
            refundAmount = RevenueSplit.Xaf(amountXaf.Value);
            if (refundAmount > booking.PriceXaf)
                throw new InvalidOperationException(
                    $"Le montant remboursé ({refundAmount:0} XAF) dépasse le montant bloqué ({booking.PriceXaf:0} XAF).");
        }

        booking.DisputeResolution = resolution;
        booking.DisputeResolutionNote = note;
        booking.DisputeResolvedByUserId = adminUserId;
        booking.DisputeResolvedAt = DateTime.UtcNow;
        booking.Status = resolution == "released_to_tutor" ? "completed" : "cancelled";
        booking.UpdatedAt = DateTime.UtcNow;

        if (resolution == "released_to_tutor")
        {
            booking.EscrowReleasedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
        else
        {
            // Lot 9.2 (17.9) : le payeur qui gagne le litige est crédité tout
            // de suite sur son portefeuille ; les frais NotchPay sont absorbés
            // par la plateforme. En remboursement partiel, la part conservée
            // par le tuteur (prix moins remboursement) est posée au registre
            // par SyncTutorBookingAsync, à la commission habituelle, avec les
            // clés d'avant le 9.2 (aucun doublon possible).
            await _refunds.RefundAsync(booking,
                resolution == "refunded_full" ? TutorRefundCause.DisputeRefundFull : TutorRefundCause.DisputeRefundPartial,
                refundAmount);
        }
        // Contre-passe l'écriture d'origine (en attente) ou la confirme selon
        // l'issue, et pose les écritures du remboursement : idempotent.
        await SyncWalletAsync(booking.Id);

        var decisionLabel = resolution switch
        {
            "refunded_full" => $"un remboursement total ({booking.RefundAmountXaf ?? booking.PriceXaf:0} XAF crédités sur le portefeuille WinPlus du payeur)",
            "refunded_partial" => $"un remboursement partiel ({booking.RefundAmountXaf ?? refundAmount:0} XAF crédités sur le portefeuille WinPlus du payeur)",
            _ => "la libération des fonds au répétiteur",
        };
        var payerId = booking.PayerUserId ?? booking.StudentUserId;
        await _ntfy.PublishAsync($"winplus-user-{payerId}", "Décision sur ton litige",
            $"Le support WinPlus a tranché : {decisionLabel} pour la séance du {booking.SessionDate:dd/MM/yyyy}.",
            userId: payerId, type: "TutorBooking");
        var tutorUserId = booking.TutorProfile?.UserId;
        if (tutorUserId.HasValue)
            await _ntfy.PublishAsync($"winplus-user-{tutorUserId.Value}", "Décision sur le litige",
                $"Le support WinPlus a tranché : {decisionLabel} pour la séance du {booking.SessionDate:dd/MM/yyyy}.",
                userId: tutorUserId.Value, type: "TutorBooking");

        return MapToDto(booking);
    }

    public async Task<List<TutorBookingDto>> GetDisputedBookingsAsync()
    {
        var bookings = await BookingsWithDetails()
            .Where(b => b.Status == "disputed")
            .OrderBy(b => b.DisputedAt)
            .ToListAsync();
        return await MapManyAsync(bookings, null, null);
    }
}
