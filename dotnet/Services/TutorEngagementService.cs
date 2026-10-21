using System.Data;
using System.Text.Json;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Backend.Utils;

namespace Backend.Services;

/// <summary>Création d'engagement refusée : au moins une séance générée n'est pas réservable.</summary>
public sealed class TutorEngagementConflictException : InvalidOperationException
{
    public IReadOnlyList<string> Conflicts { get; }

    public TutorEngagementConflictException(IReadOnlyList<string> conflicts)
        : base(conflicts.Count == 1
            ? $"Une séance du mois n'est pas disponible : {conflicts[0]}."
            : $"{conflicts.Count} séances du mois ne sont pas disponibles : {string.Join(" ; ", conflicts.Take(5))}{(conflicts.Count > 5 ? "…" : "")}.")
    {
        Conflicts = conflicts;
    }
}

public interface ITutorEngagementService
{
    Task<TutorEngagementQuoteDto> GetQuoteAsync(TutorEngagementQuoteRequestDto request);
    Task<TutorEngagementCreatedResponseDto> CreateAsync(int actingUserId, CreateTutorEngagementRequestDto request);
    Task<TutorEngagementDto> GetByIdAsync(int userId, int engagementId);
    /// <summary>Engagements du compte côté payeur ou participant (parent, institution, membre du groupe, élève).</summary>
    Task<List<TutorEngagementDto>> GetMineAsync(int userId);
    Task<List<TutorEngagementDto>> GetMineAsTutorAsync(int tutorUserId);
    Task<List<TutorPendingEngagementDto>> GetPendingForTutorAsync(int tutorUserId);
    Task<TutorEngagementDto> AcceptAsync(int tutorUserId, int engagementId);
    Task<TutorEngagementDto> DeclineAsync(int tutorUserId, int engagementId, string? reason);
    Task<TutorEngagementDto> CancelAsync(int userId, int engagementId, string? reason);
    Task<TutorEngagementDto> SetAutoRenewAsync(int userId, int engagementId, bool enabled);
    Task<TutorEngagementCreatedResponseDto> RenewAsync(int userId, int engagementId, RenewTutorEngagementRequestDto request);
    Task<bool> TryHandleNotchPayWebhookAsync(string eventId, string eventType, NotchPayWebhookTransaction transaction);

    // ── Tâches de fond (TutorEngagementLifecycleService) ──
    Task<int> CancelAbandonedPaymentsAsync(DateTime nowUtc);
    Task<int> ExpireStaleRequestsAsync(DateTime nowUtc);
    Task<int> CompleteFinishedAsync(DateTime nowUtc);
    Task<int> SendRenewalRemindersAsync(DateTime nowUtc);
    Task<int> RunAutoRenewalsAsync(DateTime nowUtc);
}

/// <summary>
/// Engagement mensuel de tutorat (lot 9.4, suivi 17.7). Voir
/// <see cref="TutorEngagement"/> pour le modèle et <see cref="TutorMonthlyPlan"/>
/// pour les règles de calcul.
///
/// L'argent : le total du mois est payé d'avance en une fois (solde : un seul
/// débit <c>tutoring_engagement_payment:{id}</c> ; Mobile Money : un seul
/// paiement NotchPay « TEN- »). Chaque séance générée porte sa part (PriceXaf)
/// et sa part des frais Mobile Money ; à partir de là, le registre suit chaque
/// séance exactement comme une séance unique (SyncTutorBookingAsync) : revenu
/// en attente à l'encaissement, confirmé à la libération (24 h après la séance
/// sans litige), remboursements et frais séance par séance. D'où, pour un
/// engagement payé par solde : débit du payeur = somme des parts libérées
/// (revenu tuteur + commission) + somme des remboursements.
/// </summary>
public class TutorEngagementService : ITutorEngagementService
{
    private static readonly string[] SlotHolding = { "pending_payment", "pending_tutor_approval", "confirmed" };
    private static readonly string[] Upcoming = { "pending_tutor_approval", "confirmed" };
    private static readonly string[] AllowedModes = { "online", "student_home", "tutor_home", "neutral_place" };
    private const string ReferencePrefix = "TEN";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Un paiement Mobile Money d'engagement sans confirmation après ce délai libère les créneaux (l'encaissement tardif est remboursé).</summary>
    public static readonly TimeSpan AbandonedPaymentDelay = TimeSpan.FromHours(2);

    private readonly ApplicationDbContext _context;
    private readonly INotchPayService _notchPay;
    private readonly INtfyService _ntfy;
    private readonly ILogger<TutorEngagementService> _logger;
    private readonly IWalletService _wallet;
    private readonly ITutorRefundService _refunds;
    private readonly TutorPolicySettings _policy;
    private readonly TutorBookingPartyResolver _parties;
    private readonly int _maxSessionMinutes;

    public TutorEngagementService(ApplicationDbContext context, INotchPayService notchPay, INtfyService ntfy, ILogger<TutorEngagementService> logger,
        IWalletService wallet, IConfiguration configuration, ITutorRefundService refunds, IPlatformSettings? platformSettings = null)
    {
        _context = context;
        _notchPay = notchPay;
        _ntfy = ntfy;
        _logger = logger;
        _wallet = wallet;
        _refunds = refunds;
        _policy = TutorPolicySettings.From(configuration, platformSettings);
        _parties = new TutorBookingPartyResolver(context);
        _maxSessionMinutes = TutorBookingService.MaxSessionMinutesFrom(configuration);
    }

    public static string PaymentKey(int engagementId) => $"tutoring_engagement_payment:{engagementId}";

    private IQueryable<TutorProfile> ProfilesWithPricing() => _context.TutorProfiles
        .Include(p => p.AvailabilitySlots)
        .Include(p => p.User)
        .Include(p => p.Subjects)
        .Include(p => p.Rates)
        .Include(p => p.RateModifiers)
        .Include(p => p.DurationDiscounts)
        .Include(p => p.GroupDiscounts)
        .Include(p => p.Packages);

    private IQueryable<TutorEngagement> WithDetails() => _context.TutorEngagements
        .Include(e => e.TutorProfile).ThenInclude(p => p!.User)
        .Include(e => e.Slots)
        .Include(e => e.Sessions).ThenInclude(s => s.Participants).ThenInclude(p => p.Student)
        .Include(e => e.Sessions).ThenInclude(s => s.Topics)
        .AsSplitQuery();

    private static TutorPackage? MonthlyOffer(TutorProfile p) =>
        p.Packages.FirstOrDefault(x => x.Kind == TutorPackageKinds.Monthly);

    private static string SessionLabel(TutorBooking b) => TutorMonthlyPlan.Label(b.SessionDate, b.StartTime);

    private static DateTime StartUtc(TutorBooking b) => b.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(b.StartTime), DateTimeKind.Utc);

    // ── Plan : séances, prix, disponibilité ─────────────────────────────

    private sealed record PlannedSession(TutorMonthlyPlan.Occurrence Occurrence, TutorPriceQuoteDto Quote, decimal PriceXaf, string? Unavailable);

    private sealed record Plan(List<PlannedSession> Sessions, string? Subject, List<TutorMonthlyPlan.Slot> Slots)
    {
        public List<string> Conflicts => Sessions.Where(s => s.Unavailable != null)
            .Select(s => $"{TutorMonthlyPlan.Label(s.Occurrence.Date, s.Occurrence.Start)} : {s.Unavailable}").ToList();
        public decimal SubtotalXaf => Sessions.Sum(s => s.Quote.TotalXaf);
        public decimal TotalXaf => Sessions.Sum(s => s.PriceXaf);
    }

    /// <summary>
    /// Génère les séances du mois et les chiffre. La disponibilité de chacune
    /// est vérifiée comme pour une séance unique (plage, tranches, préavis,
    /// vacances), plus le chevauchement avec les séances déjà réservées et le
    /// plafond hebdomadaire du tuteur (séances existantes + nouvelles). Appelée
    /// dans la transaction Serializable à la création : la lecture des séances
    /// existantes couvre alors toute la période.
    /// </summary>
    private async Task<Plan> BuildPlanAsync(TutorProfile profile, DateOnly startDate, List<TutorMonthlyPlan.Slot> slots,
        string mode, string? requestedSubject, int participantCount, int discountPercent)
    {
        string? subject;
        if (string.IsNullOrWhiteSpace(requestedSubject))
            subject = profile.Subjects.FirstOrDefault()?.Subject;
        else
            subject = profile.Subjects.FirstOrDefault(s => string.Equals(s.Subject.Trim(), requestedSubject.Trim(), StringComparison.OrdinalIgnoreCase))?.Subject
                ?? throw new InvalidOperationException("Ce répétiteur n'enseigne pas cette matière.");

        var occurrences = TutorMonthlyPlan.Occurrences(startDate, slots);
        // Semaines entières (plafond hebdomadaire) : du lundi de la première
        // semaine au dimanche de la dernière.
        var from = TutorBookingService.GetWeekStart(startDate);
        var to = TutorBookingService.GetWeekStart(TutorMonthlyPlan.EndDate(startDate)).AddDays(6);
        var existing = await _context.TutorBookings.AsNoTracking()
            .Where(b => b.TutorProfileId == profile.Id && b.SessionDate >= from && b.SessionDate <= to && SlotHolding.Contains(b.Status))
            .Select(b => new { b.SessionDate, b.StartTime, b.EndTime })
            .ToListAsync();

        var grid = TutorPricing.Grid.FromProfile(profile);
        var sessions = new List<PlannedSession>();
        var newPerWeek = new Dictionary<DateOnly, int>();
        foreach (var o in occurrences)
        {
            string? unavailable = null;
            try
            {
                TutorBookingService.ValidateSession(profile, o.Date, o.Start, o.DurationMinutes, subject, _maxSessionMinutes);
            }
            catch (InvalidOperationException ex)
            {
                unavailable = ex.Message.TrimEnd('.');
            }

            if (unavailable == null && existing.Any(b => b.SessionDate == o.Date && b.StartTime < o.End && b.EndTime > o.Start))
                unavailable = "créneau déjà réservé";

            var week = TutorBookingService.GetWeekStart(o.Date);
            newPerWeek[week] = newPerWeek.GetValueOrDefault(week) + 1;
            if (unavailable == null && profile.MaxSessionsPerWeek is int max)
            {
                var weekEnd = week.AddDays(6);
                var already = existing.Count(b => b.SessionDate >= week && b.SessionDate <= weekEnd);
                if (already + newPerWeek[week] > max)
                    unavailable = $"plafond de {max} séances par semaine atteint";
            }

            var quote = TutorPricing.Compute(grid, o.Date, o.Start, o.DurationMinutes, mode, subject, participantCount);
            sessions.Add(new PlannedSession(o, quote, TutorMonthlyPlan.DiscountedPrice(quote.TotalXaf, discountPercent), unavailable));
        }
        return new Plan(sessions, subject, slots);
    }

    private static string NormalizeMode(string? mode) => AllowedModes.Contains(mode) ? mode! : "online";

    public async Task<TutorEngagementQuoteDto> GetQuoteAsync(TutorEngagementQuoteRequestDto request)
    {
        if (request.ParticipantCount < 1 || request.ParticipantCount > TutorPricing.MaxGroupSizeCap)
            throw new InvalidOperationException($"Une séance compte de 1 à {TutorPricing.MaxGroupSizeCap} élèves.");
        var slots = TutorMonthlyPlan.ParseSlots(request.Slots, _maxSessionMinutes);
        var profile = await ProfilesWithPricing().AsNoTracking().AsSplitQuery()
            .FirstOrDefaultAsync(p => p.UserId == request.TutorUserId)
            ?? throw new InvalidOperationException("Ce répétiteur n'existe pas.");
        var offer = MonthlyOffer(profile);
        var enabled = offer is { IsActive: true };
        var discount = enabled ? TutorMonthlyPlan.ClampDiscount(offer!.DiscountPercent) : 0;
        var mode = NormalizeMode(request.Mode);
        // Lot 9.5 (17.11) : pas de devis d'engagement présentiel avec un tuteur non vérifié ou suspendu.
        TutorInPersonPolicy.EnsureModeAllowed(profile, mode);

        var plan = await BuildPlanAsync(profile, request.StartDate, slots, mode, request.Subject, request.ParticipantCount, discount);
        var conflicts = plan.Conflicts;
        if (!enabled) conflicts.Insert(0, "Ce répétiteur ne propose pas l'engagement mensuel");
        return new TutorEngagementQuoteDto
        {
            TutorUserId = request.TutorUserId,
            StartDate = request.StartDate,
            EndDate = TutorMonthlyPlan.EndDate(request.StartDate),
            Weeks = TutorMonthlyPlan.Weeks,
            Mode = mode,
            Subject = plan.Subject,
            ParticipantCount = request.ParticipantCount,
            MonthlyEnabled = enabled,
            DiscountPercent = discount,
            Slots = SlotDtos(slots),
            Sessions = plan.Sessions.Select(s => new TutorEngagementQuoteSessionDto
            {
                SessionDate = s.Occurrence.Date,
                StartTime = TutorPricing.Hhmm(s.Occurrence.Start),
                EndTime = TutorPricing.Hhmm(s.Occurrence.End),
                DurationMinutes = s.Occurrence.DurationMinutes,
                BaseTotalXaf = s.Quote.TotalXaf,
                DiscountXaf = s.Quote.TotalXaf - s.PriceXaf,
                PriceXaf = s.PriceXaf,
                IsAvailable = s.Unavailable == null,
                UnavailableReason = s.Unavailable,
            }).ToList(),
            SubtotalXaf = plan.SubtotalXaf,
            DiscountXaf = plan.SubtotalXaf - plan.TotalXaf,
            TotalXaf = plan.TotalXaf,
            IsAvailable = enabled && conflicts.Count == 0,
            Conflicts = conflicts,
        };
    }

    private static List<TutorEngagementSlotDto> SlotDtos(IEnumerable<TutorMonthlyPlan.Slot> slots) =>
        slots.Select(s => new TutorEngagementSlotDto
        {
            DayOfWeek = s.DayOfWeek,
            StartTime = TutorPricing.Hhmm(s.Start),
            DurationMinutes = s.DurationMinutes,
            EndTime = TutorPricing.Hhmm(s.End),
        }).ToList();

    // ── Création ────────────────────────────────────────────────────────

    public Task<TutorEngagementCreatedResponseDto> CreateAsync(int actingUserId, CreateTutorEngagementRequestDto request) =>
        CreateCoreAsync(actingUserId, request, renewedFrom: null);

    private async Task<TutorEngagementCreatedResponseDto> CreateCoreAsync(int actingUserId, CreateTutorEngagementRequestDto request, TutorEngagement? renewedFrom)
    {
        var mode = NormalizeMode(request.Mode);
        var slots = TutorMonthlyPlan.ParseSlots(request.Slots, _maxSessionMinutes);
        var payByWallet = TutorPaymentMethods.IsWallet(request.PaymentMethod);
        if (request.AutoRenew && !payByWallet)
            throw new InvalidOperationException("Le renouvellement automatique n'est possible qu'avec un paiement par solde.");
        if (!payByWallet && string.IsNullOrWhiteSpace(request.Phone))
            throw new InvalidOperationException("Indique ton numéro Mobile Money.");

        var profile = await ProfilesWithPricing().AsSplitQuery()
            .FirstOrDefaultAsync(p => p.UserId == request.TutorUserId)
            ?? throw new InvalidOperationException("Ce répétiteur n'existe pas.");
        var offer = MonthlyOffer(profile);
        if (offer is not { IsActive: true })
            throw new InvalidOperationException("Ce répétiteur ne propose pas l'engagement mensuel.");
        // Lot 9.5 (17.11) : engagement présentiel refusé avec un tuteur non
        // vérifié ou suspendu, à la création comme au renouvellement (manuel ou automatique).
        TutorInPersonPolicy.EnsureModeAllowed(profile, mode);
        var discount = TutorMonthlyPlan.ClampDiscount(offer.DiscountPercent);

        // Payeur et participants : exactement les contrôles du 9.3 (rôle réel,
        // lien parent accepté, rattachement institution confirmé, groupe).
        var parties = await _parties.ResolveAsync(actingUserId, request.PayerType, request.ParticipantUserIds,
            request.StudyGroupId, request.TutorAssignmentId, profile.Id);
        var topics = TutorPricing.SanitizeTopics(request.Topics);

        if (payByWallet)
        {
            // Contrôle anticipé (message clair) ; le contrôle qui fait foi est le débit sous verrou.
            var preview = await BuildPlanAsync(profile, request.StartDate, slots, mode, request.Subject, parties.ParticipantIds.Count, discount);
            if (preview.Conflicts.Count > 0) throw new TutorEngagementConflictException(preview.Conflicts);
            var available = await _wallet.GetAvailableAsync(parties.PayerUserId);
            if (available < preview.TotalXaf)
                throw new InsufficientWalletBalanceException(Math.Max(0, available), preview.TotalXaf);
        }

        var now = DateTime.UtcNow;
        TutorEngagement engagement = null!;

        // Garde anti double-réservation (comme la séance unique) : lecture des
        // séances existantes de toute la période et insertion des séances du
        // mois dans une seule transaction Serializable. Une réservation
        // concurrente sur l'un des créneaux fait échouer l'une des deux au
        // COMMIT (40001). Le débit du solde est posé dans la même transaction.
        try
        {
            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            {
                var plan = await BuildPlanAsync(profile, request.StartDate, slots, mode, request.Subject, parties.ParticipantIds.Count, discount);
                if (plan.Conflicts.Count > 0)
                    throw new TutorEngagementConflictException(plan.Conflicts);
                if (plan.TotalXaf <= 0)
                    throw new InvalidOperationException("Montant d'engagement invalide.");

                var firstStart = plan.Sessions.Min(s => s.Occurrence.Date.ToDateTime(TimeOnly.FromTimeSpan(s.Occurrence.Start), DateTimeKind.Utc));
                var byNotice = now.AddHours(profile.NoticeHours);
                var latest = firstStart.AddHours(-1);

                engagement = new TutorEngagement
                {
                    TutorProfileId = profile.Id,
                    TutorPackageId = offer.Id,
                    PayerType = parties.PayerType,
                    PayerUserId = parties.PayerUserId,
                    BookedByUserId = parties.BookedByUserId,
                    PayerInstitutionId = parties.InstitutionId,
                    StudyGroupId = parties.StudyGroupId,
                    TutorAssignmentId = parties.AssignmentId,
                    ParticipantCount = parties.ParticipantIds.Count,
                    StudentUserId = parties.ParticipantIds[0],
                    Subject = plan.Subject,
                    Mode = mode,
                    TopicsJson = topics.Count > 0 ? JsonSerializer.Serialize(topics, Json) : null,
                    StartDate = request.StartDate,
                    EndDate = TutorMonthlyPlan.EndDate(request.StartDate),
                    Weeks = TutorMonthlyPlan.Weeks,
                    DiscountPercent = discount,
                    SubtotalXaf = plan.SubtotalXaf,
                    DiscountXaf = plan.SubtotalXaf - plan.TotalXaf,
                    TotalXaf = plan.TotalXaf,
                    PaymentMethod = payByWallet ? TutorPaymentMethods.Wallet : TutorPaymentMethods.MobileMoney,
                    PaymentStatus = payByWallet ? "completed" : "pending",
                    PaymentFeeXaf = payByWallet ? 0m : null,
                    PhoneNumber = payByWallet ? null : request.Phone,
                    Status = payByWallet ? TutorEngagementStatuses.PendingTutorApproval : TutorEngagementStatuses.PendingPayment,
                    ResponseDeadlineAt = byNotice < latest ? byNotice : latest,
                    AutoRenew = request.AutoRenew && payByWallet,
                    RenewedFromEngagementId = renewedFrom?.Id,
                    CreatedAt = now,
                    Slots = slots.Select(s => new TutorEngagementSlot { DayOfWeek = s.DayOfWeek, StartTime = s.Start, DurationMinutes = s.DurationMinutes }).ToList(),
                    Sessions = plan.Sessions.Select(s => new TutorBooking
                    {
                        TutorProfileId = profile.Id,
                        StudentUserId = parties.ParticipantIds[0],
                        PayerType = parties.PayerType,
                        PayerUserId = parties.PayerUserId,
                        BookedByUserId = parties.BookedByUserId,
                        PayerInstitutionId = parties.InstitutionId,
                        StudyGroupId = parties.StudyGroupId,
                        TutorAssignmentId = parties.AssignmentId,
                        ParticipantCount = parties.ParticipantIds.Count,
                        ObjectType = TutorBookingObjectTypes.MonthlySession,
                        Participants = parties.ParticipantIds.Select(id => new TutorBookingParticipant { StudentUserId = id }).ToList(),
                        Subject = plan.Subject,
                        SessionDate = s.Occurrence.Date,
                        StartTime = s.Occurrence.Start,
                        EndTime = s.Occurrence.End,
                        Mode = mode,
                        PriceXaf = s.PriceXaf,
                        PriceBreakdownJson = JsonSerializer.Serialize(s.Quote, Json),
                        Status = payByWallet ? "pending_tutor_approval" : "pending_payment",
                        PaymentStatus = payByWallet ? "completed" : "pending",
                        PaymentMethod = payByWallet ? TutorPaymentMethods.Wallet : TutorPaymentMethods.MobileMoney,
                        PaymentFeeXaf = payByWallet ? 0m : null,
                        Topics = topics.Select(t => new TutorBookingTopic { Label = t }).ToList(),
                        CreatedAt = now,
                    }).ToList(),
                };

                _context.TutorEngagements.Add(engagement);
                await _context.SaveChangesAsync();

                if (renewedFrom != null)
                {
                    renewedFrom.RenewedToEngagementId = engagement.Id;
                    renewedFrom.UpdatedAt = now;
                    await _context.SaveChangesAsync();
                }

                if (payByWallet)
                {
                    var payer = parties.PayerUserId;
                    var label = $"Engagement mensuel de tutorat du {engagement.StartDate:dd/MM/yyyy} au {engagement.EndDate:dd/MM/yyyy} ({engagement.Sessions.Count} séances)";
                    if (parties.PayerType == TutorPayerTypes.Parent)
                        await _wallet.RunLockedAsync(payer, async () =>
                        {
                            await _wallet.DebitParentWalletAsync(payer, engagement.TotalXaf, PaymentKey(engagement.Id), label, "TutorEngagement", engagement.Id);
                            return true;
                        });
                    else
                        await _wallet.RunLockedAsync(payer, () => _wallet.DebitAsync(new WalletEntry(
                            payer, WalletEntryTypes.TutoringPayment, engagement.TotalXaf, PaymentKey(engagement.Id),
                            label, "TutorEngagement", engagement.Id)));
                }

                await tx.CommitAsync();
            }
        }
        catch (Exception ex)
        {
            // Rien ne doit survivre d'une tentative refusée dans le suivi du
            // contexte (une tâche de fond enregistre ensuite d'autres
            // changements) : ajouts détachés, engagement renouvelé relu tel
            // qu'en base (la transaction est déjà annulée ici).
            foreach (var entry in _context.ChangeTracker.Entries().Where(x => x.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            if (renewedFrom != null)
                await _context.Entry(renewedFrom).ReloadAsync();
            if (IsSerializationFailure(ex))
                throw new InvalidOperationException("Un des créneaux vient d'être réservé par quelqu'un d'autre. Vérifie le devis et réessaie.");
            throw;
        }

        if (payByWallet)
        {
            await SyncSessionsAsync(engagement);
            await NotifyPaidAsync(engagement, profile);
            return new TutorEngagementCreatedResponseDto { Engagement = await GetByIdAsync(engagement.BookedByUserId, engagement.Id) };
        }

        // Mobile Money : un seul paiement NotchPay pour le mois.
        var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actingUserId);
        var email = actor?.Email ?? $"user{actingUserId}@winplus.cm";
        var name = $"{actor?.FirstName} {actor?.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(name)) name = email.Split('@')[0];
        try
        {
            var phone = PhoneNumbers.ToE164Digits(request.Phone!);
            var tutorName = $"{profile.User?.FirstName} {profile.User?.LastName}".Trim();
            var result = await _notchPay.InitiatePaymentAsync(phone, engagement.TotalXaf, engagement.Id,
                $"WinPlus - Engagement mensuel avec {tutorName}", email, name, PhoneNumbers.CameroonMobileMoneyChannel(phone), ReferencePrefix);
            engagement.NotchpayReference = result.Transaction?.Reference;
            engagement.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return new TutorEngagementCreatedResponseDto
            {
                Engagement = await GetByIdAsync(engagement.BookedByUserId, engagement.Id),
                NotchpayAuthorizationUrl = result.AuthorizationUrl,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec initiation NotchPay pour l'engagement mensuel {EngagementId}", engagement.Id);
            engagement.Status = TutorEngagementStatuses.Cancelled;
            engagement.PaymentStatus = "failed";
            engagement.CancellationReason = "Échec de l'initiation du paiement.";
            engagement.UpdatedAt = DateTime.UtcNow;
            foreach (var s in engagement.Sessions)
            {
                s.Status = "cancelled";
                s.PaymentStatus = "failed";
                s.CancellationReason = "Échec de l'initiation du paiement.";
                s.UpdatedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
            if (ex is InvalidOperationException rejection)
                throw new InvalidOperationException($"Le paiement a été refusé par l'opérateur mobile money. Vérifie ton numéro et l'opérateur choisi. ({rejection.Message})");
            throw new InvalidOperationException("Le service de paiement est momentanément indisponible. Réessaie dans quelques minutes.");
        }
    }

    private static bool IsSerializationFailure(Exception ex)
    {
        for (Exception? cur = ex; cur != null; cur = cur.InnerException)
            if (cur is PostgresException { SqlState: "40001" }) return true;
        return false;
    }

    private async Task SyncSessionsAsync(TutorEngagement engagement)
    {
        foreach (var s in engagement.Sessions)
        {
            try { await _wallet.SyncTutorBookingAsync(s.Id); }
            catch (Exception ex) { _logger.LogError(ex, "Écritures de la séance {BookingId} (engagement {EngagementId}) non posées (réconciliation à venir)", s.Id, engagement.Id); }
        }
    }

    // ── Lecture ─────────────────────────────────────────────────────────

    private async Task<int?> ViewerInstitutionAsync(int userId) =>
        await _context.Users.AsNoTracking().Where(u => u.Id == userId && u.Role == "institution").Select(u => u.InstitutionId).FirstOrDefaultAsync();

    private static bool IsPayerSide(TutorEngagement e, int userId, int? viewerInstitutionId) =>
        e.PayerUserId == userId || e.BookedByUserId == userId
        || (viewerInstitutionId != null && e.PayerInstitutionId == viewerInstitutionId);

    private static IEnumerable<int> ParticipantIds(TutorEngagement e)
    {
        var first = e.Sessions.OrderBy(s => s.Id).FirstOrDefault();
        var ids = first?.Participants.OrderBy(p => p.StudentUserId == e.StudentUserId ? 0 : 1).ThenBy(p => p.Id).Select(p => p.StudentUserId).ToList()
                  ?? new List<int>();
        if (ids.Count == 0) ids.Add(e.StudentUserId);
        return ids;
    }

    private static bool IsParticipant(TutorEngagement e, int userId) => ParticipantIds(e).Contains(userId);

    public async Task<TutorEngagementDto> GetByIdAsync(int userId, int engagementId)
    {
        var e = await WithDetails().AsNoTracking().FirstOrDefaultAsync(x => x.Id == engagementId)
            ?? throw new KeyNotFoundException("Engagement introuvable.");
        var viewerInstitution = await ViewerInstitutionAsync(userId);
        if (e.TutorProfile?.UserId != userId && !IsParticipant(e, userId) && !IsPayerSide(e, userId, viewerInstitution))
            throw new KeyNotFoundException("Engagement introuvable.");
        return (await MapManyAsync(new List<TutorEngagement> { e }, userId, viewerInstitution))[0];
    }

    public async Task<List<TutorEngagementDto>> GetMineAsync(int userId)
    {
        var viewerInstitution = await ViewerInstitutionAsync(userId);
        var list = await WithDetails().AsNoTracking()
            .Where(e => e.PayerUserId == userId || e.BookedByUserId == userId || e.StudentUserId == userId
                     || (viewerInstitution != null && e.PayerInstitutionId == viewerInstitution)
                     || e.Sessions.Any(s => s.Participants.Any(p => p.StudentUserId == userId)))
            .OrderByDescending(e => e.StartDate).ThenByDescending(e => e.Id)
            .Take(100)
            .ToListAsync();
        return await MapManyAsync(list, userId, viewerInstitution);
    }

    public async Task<List<TutorEngagementDto>> GetMineAsTutorAsync(int tutorUserId)
    {
        var list = await WithDetails().AsNoTracking()
            .Where(e => e.TutorProfile!.UserId == tutorUserId && e.Status != TutorEngagementStatuses.PendingPayment)
            .OrderByDescending(e => e.StartDate).ThenByDescending(e => e.Id)
            .Take(100)
            .ToListAsync();
        return await MapManyAsync(list, tutorUserId, null);
    }

    public async Task<List<TutorPendingEngagementDto>> GetPendingForTutorAsync(int tutorUserId)
    {
        var list = await WithDetails().AsNoTracking()
            .Where(e => e.TutorProfile!.UserId == tutorUserId && e.Status == TutorEngagementStatuses.PendingTutorApproval)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync();
        var dtos = await MapManyAsync(list, tutorUserId, null);
        var now = DateTime.UtcNow;
        return list.Select((e, i) => new TutorPendingEngagementDto
        {
            Engagement = dtos[i],
            MinutesRemaining = Math.Max(0, (int)((e.ResponseDeadlineAt ?? now) - now).TotalMinutes),
        }).ToList();
    }

    private static string? DisplayName(User? u) =>
        u == null ? null : ($"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.Email?.Split('@')[0]);

    private async Task<List<TutorEngagementDto>> MapManyAsync(List<TutorEngagement> list, int viewerUserId, int? viewerInstitutionId)
    {
        var payerIds = list.Select(e => e.PayerUserId).Distinct().ToList();
        var payers = await _context.Users.AsNoTracking().IgnoreQueryFilters().Where(u => payerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
        var institutionIds = list.Where(e => e.PayerInstitutionId != null).Select(e => e.PayerInstitutionId!.Value).Distinct().ToList();
        var institutions = institutionIds.Count == 0 ? new Dictionary<int, string>()
            : await _context.Institutions.AsNoTracking().IgnoreQueryFilters().Where(i => institutionIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name);
        var groupIds = list.Where(e => e.StudyGroupId != null).Select(e => e.StudyGroupId!.Value).Distinct().ToList();
        var groups = groupIds.Count == 0 ? new Dictionary<int, string>()
            : await _context.StudyGroups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name);
        return list.Select(e => MapToDto(e, viewerUserId, viewerInstitutionId, payers, institutions, groups)).ToList();
    }

    private TutorEngagementDto MapToDto(TutorEngagement e, int viewerUserId, int? viewerInstitutionId,
        IReadOnlyDictionary<int, User> payers, IReadOnlyDictionary<int, string> institutions, IReadOnlyDictionary<int, string> groups)
    {
        var now = DateTime.UtcNow;
        var isTutor = e.TutorProfile?.UserId == viewerUserId;
        var isPayer = IsPayerSide(e, viewerUserId, viewerInstitutionId);
        var isParticipant = IsParticipant(e, viewerUserId);
        var sessions = e.Sessions.OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime).ToList();
        var first = sessions.FirstOrDefault();
        var participants = first == null
            ? new List<TutorBookingParticipantDto>()
            : first.Participants.OrderBy(p => p.StudentUserId == e.StudentUserId ? 0 : 1).ThenBy(p => p.Id)
                .Select(p => new TutorBookingParticipantDto { UserId = p.StudentUserId, Name = DisplayName(p.Student), AvatarUrl = p.Student?.AvatarUrl }).ToList();
        var paid = string.Equals(e.PaymentStatus, "completed", StringComparison.OrdinalIgnoreCase);

        var sessionDtos = sessions.Select(s =>
        {
            var disputeDeadline = s.CompletedAt?.Add(_policy.DisputeWindow);
            var endUtc = s.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(s.EndTime), DateTimeKind.Utc);
            string money;
            if (!string.Equals(s.PaymentStatus, "completed", StringComparison.OrdinalIgnoreCase) && s.RefundedAt == null) money = "unpaid";
            else if (s.RefundedAt != null) money = s.TutorRetainedXaf is > 0 ? "partial" : "refunded";
            else if (s.EscrowReleasedAt != null) money = "released";
            else money = "held";
            return new TutorEngagementSessionDto
            {
                Id = s.Id,
                SessionDate = s.SessionDate,
                StartTime = TutorPricing.Hhmm(s.StartTime),
                EndTime = TutorPricing.Hhmm(s.EndTime),
                DurationMinutes = (int)(s.EndTime - s.StartTime).TotalMinutes,
                PriceXaf = s.PriceXaf,
                Status = s.Status,
                PaymentStatus = s.PaymentStatus,
                CompletedAt = s.CompletedAt,
                EscrowReleasedAt = s.EscrowReleasedAt,
                DisputedAt = s.DisputedAt,
                DisputeDeadline = s.Status == "completed" && s.EscrowReleasedAt == null ? disputeDeadline : null,
                RefundAmountXaf = s.RefundAmountXaf,
                RefundFeeXaf = s.RefundFeeXaf,
                RefundCause = s.RefundCause,
                RefundPercent = s.RefundPercent,
                TutorRetainedXaf = s.TutorRetainedXaf,
                MoneyState = money,
                CanMarkCompleted = isTutor && s.Status == "confirmed" && now >= endUtc,
                CanDispute = (isPayer || isParticipant) && s.Status == "completed" && disputeDeadline.HasValue && s.EscrowReleasedAt == null && now <= disputeDeadline.Value,
                CanCancel = (isPayer || isTutor) && Upcoming.Contains(s.Status) && StartUtc(s) > now,
            };
        }).ToList();

        // Argent du mois, séance par séance : libéré (au tuteur, commission
        // comprise), remboursé, frais retenus au payeur, et le reste bloqué.
        var released = sessions.Where(s => s.RefundedAt == null && s.EscrowReleasedAt != null).Sum(s => s.PriceXaf)
                     + sessions.Where(s => s.RefundedAt != null).Sum(s => s.TutorRetainedXaf ?? 0m);
        var refunded = sessions.Sum(s => s.RefundAmountXaf ?? 0m);
        var payerFees = sessions.Where(s => s.RefundFeeBorneBy == TutorFeeBearer.Payer).Sum(s => s.RefundFeeXaf ?? 0m);
        var held = paid ? Math.Max(0, e.TotalXaf - released - refunded - payerFees) : 0m;
        var nextStart = TutorMonthlyPlan.NextStartDate(e.StartDate);
        var renewable = e.Status is TutorEngagementStatuses.Active or TutorEngagementStatuses.PendingTutorApproval
                        && e.RenewedToEngagementId == null;

        string? payerName = e.PayerInstitutionId is int iid && institutions.TryGetValue(iid, out var instName)
            ? instName : payers.TryGetValue(e.PayerUserId, out var payer) ? DisplayName(payer) : null;

        return new TutorEngagementDto
        {
            Id = e.Id,
            TutorUserId = e.TutorProfile?.UserId ?? 0,
            TutorName = DisplayName(e.TutorProfile?.User),
            TutorAvatarUrl = e.TutorProfile?.User?.AvatarUrl,
            Status = e.Status,
            StartDate = e.StartDate,
            EndDate = e.EndDate,
            NextStartDate = nextStart,
            Weeks = e.Weeks,
            Slots = SlotDtos(e.Slots.Select(s => new TutorMonthlyPlan.Slot(s.DayOfWeek, s.StartTime, s.DurationMinutes))
                .OrderBy(s => (s.DayOfWeek + 6) % 7).ThenBy(s => s.Start)),
            Mode = e.Mode,
            Subject = e.Subject,
            Topics = ParseTopics(e.TopicsJson),
            PayerType = e.PayerType,
            PayerUserId = e.PayerUserId,
            PayerName = payerName,
            BookedByUserId = e.BookedByUserId,
            PayerInstitutionId = e.PayerInstitutionId,
            StudyGroupId = e.StudyGroupId,
            StudyGroupName = e.StudyGroupId is int gid && groups.TryGetValue(gid, out var gname) ? gname : null,
            TutorAssignmentId = e.TutorAssignmentId,
            ParticipantCount = e.ParticipantCount,
            Participants = participants,
            DiscountPercent = e.DiscountPercent,
            SubtotalXaf = e.SubtotalXaf,
            DiscountXaf = e.DiscountXaf,
            TotalXaf = e.TotalXaf,
            PaymentMethod = e.PaymentMethod,
            PaymentStatus = e.PaymentStatus,
            NotchpayReference = isPayer ? e.NotchpayReference : null,
            ReleasedXaf = released,
            HeldXaf = held,
            RefundedXaf = refunded,
            FeesRetainedXaf = payerFees,
            SessionsCount = sessions.Count,
            SessionsDone = sessions.Count(s => s.Status == "completed"),
            SessionsCancelled = sessions.Count(s => s.Status is "cancelled" or "rejected" or "expired"),
            Sessions = sessionDtos,
            ResponseDeadlineAt = e.ResponseDeadlineAt,
            AutoRenew = e.AutoRenew,
            CanSetAutoRenew = isPayer && renewable && TutorPaymentMethods.IsWallet(e.PaymentMethod),
            CanRenew = isPayer && e.Status == TutorEngagementStatuses.Active && e.RenewedToEngagementId == null,
            CanCancel = (isPayer || isTutor) && e.Status is TutorEngagementStatuses.PendingPayment or TutorEngagementStatuses.PendingTutorApproval or TutorEngagementStatuses.Active
                        && (e.Status != TutorEngagementStatuses.Active || sessions.Any(s => Upcoming.Contains(s.Status) && StartUtc(s) > now)),
            AutoRenewAt = TutorMonthlyPlan.AutoRenewAt(e.StartDate),
            AutoRenewAttemptedAt = e.AutoRenewAttemptedAt,
            AutoRenewFailureReason = e.AutoRenewFailureReason,
            RenewedFromEngagementId = e.RenewedFromEngagementId,
            RenewedToEngagementId = e.RenewedToEngagementId,
            CancellationReason = e.CancellationReason,
            CancelledAt = e.CancelledAt,
            ViewerIsPayer = isPayer,
            ViewerIsParticipant = isParticipant,
            ViewerIsTutor = isTutor,
            CreatedAt = e.CreatedAt,
        };
    }

    private static List<string> ParseTopics(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json, Json) ?? new List<string>(); }
        catch (JsonException) { return new List<string>(); }
    }

    // ── Décision du tuteur sur l'engagement entier ──────────────────────

    private async Task<TutorEngagement> LoadTrackedAsync(int engagementId) =>
        await _context.TutorEngagements
            .Include(e => e.TutorProfile)
            .Include(e => e.Slots)
            .Include(e => e.Sessions).ThenInclude(s => s.Participants)
            .Include(e => e.Sessions).ThenInclude(s => s.Topics)
            .AsSplitQuery()
            .FirstOrDefaultAsync(e => e.Id == engagementId)
        ?? throw new KeyNotFoundException("Engagement introuvable.");

    public async Task<TutorEngagementDto> AcceptAsync(int tutorUserId, int engagementId)
    {
        var e = await LoadTrackedAsync(engagementId);
        if (e.TutorProfile?.UserId != tutorUserId) throw new KeyNotFoundException("Engagement introuvable.");
        if (e.Status != TutorEngagementStatuses.PendingTutorApproval)
            throw new InvalidOperationException("Cette demande d'engagement n'est plus en attente.");
        var now = DateTime.UtcNow;
        if (e.ResponseDeadlineAt is DateTime deadline && now >= deadline)
            throw new InvalidOperationException("Le délai de réponse est dépassé : la demande va être annulée et le payeur remboursé.");

        // Lot 9.5 (17.17) : engagement présentiel jamais accepté par un tuteur
        // au présentiel suspendu : refusé d'office, remboursé intégralement.
        if (TutorInPersonRequestCloser.MustClose(e.TutorProfile, e.Mode))
        {
            await new TutorInPersonRequestCloser(_context, _refunds, _ntfy, _logger).CloseEngagementAsync(e.Id);
            throw new InvalidOperationException("Ton présentiel est suspendu : cet engagement présentiel ne peut pas être accepté. Il a été refusé et le payeur remboursé intégralement, sans frais pour toi.");
        }

        e.Status = TutorEngagementStatuses.Active;
        e.RespondedAt = now;
        e.UpdatedAt = now;
        foreach (var s in e.Sessions.Where(s => s.Status == "pending_tutor_approval"))
        {
            s.Status = "confirmed";
            s.ConfirmedAt = now;
            s.UpdatedAt = now;
        }
        await _context.SaveChangesAsync();

        await NotifyPayerSideAsync(e, new("Engagement mensuel confirmé !", "Monthly commitment confirmed!"),
            new($"Le répétiteur a accepté l'engagement du {e.StartDate:dd/MM/yyyy} au {e.EndDate:dd/MM/yyyy} : {e.Sessions.Count(s => s.Status == "confirmed")} séances réservées.",
                $"The tutor has accepted the commitment from {e.StartDate:dd/MM/yyyy} to {e.EndDate:dd/MM/yyyy}: {e.Sessions.Count(s => s.Status == "confirmed")} sessions booked."));
        return await GetByIdAsync(tutorUserId, e.Id);
    }

    public async Task<TutorEngagementDto> DeclineAsync(int tutorUserId, int engagementId, string? reason)
    {
        var e = await LoadTrackedAsync(engagementId);
        if (e.TutorProfile?.UserId != tutorUserId) throw new KeyNotFoundException("Engagement introuvable.");
        if (e.Status != TutorEngagementStatuses.PendingTutorApproval)
            throw new InvalidOperationException("Cette demande d'engagement n'est plus en attente.");

        var total = await CloseRequestAsync(e, TutorEngagementStatuses.Rejected, "rejected", TutorRefundCause.TutorDeclined,
            string.IsNullOrWhiteSpace(reason) ? "Refusé par le répétiteur." : Truncate(reason, 300));
        await NotifyPayerSideAsync(e, new("Engagement mensuel refusé", "Monthly commitment declined"),
            new($"Le répétiteur a refusé l'engagement du {e.StartDate:dd/MM/yyyy}. {total:0} XAF ont été crédités sur le portefeuille WinPlus du payeur (remboursement intégral).",
                $"The tutor has declined the commitment starting {e.StartDate:dd/MM/yyyy}. {total:0} XAF have been credited to the payer's WinPlus wallet (full refund)."));
        await NotifyTutorFeesAsync(e, new("refusé", "declined"));
        return await GetByIdAsync(tutorUserId, e.Id);
    }

    /// <summary>
    /// Refus ou absence de réponse : toutes les séances en attente sont
    /// refusées/expirées et remboursées intégralement, séance par séance
    /// (règle des frais du 9.2 : frais Mobile Money retenus sur le tuteur).
    /// Renvoie le total crédité au payeur.
    /// </summary>
    private async Task<decimal> CloseRequestAsync(TutorEngagement e, string engagementStatus, string sessionStatus, TutorRefundCause cause, string reason)
    {
        var now = DateTime.UtcNow;
        e.Status = engagementStatus;
        e.RespondedAt ??= now;
        e.AutoRenew = false;
        e.CancellationReason = reason;
        e.UpdatedAt = now;
        await _context.SaveChangesAsync();

        var total = 0m;
        foreach (var s in e.Sessions.Where(s => s.Status is "pending_tutor_approval" or "pending_payment").OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime))
        {
            s.Status = sessionStatus;
            s.CancellationReason = reason;
            s.UpdatedAt = now;
            var decision = await _refunds.RefundAsync(s, cause);
            total += decision?.RefundXaf ?? 0m;
        }
        await _context.SaveChangesAsync();
        return total;
    }

    // ── Annulation en cours de mois ─────────────────────────────────────

    public async Task<TutorEngagementDto> CancelAsync(int userId, int engagementId, string? reason)
    {
        var e = await LoadTrackedAsync(engagementId);
        var isTutor = e.TutorProfile?.UserId == userId;
        var isPayer = IsPayerSide(e, userId, await ViewerInstitutionAsync(userId));
        if (!isTutor && !isPayer)
        {
            if (IsParticipant(e, userId))
                throw new InvalidOperationException("Cet engagement a été réservé et payé par quelqu'un d'autre : demande-lui de l'annuler.");
            throw new KeyNotFoundException("Engagement introuvable.");
        }
        if (e.Status is not (TutorEngagementStatuses.PendingPayment or TutorEngagementStatuses.PendingTutorApproval or TutorEngagementStatuses.Active))
            throw new InvalidOperationException("Cet engagement ne peut plus être annulé.");

        var now = DateTime.UtcNow;
        var why = string.IsNullOrWhiteSpace(reason) ? null : Truncate(reason, 300);
        e.Status = TutorEngagementStatuses.Cancelled;
        e.CancelledAt = now;
        e.CancelledByUserId = userId;
        e.CancellationReason = why ?? (isTutor ? "Annulé par le répétiteur." : "Annulé par le payeur.");
        e.AutoRenew = false;
        e.UpdatedAt = now;

        if (e.PaymentStatus != "completed")
        {
            // Paiement Mobile Money pas encore encaissé : rien à rembourser ;
            // un encaissement tardif sera rendu au portefeuille (webhook).
            foreach (var s in e.Sessions.Where(s => SlotHolding.Contains(s.Status)))
            {
                s.Status = "cancelled";
                s.CancelledAt = now;
                s.CancelledByUserId = userId;
                s.CancellationReason = e.CancellationReason;
                s.UpdatedAt = now;
            }
            await _context.SaveChangesAsync();
            return await GetByIdAsync(userId, e.Id);
        }
        await _context.SaveChangesAsync();

        // Séances futures seulement : chacune est remboursée selon la politique
        // d'annulation du tuteur appliquée à SON heure de début (payeur), ou
        // intégralement (tuteur, frais retenus sur le tuteur). Les séances
        // passées restent dues et suivent leur propre cycle.
        var cause = isTutor ? TutorRefundCause.TutorCancelled : TutorRefundCause.PayerCancelled;
        var refunded = 0m;
        var count = 0;
        foreach (var s in e.Sessions.Where(s => Upcoming.Contains(s.Status) && StartUtc(s) > now).OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime))
        {
            s.Status = "cancelled";
            s.CancelledAt = now;
            s.CancelledByUserId = userId;
            s.CancellationReason = e.CancellationReason;
            s.UpdatedAt = now;
            var decision = await _refunds.RefundAsync(s, cause);
            refunded += decision?.RefundXaf ?? 0m;
            count++;
        }
        await _context.SaveChangesAsync();

        var who = isTutor ? "par le répétiteur" : "par le payeur";
        var whoEn = isTutor ? "by the tutor" : "by the payer";
        await NotifyPayerSideAsync(e, new("Engagement mensuel annulé", "Monthly commitment cancelled"),
            new($"L'engagement du {e.StartDate:dd/MM/yyyy} a été annulé {who} : {count} séance(s) à venir annulée(s), {refunded:0} XAF crédités sur le portefeuille WinPlus du payeur."
                + (isTutor ? " Remboursement intégral." : " Remboursement selon la politique d'annulation du répétiteur, séance par séance."),
                $"The commitment starting {e.StartDate:dd/MM/yyyy} has been cancelled {whoEn}: {count} upcoming session(s) cancelled, {refunded:0} XAF credited to the payer's WinPlus wallet."
                + (isTutor ? " Full refund." : " Refund under the tutor's cancellation policy, session by session.")), includeTutor: !isTutor);
        if (isTutor) await NotifyTutorFeesAsync(e, new("annulé", "cancelled"));
        return await GetByIdAsync(userId, e.Id);
    }

    /// <summary>
    /// Après l'annulation d'une séance isolée (TutorBookingService) : un
    /// engagement dont plus aucune séance n'est vivante ni effectuée passe à
    /// « cancelled » (plus rien à renouveler).
    /// </summary>
    public static async Task RefreshStatusAsync(ApplicationDbContext db, int engagementId)
    {
        var e = await db.TutorEngagements.Include(x => x.Sessions).FirstOrDefaultAsync(x => x.Id == engagementId);
        if (e == null || e.Status is not (TutorEngagementStatuses.Active or TutorEngagementStatuses.PendingTutorApproval)) return;
        if (e.Sessions.All(s => s.Status is "cancelled" or "rejected" or "expired"))
        {
            e.Status = TutorEngagementStatuses.Cancelled;
            e.CancelledAt ??= DateTime.UtcNow;
            e.AutoRenew = false;
            e.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    // ── Renouvellement ──────────────────────────────────────────────────

    public async Task<TutorEngagementDto> SetAutoRenewAsync(int userId, int engagementId, bool enabled)
    {
        var e = await LoadTrackedAsync(engagementId);
        if (!IsPayerSide(e, userId, await ViewerInstitutionAsync(userId)))
            throw new KeyNotFoundException("Engagement introuvable.");
        if (enabled)
        {
            if (!TutorPaymentMethods.IsWallet(e.PaymentMethod))
                throw new InvalidOperationException("Le renouvellement automatique n'est possible que pour un engagement payé par solde.");
            if (e.Status is not (TutorEngagementStatuses.Active or TutorEngagementStatuses.PendingTutorApproval) || e.RenewedToEngagementId != null)
                throw new InvalidOperationException("Cet engagement ne peut plus être renouvelé automatiquement.");
            if (DateTime.UtcNow >= TutorMonthlyPlan.AutoRenewAt(e.StartDate) && e.AutoRenewAttemptedAt != null)
                throw new InvalidOperationException("La tentative de renouvellement automatique de ce mois a déjà eu lieu : renouvelle manuellement.");
        }
        e.AutoRenew = enabled;
        e.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return await GetByIdAsync(userId, e.Id);
    }

    /// <summary>Requête de renouvellement : mêmes créneaux, mêmes participants, même payeur, période suivante.</summary>
    private static CreateTutorEngagementRequestDto RenewalRequest(TutorEngagement e, int tutorUserId, string paymentMethod, string? phone, bool autoRenew) => new()
    {
        TutorUserId = tutorUserId,
        StartDate = TutorMonthlyPlan.NextStartDate(e.StartDate),
        Slots = e.Slots.Select(s => new TutorEngagementSlotDto { DayOfWeek = s.DayOfWeek, StartTime = TutorPricing.Hhmm(s.StartTime), DurationMinutes = s.DurationMinutes }).ToList(),
        Mode = e.Mode,
        Subject = e.Subject,
        ParticipantCount = e.ParticipantCount,
        Topics = ParseTopics(e.TopicsJson),
        PaymentMethod = paymentMethod,
        Phone = phone,
        PayerType = e.PayerType,
        ParticipantUserIds = ParticipantIds(e).ToList(),
        StudyGroupId = e.StudyGroupId,
        TutorAssignmentId = e.TutorAssignmentId,
        AutoRenew = autoRenew,
    };

    private async Task<int?> ActiveAssignmentOrNullAsync(int? assignmentId) =>
        assignmentId is int id && await _context.TutorAssignments.AsNoTracking().AnyAsync(a => a.Id == id && a.Status == TutorAssignmentStatuses.Active)
            ? id : null;

    public async Task<TutorEngagementCreatedResponseDto> RenewAsync(int userId, int engagementId, RenewTutorEngagementRequestDto request)
    {
        var e = await LoadTrackedAsync(engagementId);
        if (!IsPayerSide(e, userId, await ViewerInstitutionAsync(userId)))
            throw new KeyNotFoundException("Engagement introuvable.");
        if (e.Status != TutorEngagementStatuses.Active)
            throw new InvalidOperationException("Seul un engagement en cours peut être renouvelé.");
        if (e.RenewedToEngagementId is int next
            && !await _context.TutorEngagements.AsNoTracking().AnyAsync(x => x.Id == next && TutorEngagementStatuses.Failed.Contains(x.Status)))
            throw new InvalidOperationException("Cet engagement a déjà été renouvelé.");
        var method = TutorPaymentMethods.IsWallet(request.PaymentMethod) ? TutorPaymentMethods.Wallet : TutorPaymentMethods.MobileMoney;
        var req = RenewalRequest(e, e.TutorProfile!.UserId, method, request.Phone, request.AutoRenew ?? (e.AutoRenew && method == TutorPaymentMethods.Wallet));
        req.TutorAssignmentId = await ActiveAssignmentOrNullAsync(e.TutorAssignmentId);
        e.RenewedToEngagementId = null;
        return await CreateCoreAsync(userId, req, renewedFrom: e);
    }

    // ── Paiement Mobile Money (webhook NotchPay « TEN- ») ───────────────

    public async Task<bool> TryHandleNotchPayWebhookAsync(string eventId, string eventType, NotchPayWebhookTransaction transaction)
    {
        if (string.IsNullOrEmpty(transaction.Reference) || !transaction.Reference.StartsWith($"{ReferencePrefix}-"))
            return false;

        var e = await _context.TutorEngagements
            .Include(x => x.TutorProfile)
            .Include(x => x.Slots)
            .Include(x => x.Sessions).ThenInclude(s => s.Participants)
            .Include(x => x.Sessions).ThenInclude(s => s.Topics)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.NotchpayReference == transaction.Reference);
        if (e == null)
        {
            _logger.LogWarning("Engagement mensuel introuvable pour la référence {Ref}", transaction.Reference);
            return true;
        }

        var wasFinal = e.PaymentStatus is "completed" or "failed";
        var status = MapNotchPayStatus(transaction.Status);

        if (!wasFinal && status == "completed" && transaction.Amount is decimal notified
            && decimal.Round(notified, 0, MidpointRounding.AwayFromZero) != decimal.Round(e.TotalXaf, 0, MidpointRounding.AwayFromZero))
        {
            _logger.LogError("Webhook NotchPay {Ref} rejeté : montant notifié {Notified} XAF ≠ total de l'engagement {Total} XAF (engagement {Id})",
                transaction.Reference, notified, e.TotalXaf, e.Id);
            try
            {
                await _ntfy.PublishAdminAsync("Écart de montant sur un paiement d'engagement mensuel",
                    $"Engagement #{e.Id} : montant notifié {notified:0} XAF, total {e.TotalXaf:0} XAF. L'engagement n'a pas été confirmé, vérification manuelle requise.",
                    tags: new[] { "rotating_light", "moneybag" });
            }
            catch (Exception ex) { _logger.LogError(ex, "Alerte d'écart de montant non envoyée (engagement {Id})", e.Id); }
            return true;
        }

        if (wasFinal || status == null || status == "pending") return true;
        var now = DateTime.UtcNow;

        if (status == "failed")
        {
            e.PaymentStatus = "failed";
            if (e.Status == TutorEngagementStatuses.PendingPayment) e.Status = TutorEngagementStatuses.Cancelled;
            e.CancellationReason ??= "Paiement échoué.";
            e.UpdatedAt = now;
            foreach (var s in e.Sessions.Where(s => s.Status == "pending_payment" || s.PaymentStatus == "pending"))
            {
                if (s.Status == "pending_payment") s.Status = "cancelled";
                s.PaymentStatus = "failed";
                s.CancellationReason ??= "Paiement échoué.";
                s.UpdatedAt = now;
            }
            await _context.SaveChangesAsync();
            return true;
        }

        // Encaissé : frais figés et répartis entre les séances (somme exacte).
        e.PaymentStatus = "completed";
        e.PaymentFeeXaf = TutorRefundRules.NotchPayFee(e.TotalXaf, _policy.NotchPayFeePercent);
        e.UpdatedAt = now;
        var ordered = e.Sessions.OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime).ToList();
        var shares = TutorMonthlyPlan.AllocateFee(e.PaymentFeeXaf.Value, ordered.Select(s => s.PriceXaf).ToList());
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].PaymentStatus = "completed";
            ordered[i].PaymentMethod = TutorPaymentMethods.MobileMoney;
            ordered[i].PaymentFeeXaf = shares[i];
            ordered[i].UpdatedAt = now;
        }

        if (e.Status != TutorEngagementStatuses.PendingPayment)
        {
            // Encaissement après l'annulation (ou l'abandon) du paiement : les
            // séances ne reprennent pas vie, le montant est rendu au
            // portefeuille du payeur, frais Mobile Money déduits.
            await _context.SaveChangesAsync();
            foreach (var s in ordered.Where(s => s.RefundedAt == null))
                await _refunds.RefundAsync(s, TutorRefundCause.PayerCancelled, overridePercent: 100);
            await SyncSessionsAsync(e);
            return true;
        }

        e.Status = TutorEngagementStatuses.PendingTutorApproval;
        foreach (var s in ordered.Where(s => s.Status == "pending_payment"))
            s.Status = "pending_tutor_approval";
        await _context.SaveChangesAsync();
        await SyncSessionsAsync(e);
        var profile = e.TutorProfile ?? await _context.TutorProfiles.FirstAsync(p => p.Id == e.TutorProfileId);
        // Lot 9.5 (17.17) : encaissé après la suspension du présentiel du
        // tuteur : refusé et remboursé intégralement tout de suite.
        if (TutorInPersonRequestCloser.MustClose(profile, e.Mode))
        {
            await new TutorInPersonRequestCloser(_context, _refunds, _ntfy, _logger).CloseEngagementAsync(e.Id);
            return true;
        }
        await NotifyPaidAsync(e, profile);
        return true;
    }

    // ── Tâches de fond ──────────────────────────────────────────────────

    public async Task<int> CancelAbandonedPaymentsAsync(DateTime nowUtc)
    {
        var threshold = nowUtc - AbandonedPaymentDelay;
        var list = await _context.TutorEngagements.Include(e => e.Sessions)
            .Where(e => e.Status == TutorEngagementStatuses.PendingPayment && e.CreatedAt <= threshold)
            .ToListAsync();
        foreach (var e in list)
        {
            e.Status = TutorEngagementStatuses.Cancelled;
            e.CancellationReason = "Paiement Mobile Money non confirmé : créneaux libérés.";
            e.CancelledAt = nowUtc;
            e.UpdatedAt = nowUtc;
            foreach (var s in e.Sessions.Where(s => s.Status == "pending_payment"))
            {
                s.Status = "cancelled";
                s.CancellationReason = e.CancellationReason;
                s.UpdatedAt = nowUtc;
            }
        }
        if (list.Count > 0) await _context.SaveChangesAsync();
        return list.Count;
    }

    public async Task<int> ExpireStaleRequestsAsync(DateTime nowUtc)
    {
        var ids = await _context.TutorEngagements.AsNoTracking()
            .Where(e => e.Status == TutorEngagementStatuses.PendingTutorApproval && e.ResponseDeadlineAt != null && e.ResponseDeadlineAt <= nowUtc)
            .Select(e => e.Id).ToListAsync();
        var done = 0;
        foreach (var id in ids)
        {
            try
            {
                var e = await LoadTrackedAsync(id);
                if (e.Status != TutorEngagementStatuses.PendingTutorApproval) continue;
                var total = await CloseRequestAsync(e, TutorEngagementStatuses.Expired, "expired", TutorRefundCause.TutorNoResponse,
                    "Le répétiteur n'a pas répondu dans son délai.");
                done++;
                await NotifyPayerSideAsync(e, new("Demande d'engagement expirée - remboursement effectué", "Commitment request expired - refund made"),
                    new($"Le répétiteur n'a pas répondu à temps pour l'engagement du {e.StartDate:dd/MM/yyyy}. {total:0} XAF ont été crédités sur le portefeuille WinPlus du payeur (remboursement intégral).",
                        $"The tutor did not reply in time for the commitment starting {e.StartDate:dd/MM/yyyy}. {total:0} XAF have been credited to the payer's WinPlus wallet (full refund)."),
                    includeTutor: true);
                await NotifyTutorFeesAsync(e, new("expiré", "expired"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Expiration de l'engagement {EngagementId} non enregistrée", id);
                _context.ChangeTracker.Clear();
            }
        }
        return done;
    }

    public async Task<int> CompleteFinishedAsync(DateTime nowUtc)
    {
        var today = DateOnly.FromDateTime(nowUtc);
        var list = await _context.TutorEngagements
            .Where(e => e.Status == TutorEngagementStatuses.Active && e.EndDate < today)
            .ToListAsync();
        foreach (var e in list)
        {
            e.Status = TutorEngagementStatuses.Completed;
            e.UpdatedAt = nowUtc;
        }
        if (list.Count > 0) await _context.SaveChangesAsync();
        return list.Count;
    }

    public async Task<int> SendRenewalRemindersAsync(DateTime nowUtc)
    {
        var candidates = await _context.TutorEngagements.Include(e => e.Slots).Include(e => e.TutorProfile).ThenInclude(p => p!.User)
            .Include(e => e.Sessions).ThenInclude(s => s.Participants)
            .AsSplitQuery()
            .Where(e => e.Status == TutorEngagementStatuses.Active && e.RenewalReminderSentAt == null && e.RenewedToEngagementId == null)
            .ToListAsync();
        var sent = 0;
        foreach (var e in candidates.Where(e => nowUtc >= TutorMonthlyPlan.RenewalReminderAt(e.StartDate)
                                             && DateOnly.FromDateTime(nowUtc) < TutorMonthlyPlan.NextStartDate(e.StartDate)))
        {
            e.RenewalReminderSentAt = nowUtc;
            await _context.SaveChangesAsync();
            sent++;

            var next = TutorMonthlyPlan.NextStartDate(e.StartDate);
            string amount = "";
            string amountEn = "";
            try
            {
                var q = await GetQuoteAsync(RenewalRequest(e, e.TutorProfile!.UserId, e.PaymentMethod, null, false));
                amount = $" Montant du mois suivant : {q.TotalXaf:0} XAF" + (q.IsAvailable ? "." : " (certains créneaux ne sont plus libres : vérifie avant de renouveler).");
                amountEn = $" Amount for next month: {q.TotalXaf:0} XAF" + (q.IsAvailable ? "." : " (some slots are no longer free: check before renewing).");
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Devis de renouvellement indisponible (engagement {Id})", e.Id); }

            var message = e.AutoRenew
                ? new LocalizedText(
                    $"Ton engagement mensuel avec {DisplayName(e.TutorProfile?.User)} se termine le {e.EndDate:dd/MM/yyyy}. Le renouvellement automatique sera tenté le {TutorMonthlyPlan.AutoRenewAt(e.StartDate):dd/MM/yyyy} sur ton solde WinPlus, pour les mêmes créneaux à partir du {next:dd/MM/yyyy}.{amount} Vérifie que ton solde est suffisant.",
                    $"Your monthly commitment with {DisplayName(e.TutorProfile?.User)} ends on {e.EndDate:dd/MM/yyyy}. Automatic renewal will be attempted on {TutorMonthlyPlan.AutoRenewAt(e.StartDate):dd/MM/yyyy} from your WinPlus balance, for the same slots from {next:dd/MM/yyyy}.{amountEn} Check that your balance is sufficient.")
                : new LocalizedText(
                    $"Ton engagement mensuel avec {DisplayName(e.TutorProfile?.User)} se termine le {e.EndDate:dd/MM/yyyy}. Renouvelle-le avec les mêmes créneaux à partir du {next:dd/MM/yyyy} depuis la page de l'engagement.{amount}",
                    $"Your monthly commitment with {DisplayName(e.TutorProfile?.User)} ends on {e.EndDate:dd/MM/yyyy}. Renew it with the same slots from {next:dd/MM/yyyy} on the commitment page.{amountEn}");
            await NotifyPayerOnlyAsync(e, new("Ton engagement mensuel arrive à échéance", "Your monthly commitment is ending soon"), message);
        }
        return sent;
    }

    public async Task<int> RunAutoRenewalsAsync(DateTime nowUtc)
    {
        var candidates = await _context.TutorEngagements.AsNoTracking()
            .Where(e => e.Status == TutorEngagementStatuses.Active && e.AutoRenew && e.AutoRenewAttemptedAt == null && e.RenewedToEngagementId == null
                     && e.PaymentMethod == TutorPaymentMethods.Wallet)
            .Select(e => new { e.Id, e.StartDate })
            .ToListAsync();
        var renewed = 0;
        foreach (var c in candidates.Where(c => nowUtc >= TutorMonthlyPlan.AutoRenewAt(c.StartDate)
                                             && DateOnly.FromDateTime(nowUtc) < TutorMonthlyPlan.NextStartDate(c.StartDate)))
        {
            var e = await LoadTrackedAsync(c.Id);
            if (e.AutoRenewAttemptedAt != null || e.RenewedToEngagementId != null) continue;
            // Marquée avant la tentative : jamais deux tentatives pour la même échéance.
            e.AutoRenewAttemptedAt = nowUtc;
            e.UpdatedAt = nowUtc;
            await _context.SaveChangesAsync();

            string? failure = null;
            TutorEngagementCreatedResponseDto? created = null;
            try
            {
                var req = RenewalRequest(e, e.TutorProfile!.UserId, TutorPaymentMethods.Wallet, null, autoRenew: true);
                req.TutorAssignmentId = await ActiveAssignmentOrNullAsync(e.TutorAssignmentId);
                // Mêmes contrôles qu'une réservation faite par le compte qui a
                // réservé (liens parent, rattachement institution, groupe).
                created = await CreateCoreAsync(e.BookedByUserId, req, renewedFrom: e);
            }
            catch (InsufficientWalletBalanceException ex)
            {
                failure = $"Solde insuffisant : {ex.AvailableXaf:0} XAF disponibles, {ex.RequiredXaf:0} XAF requis.";
            }
            catch (TutorEngagementConflictException ex)
            {
                failure = "Créneaux plus disponibles : " + string.Join(" ; ", ex.Conflicts.Take(3)) + (ex.Conflicts.Count > 3 ? "…" : "") + ".";
            }
            catch (InvalidOperationException ex)
            {
                failure = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Renouvellement automatique de l'engagement {Id} en échec technique", e.Id);
                failure = "Erreur technique lors du renouvellement.";
            }

            if (created != null)
            {
                renewed++;
                await NotifyPayerOnlyAsync(e, new("Engagement mensuel renouvelé", "Monthly commitment renewed"),
                    new($"Ton engagement a été renouvelé automatiquement à partir du {created.Engagement.StartDate:dd/MM/yyyy} : {created.Engagement.TotalXaf:0} XAF débités de ton solde. Le répétiteur doit encore accepter ; sinon tu seras remboursé intégralement.",
                        $"Your commitment has been renewed automatically from {created.Engagement.StartDate:dd/MM/yyyy}: {created.Engagement.TotalXaf:0} XAF debited from your balance. The tutor still has to accept; otherwise you will get a full refund."));
                continue;
            }

            var tracked = await _context.TutorEngagements.FirstAsync(x => x.Id == e.Id);
            tracked.AutoRenewFailureReason = Truncate(failure ?? "Renouvellement impossible.", 300);
            tracked.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await NotifyPayerOnlyAsync(e, new("Renouvellement automatique impossible", "Automatic renewal not possible"),
                new($"Ton engagement mensuel n'a pas pu être renouvelé automatiquement : {tracked.AutoRenewFailureReason} Tu peux le renouveler manuellement depuis la page de l'engagement avant le {TutorMonthlyPlan.NextStartDate(e.StartDate):dd/MM/yyyy}.",
                    $"Your monthly commitment could not be renewed automatically: {tracked.AutoRenewFailureReason} You can renew it manually on the commitment page before {TutorMonthlyPlan.NextStartDate(e.StartDate):dd/MM/yyyy}."));
        }
        return renewed;
    }

    // ── Notifications (une par événement de l'engagement, pas une par séance) ──

    private async Task NotifyPaidAsync(TutorEngagement e, TutorProfile profile)
    {
        var tutorUserId = profile.UserId;
        var slots = string.Join(", ", e.Slots.Select(s => $"{DayName(s.DayOfWeek)} {TutorPricing.Hhmm(s.StartTime)} ({s.DurationMinutes} min)"));
        var slotsEn = string.Join(", ", e.Slots.Select(s => $"{DayNameEn(s.DayOfWeek)} {TutorPricing.Hhmm(s.StartTime)} ({s.DurationMinutes} min)"));
        await SafePublishAsync(tutorUserId, new("Nouvelle demande d'engagement mensuel", "New monthly commitment request"),
            new($"{e.Sessions.Count} séances du {e.StartDate:dd/MM/yyyy} au {e.EndDate:dd/MM/yyyy} : {slots}. Accepte ou refuse l'engagement entier avant {e.ResponseDeadlineAt:dd/MM/yyyy HH:mm} (UTC).",
                $"{e.Sessions.Count} sessions from {e.StartDate:dd/MM/yyyy} to {e.EndDate:dd/MM/yyyy}: {slotsEn}. Accept or decline the whole commitment before {e.ResponseDeadlineAt:dd/MM/yyyy HH:mm} (UTC)."),
            e.Id, priority: "high");
        await NotifyPayerOnlyAsync(e, new("Paiement reçu - en attente du répétiteur", "Payment received - waiting for the tutor"),
            new($"L'engagement du {e.StartDate:dd/MM/yyyy} est payé ({e.TotalXaf:0} XAF) et en attente d'acceptation par le répétiteur. S'il refuse ou ne répond pas, tu es remboursé intégralement.",
                $"The commitment starting {e.StartDate:dd/MM/yyyy} is paid ({e.TotalXaf:0} XAF) and waiting for the tutor to accept it. If the tutor declines or does not reply, you get a full refund."));
        foreach (var id in ParticipantIds(e).Where(id => id != e.PayerUserId && id != e.BookedByUserId).Distinct())
            await SafePublishAsync(id, new("Un engagement mensuel a été réservé pour toi", "A monthly commitment has been booked for you"),
                new($"{e.Sessions.Count} séances du {e.StartDate:dd/MM/yyyy} au {e.EndDate:dd/MM/yyyy}, en attente d'acceptation du répétiteur. Retrouve-les dans Mes séances.",
                    $"{e.Sessions.Count} sessions from {e.StartDate:dd/MM/yyyy} to {e.EndDate:dd/MM/yyyy}, waiting for the tutor to accept. Find them in My sessions."), e.Id);
    }

    private async Task NotifyPayerOnlyAsync(TutorEngagement e, LocalizedText title, LocalizedText message)
    {
        var ids = new HashSet<int> { e.PayerUserId, e.BookedByUserId };
        foreach (var id in ids) await SafePublishAsync(id, title, message, e.Id);
    }

    private async Task NotifyPayerSideAsync(TutorEngagement e, LocalizedText title, LocalizedText message, bool includeTutor = false)
    {
        var ids = new HashSet<int>(ParticipantIds(e)) { e.PayerUserId, e.BookedByUserId };
        if (includeTutor && e.TutorProfile?.UserId is int t) ids.Add(t);
        foreach (var id in ids) await SafePublishAsync(id, title, message, e.Id);
    }

    private async Task NotifyTutorFeesAsync(TutorEngagement e, LocalizedText verb)
    {
        var fees = e.Sessions.Sum(s => s.TutorFeeChargedXaf ?? 0m);
        if (fees > 0 && e.TutorProfile?.UserId is int t)
            await SafePublishAsync(t, new("Frais Mobile Money retenus", "Mobile Money fees withheld"),
                new($"L'engagement du {e.StartDate:dd/MM/yyyy} a été {verb.Fr} de ton fait : le payeur est remboursé intégralement et les frais Mobile Money ({fees:0} XAF) ont été retenus sur ton solde, comme le prévoit la politique de tutorat.",
                    $"The commitment starting {e.StartDate:dd/MM/yyyy} was {verb.En} on your side: the payer gets a full refund and the Mobile Money fees ({fees:0} XAF) have been withheld from your balance, as set out in the tutoring policy."), e.Id);
    }

    /// <summary>Lot 10.6 : notification dans la langue du destinataire (User.Locale).</summary>
    private async Task SafePublishAsync(int userId, LocalizedText title, LocalizedText message, int engagementId, string priority = "default")
    {
        try
        {
            await _ntfy.PublishLocalizedAsync(_context, $"winplus-user-{userId}", title, message, priority: priority, userId: userId, type: "TutorBooking",
                relatedEntityType: "TutorEngagement", relatedEntityId: engagementId);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Notification d'engagement {Id} non envoyée au compte {User}", engagementId, userId); }
    }

    private static readonly string[] Days = { "dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi" };
    private static string DayName(int d) => d is >= 0 and <= 6 ? Days[d] : "?";
    private static readonly string[] DaysEn = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
    private static string DayNameEn(int d) => d is >= 0 and <= 6 ? DaysEn[d] : "?";

    // ── Outils ──────────────────────────────────────────────────────────

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static string? MapNotchPayStatus(string? s) => s?.ToLowerInvariant() switch
    {
        "complete" or "completed" or "success" or "successful" => "completed",
        "failed" or "failure" or "canceled" or "cancelled" or "rejected" or "expired" or "timeout" => "failed",
        "pending" or "processing" or "initiated" => "pending",
        _ => null,
    };
}
