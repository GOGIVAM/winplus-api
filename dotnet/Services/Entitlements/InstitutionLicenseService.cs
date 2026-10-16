using System.Linq.Expressions;
using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Entitlements;

/// <summary>
/// Règles chiffrées des licences d'établissement (étude 9.7b section 2.3,
/// décision 18.21). Seul endroit où elles vivent.
/// </summary>
public static class LicensePolicy
{
    public const int MinStudentSeats = 20;
    public const int StudentsPerTeacherSeat = 15;
    public const int MinTeacherSeats = 3;
    public const int TrialSeats = 30;
    public const int DefaultTrialDays = 30;
    public const int GraceDays = 3;
    public const int RenewalNoticeDays = 30;

    /// <summary>Bonus WinAI ajouté au plan personnel de chaque élève titulaire d'un siège.</summary>
    public const long StudentBonusTokens = 400_000;
    /// <summary>Référence WinAI minimale d'un enseignant titulaire d'un siège.</summary>
    public const long TeacherReferenceTokens = 1_000_000;
    /// <summary>Budget WinAI de l'établissement pour ses propres fonctions IA, par siège et par an.</summary>
    public const long AiBudgetPerSeatPerYear = 20_000;
    /// <summary>Part du professeur titulaire d'un siège enseignant : celle du plan Pro (18.21).</summary>
    public const int TeacherSeatRevenueSharePercent = 75;

    /// <summary>Prix TTC par élève et par an selon le volume.</summary>
    public static decimal YearlyPricePerSeat(int seats) => seats switch
    {
        >= 1000 => 3600,
        >= 200 => 4800,
        _ => 6000,
    };

    public static string TierFor(int seats) => seats switch
    {
        >= 1000 => LicenseTiers.Network,
        >= 200 => LicenseTiers.Establishment,
        _ => LicenseTiers.School,
    };

    public static int TeacherSeatsFor(int students) =>
        Math.Max(MinTeacherSeats, (int)Math.Ceiling(students / (double)StudentsPerTeacherSeat));

    /// <summary>
    /// Fin d'une licence commencée à <paramref name="start"/> : 31 juillet de
    /// l'année scolaire en cours (une licence prise en août court jusqu'au
    /// 31 juillet suivant), ou trois mois pour une licence trimestrielle.
    /// </summary>
    public static DateTime EndFor(string term, DateTime start)
    {
        if (term == LicenseTerms.Quarter) return start.AddMonths(3);
        var year = start.Month >= 8 ? start.Year + 1 : start.Year;
        return new DateTime(year, 7, 31, 23, 59, 59, DateTimeKind.Utc);
    }

    /// <summary>Prix par siège pour la période : l'année scolaire (11 mois) au tarif plein, le trimestre aux 3/11.</summary>
    public static decimal PricePerSeatFor(string term, decimal yearlyPerSeat) =>
        term == LicenseTerms.Quarter ? Math.Round(yearlyPerSeat * 3m / 11m, 0, MidpointRounding.AwayFromZero) : yearlyPerSeat;

    public static Expression<Func<InstitutionLicense, bool>> InForce(DateTime now) => l =>
        ((l.Status == LicenseStatuses.Active || l.Status == LicenseStatuses.Trialing) && l.StartDate <= now && l.EndDate > now)
        || (l.Status == LicenseStatuses.PastDue && l.GraceUntil != null && l.GraceUntil > now);
}

public sealed record LicenseQuote(
    int StudentSeats, int TeacherSeats, string Tier, string Term, decimal PricePerSeatTtc, decimal ListPriceTtc,
    decimal DiscountTtc, decimal AmountDueTtc, DateTime StartsAt, DateTime EndsAt, int? PromoCodeId, string? PromoError);

public sealed class LicenseException : Exception
{
    public int StatusCode { get; }
    public LicenseException(string message, int statusCode = 400) : base(message) { StatusCode = statusCode; }
}

/// <summary>
/// Licences d'établissement (lot 9.7b étape 4) : devis, souscription et
/// renouvellement payés par le portefeuille de l'établissement, licence
/// d'essai par promotion (une seule fois par établissement), licence négociée
/// créée par l'administration, sièges élèves (rattachement accepté) et
/// enseignants (affiliation acceptée), budget WinAI de l'établissement.
/// </summary>
public class InstitutionLicenseService
{
    private readonly ApplicationDbContext _db;
    private readonly IWalletService _wallet;
    private readonly IPromotionService _promotions;
    private readonly IEntitlementService _entitlements;

    public InstitutionLicenseService(ApplicationDbContext db, IWalletService wallet, IPromotionService promotions, IEntitlementService entitlements)
    {
        _db = db;
        _wallet = wallet;
        _promotions = promotions;
        _entitlements = entitlements;
    }

    /// <summary>Instance construite hors injection (contrôleurs instanciés directement, tâches).</summary>
    public static InstitutionLicenseService For(ApplicationDbContext db, IServiceProvider? services = null) =>
        (services?.GetService(typeof(InstitutionLicenseService)) as InstitutionLicenseService)
        ?? new InstitutionLicenseService(db, new WalletService(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<WalletService>.Instance),
            new PromotionService(db), new EntitlementService(db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())));

    public async Task<InstitutionLicense?> CurrentAsync(int institutionId, DateTime now, CancellationToken ct = default) =>
        await _db.Set<InstitutionLicense>().Include(l => l.Seats)
            .Where(l => l.InstitutionId == institutionId).Where(LicensePolicy.InForce(now))
            .OrderByDescending(l => l.EndDate).FirstOrDefaultAsync(ct);

    public async Task<LicenseQuote> QuoteAsync(int actingUserId, int institutionId, int students, string? term, string? promoCode, CancellationToken ct = default)
    {
        if (students < LicensePolicy.MinStudentSeats)
            throw new LicenseException($"Une licence compte au moins {LicensePolicy.MinStudentSeats} sièges élèves.");
        var t = term == LicenseTerms.Quarter ? LicenseTerms.Quarter : LicenseTerms.SchoolYear;
        var now = DateTime.UtcNow;
        var perSeat = LicensePolicy.PricePerSeatFor(t, LicensePolicy.YearlyPricePerSeat(students));
        var list = perSeat * students;
        var offer = await _promotions.QuoteAsync(actingUserId,
            new PromotionContext(PromoCodeScopes.InstitutionLicense, list, InstitutionId: institutionId), promoCode, ct);
        var discount = offer.Valid && offer.TrialDays == null ? offer.Discount : 0;
        return new LicenseQuote(students, LicensePolicy.TeacherSeatsFor(students), LicensePolicy.TierFor(students), t, perSeat, list,
            discount, Math.Max(0, list - discount), now, LicensePolicy.EndFor(t, now),
            discount > 0 ? offer.Promo?.Id : null, !string.IsNullOrWhiteSpace(promoCode) && !offer.Valid ? offer.Error : null);
    }

    private async Task<int> HolderAsync(int institutionId, int actingUserId) =>
        await InstitutionWallet.ResolveHolderAsync(_db, institutionId, actingUserId);

    /// <summary>Souscription payée par le portefeuille de l'établissement (une licence en vigueur à la fois ; un essai est remplacé).</summary>
    public async Task<InstitutionLicense> PurchaseAsync(int actingUserId, int institutionId, int students, string? term, string? promoCode, bool autoRenew, CancellationToken ct = default)
    {
        var q = await QuoteAsync(actingUserId, institutionId, students, term, promoCode, ct);
        if (q.PromoError != null) throw new LicenseException(q.PromoError);
        var now = DateTime.UtcNow;
        var current = await CurrentAsync(institutionId, now, ct);
        if (current != null && current.Status != LicenseStatuses.Trialing)
            throw new LicenseException("Une licence est déjà en cours : ajoutez des sièges ou attendez son renouvellement.", 409);

        var holder = await HolderAsync(institutionId, actingUserId);
        var license = new InstitutionLicense
        {
            InstitutionId = institutionId, Tier = q.Tier, Term = q.Term, Status = LicenseStatuses.Active,
            StartDate = now, EndDate = q.EndsAt, StudentSeats = students, TeacherSeats = q.TeacherSeats,
            PricePerSeatTtc = q.PricePerSeatTtc, TotalPriceTtc = q.AmountDueTtc, AutoRenew = autoRenew,
            AiBudgetTokens = AiBudget(students, q.Term), PromoCodeId = q.PromoCodeId, CreatedByUserId = actingUserId, CreatedAt = now,
        };
        try
        {
            await _wallet.RunLockedAsync(holder, async () =>
            {
                _db.Set<InstitutionLicense>().Add(license);
                await _db.SaveChangesAsync(ct);
                if (q.AmountDueTtc > 0)
                    await _wallet.DebitAsync(new WalletEntry(holder, WalletEntryTypes.InstitutionLicensePayment, q.AmountDueTtc,
                        $"institution_license:{license.Id}:{license.StartDate:yyyyMMdd}", $"Licence WinPlus ({students} élèves)",
                        "InstitutionLicense", license.Id, OwnerType: WalletOwnerTypes.Institution));
                if (current != null)
                {
                    // L'essai est remplacé : ses sièges passent sur la licence payée.
                    current.Status = LicenseStatuses.Cancelled;
                    current.EndDate = now;
                    foreach (var seat in current.Seats.Where(s => s.ReleasedAt == null))
                    {
                        seat.LicenseId = license.Id;
                    }
                }
                await _db.SaveChangesAsync(ct);
                return true;
            });
        }
        catch (InsufficientWalletBalanceException ex)
        {
            _db.Entry(license).State = EntityState.Detached;
            throw new LicenseException(ex.Message, 402);
        }
        if (q.PromoCodeId is int pid)
            await _promotions.RecordUsageAsync(pid, actingUserId, null, null, PromoCodeScopes.InstitutionLicense, q.DiscountTtc, ct);
        await AutoAssignAllAsync(license, ct);
        _entitlements.Invalidate();
        return license;
    }

    private static long AiBudget(int seats, string term) =>
        LicensePolicy.AiBudgetPerSeatPerYear * seats * (term == LicenseTerms.Quarter ? 3 : 11) / 11;

    /// <summary>Licence d'essai ouverte par une promotion « trial_days » visant les licences (une seule fois par établissement).</summary>
    public async Task<InstitutionLicense> StartTrialAsync(int actingUserId, int institutionId, string? promoCode, CancellationToken ct = default)
    {
        if (await _db.Set<InstitutionLicense>().AnyAsync(l => l.InstitutionId == institutionId, ct))
            throw new LicenseException("L'essai n'est ouvert qu'une fois, à un établissement qui n'a jamais eu de licence.", 409);
        var offer = await _promotions.QuoteAsync(actingUserId,
            new PromotionContext(PromoCodeScopes.InstitutionLicense, 0, InstitutionId: institutionId), promoCode, ct);
        if (!offer.Valid || offer.TrialDays is not int days)
            throw new LicenseException(offer.Error ?? "Aucune offre d'essai n'est disponible pour votre établissement.");
        var now = DateTime.UtcNow;
        var license = new InstitutionLicense
        {
            InstitutionId = institutionId, Tier = LicenseTiers.Trial, Term = LicenseTerms.Quarter, Status = LicenseStatuses.Trialing,
            StartDate = now, EndDate = now.AddDays(days), StudentSeats = LicensePolicy.TrialSeats,
            TeacherSeats = LicensePolicy.TeacherSeatsFor(LicensePolicy.TrialSeats), PricePerSeatTtc = 0, TotalPriceTtc = 0,
            AiBudgetTokens = LicensePolicy.AiBudgetPerSeatPerYear * LicensePolicy.TrialSeats * days / 330,
            PromoCodeId = offer.Promo!.Id, CreatedByUserId = actingUserId, CreatedAt = now,
        };
        _db.Set<InstitutionLicense>().Add(license);
        await _db.SaveChangesAsync(ct);
        await _promotions.RecordUsageAsync(offer.Promo.Id, actingUserId, null, null, PromoCodeScopes.InstitutionLicense, 0, ct);
        await AutoAssignAllAsync(license, ct);
        _entitlements.Invalidate();
        return license;
    }

    /// <summary>Licence négociée par l'administration (prix par siège libre, débit du portefeuille facultatif).</summary>
    public async Task<InstitutionLicense> AdminCreateAsync(int adminId, int institutionId, int students, int? teacherSeats, decimal pricePerSeat,
        DateTime? start, DateTime? end, bool chargeWallet, string? notes, CancellationToken ct = default)
    {
        if (!await _db.Institutions.AnyAsync(i => i.Id == institutionId && !i.IsDeleted, ct)) throw new LicenseException("Établissement introuvable.", 404);
        if (students < 1) throw new LicenseException("Nombre de sièges invalide.");
        if (pricePerSeat < 0) throw new LicenseException("Prix invalide.");
        var now = DateTime.UtcNow;
        var s = start ?? now;
        var e = end ?? LicensePolicy.EndFor(LicenseTerms.SchoolYear, s);
        if (e <= s) throw new LicenseException("La fin doit suivre le début.");
        if (await _db.Set<InstitutionLicense>().Where(l => l.InstitutionId == institutionId).AnyAsync(LicensePolicy.InForce(now), ct))
            throw new LicenseException("Cet établissement a déjà une licence en cours.", 409);
        var license = new InstitutionLicense
        {
            InstitutionId = institutionId, Tier = LicenseTiers.Negotiated, Term = (e - s).TotalDays > 120 ? LicenseTerms.SchoolYear : LicenseTerms.Quarter,
            Status = LicenseStatuses.Active, StartDate = s, EndDate = e, StudentSeats = students,
            TeacherSeats = teacherSeats ?? LicensePolicy.TeacherSeatsFor(students), PricePerSeatTtc = pricePerSeat,
            TotalPriceTtc = pricePerSeat * students, IsNegotiated = true,
            AiBudgetTokens = (long)(LicensePolicy.AiBudgetPerSeatPerYear * students * Math.Max(1, (e - s).TotalDays) / 330),
            CreatedByUserId = adminId, Notes = notes, CreatedAt = now,
        };
        _db.Set<InstitutionLicense>().Add(license);
        await _db.SaveChangesAsync(ct);
        if (chargeWallet && license.TotalPriceTtc > 0)
        {
            var holder = await _db.Institutions.Where(i => i.Id == institutionId).Select(i => i.WalletOwnerUserId).FirstOrDefaultAsync(ct)
                ?? throw new LicenseException("L'établissement n'a pas encore de portefeuille : créez la licence sans débit.");
            try
            {
                await _wallet.RunLockedAsync(holder, async () =>
                {
                    await _wallet.DebitAsync(new WalletEntry(holder, WalletEntryTypes.InstitutionLicensePayment, license.TotalPriceTtc,
                        $"institution_license:{license.Id}:{license.StartDate:yyyyMMdd}", $"Licence WinPlus négociée ({students} élèves)",
                        "InstitutionLicense", license.Id, CreatedByUserId: adminId, OwnerType: WalletOwnerTypes.Institution));
                    return true;
                });
            }
            catch (InsufficientWalletBalanceException ex)
            {
                _db.Set<InstitutionLicense>().Remove(license);
                await _db.SaveChangesAsync(ct);
                throw new LicenseException(ex.Message, 402);
            }
        }
        await AutoAssignAllAsync(license, ct);
        _entitlements.Invalidate();
        return license;
    }

    // ── Sièges ──────────────────────────────────────────────────────────────

    private async Task<bool> IsEligibleAsync(int institutionId, string kind, int userId, CancellationToken ct) =>
        kind == SeatKinds.Student
            ? await _db.InstitutionStudents.AnyAsync(s => s.InstitutionId == institutionId && s.StudentId == userId && s.IsActive
                                                        && s.ConfirmationStatus == InstitutionAttachmentStatuses.Accepted, ct)
            : await _db.InstitutionTeacherLinks.AnyAsync(l => l.InstitutionId == institutionId && l.TeacherId == userId && l.Status == "accepted", ct);

    /// <summary>
    /// Attribue un siège. Élève : rattachement accepté (17.14) ; enseignant :
    /// affiliation acceptée. Un siège libéré peut être réattribué une fois par
    /// période (au plus autant de libérations que de sièges).
    /// </summary>
    public async Task<InstitutionSeat> AssignSeatAsync(int institutionId, string kind, int userId, int? actorId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var license = await CurrentAsync(institutionId, now, ct) ?? throw new LicenseException("Aucune licence en cours.", 404);
        if (kind is not (SeatKinds.Student or SeatKinds.Teacher)) throw new LicenseException("Type de siège invalide.");
        if (!await IsEligibleAsync(institutionId, kind, userId, ct))
            throw new LicenseException(kind == SeatKinds.Student
                ? "L'élève doit d'abord accepter son rattachement à l'établissement."
                : "Le professeur doit d'abord être affilié à l'établissement.");
        var existing = license.Seats.FirstOrDefault(s => s.UserId == userId && s.Kind == kind && s.ReleasedAt == null);
        if (existing != null) return existing;
        var capacity = kind == SeatKinds.Student ? license.StudentSeats : license.TeacherSeats;
        var active = license.Seats.Count(s => s.Kind == kind && s.ReleasedAt == null);
        if (active >= capacity) throw new LicenseException("Tous les sièges de la licence sont attribués : ajoutez des sièges.", 409);
        var released = license.Seats.Count(s => s.Kind == kind && s.ReleasedAt != null);
        if (released > 0 && active + released >= capacity * 2)
            throw new LicenseException("Chaque siège ne peut être réattribué qu'une fois par période.", 409);
        var seat = new InstitutionSeat { LicenseId = license.Id, Kind = kind, UserId = userId, AssignedAt = now, AssignedByUserId = actorId };
        _db.Set<InstitutionSeat>().Add(seat);
        await _db.SaveChangesAsync(ct);
        _entitlements.Invalidate();
        return seat;
    }

    public async Task ReleaseSeatAsync(int institutionId, int seatId, CancellationToken ct = default)
    {
        var seat = await _db.Set<InstitutionSeat>().Include(s => s.License)
            .FirstOrDefaultAsync(s => s.Id == seatId && s.License!.InstitutionId == institutionId, ct)
            ?? throw new LicenseException("Siège introuvable.", 404);
        seat.ReleasedAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _entitlements.Invalidate();
    }

    /// <summary>Attribution automatique au rattachement accepté (élève) ou à l'affiliation acceptée (enseignant), s'il reste un siège.</summary>
    public async Task TryAutoAssignAsync(int institutionId, string kind, int userId, CancellationToken ct = default)
    {
        try { await AssignSeatAsync(institutionId, kind, userId, null, ct); }
        catch (LicenseException) { /* pas de licence, ou plus de siège : rien à faire */ }
    }

    private async Task AutoAssignAllAsync(InstitutionLicense license, CancellationToken ct)
    {
        var students = await _db.InstitutionStudents.AsNoTracking()
            .Where(s => s.InstitutionId == license.InstitutionId && s.IsActive && s.ConfirmationStatus == InstitutionAttachmentStatuses.Accepted)
            .OrderBy(s => s.RespondedAt ?? s.CreatedAt).Select(s => s.StudentId).ToListAsync(ct);
        foreach (var id in students) await TryAutoAssignAsync(license.InstitutionId, SeatKinds.Student, id, ct);
        var teachers = await _db.InstitutionTeacherLinks.AsNoTracking()
            .Where(l => l.InstitutionId == license.InstitutionId && l.Status == "accepted")
            .OrderBy(l => l.UpdatedAt).Select(l => l.TeacherId).ToListAsync(ct);
        foreach (var id in teachers) await TryAutoAssignAsync(license.InstitutionId, SeatKinds.Teacher, id, ct);
    }

    // ── Budget WinAI de l'établissement ─────────────────────────────────────

    public const string AiSourcePrefix = "inst:";

    public async Task<(long Budget, long Used)> AiBudgetAsync(int institutionId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var license = await CurrentAsync(institutionId, now, ct);
        if (license == null) return (0, 0);
        var userIds = await _db.Users.AsNoTracking().Where(u => u.InstitutionId == institutionId && u.Role == "institution").Select(u => u.Id).ToListAsync(ct);
        var used = await _db.AiTokenUsages.AsNoTracking()
            .Where(u => userIds.Contains(u.UserId) && u.Source.StartsWith(AiSourcePrefix) && u.CreatedAt >= license.StartDate)
            .SumAsync(u => (long?)u.TokensCharged, ct) ?? 0;
        return (license.AiBudgetTokens, used);
    }

    /// <summary>Inscrit le coût réel d'une fonction IA de l'établissement sur son budget.</summary>
    public async Task RecordAiUsageAsync(int userId, string feature, int tokens, CancellationToken ct = default)
    {
        if (tokens <= 0) return;
        var source = (AiSourcePrefix + feature);
        _db.AiTokenUsages.Add(new AiTokenUsage
        {
            UserId = userId, ClientMessageId = Guid.NewGuid().ToString("N"), TokensCharged = tokens, IsFinalized = true,
            Source = source.Length > 20 ? source[..20] : source, CreatedAt = DateTime.UtcNow, FinalizedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
    }

    // ── Renouvellement ──────────────────────────────────────────────────────

    private void Notify(int userId, string title, string message, int licenseId) =>
        _db.Notifications.Add(new Notification
        {
            UserId = userId, Title = title, Message = message, Type = "Subscription",
            RelatedEntityType = "InstitutionLicense", RelatedEntityId = licenseId, CreatedAt = DateTime.UtcNow, User = null!,
        });

    private async Task NotifyInstitutionAsync(int institutionId, string title, string message, int licenseId, CancellationToken ct)
    {
        var ids = await _db.Users.AsNoTracking().Where(u => u.InstitutionId == institutionId && u.Role == "institution" && !u.IsDeleted)
            .Select(u => u.Id).ToListAsync(ct);
        foreach (var id in ids) Notify(id, title, message, licenseId);
    }

    /// <summary>
    /// Préavis de 30 jours, renouvellement par le portefeuille de
    /// l'établissement (mêmes sièges ; prix du palier, ou prix négocié
    /// conservé), 3 jours de grâce puis expiration ; fin des essais.
    /// </summary>
    public async Task<(int Renewed, int InGrace, int Expired)> RenewDueAsync(DateTime now, CancellationToken ct = default)
    {
        int renewed = 0, grace = 0, expired = 0;
        var licenses = await _db.Set<InstitutionLicense>()
            .Where(l => l.Status == LicenseStatuses.Active || l.Status == LicenseStatuses.Trialing || l.Status == LicenseStatuses.PastDue)
            .ToListAsync(ct);
        foreach (var l in licenses)
        {
            if (l.EndDate > now)
            {
                if (l.Status == LicenseStatuses.Active && l.EndDate <= now.AddDays(LicensePolicy.RenewalNoticeDays) && l.RenewalNoticeSentAt == null)
                {
                    l.RenewalNoticeSentAt = now;
                    await NotifyInstitutionAsync(l.InstitutionId, "Licence WinPlus : échéance dans 30 jours",
                        l.AutoRenew
                            ? $"Votre licence sera renouvelée le {l.EndDate:dd/MM/yyyy} avec le solde de l'établissement ({l.TotalPriceTtc:0} XAF environ). Vérifiez votre solde."
                            : $"Votre licence prend fin le {l.EndDate:dd/MM/yyyy}. Renouvelez-la pour garder les sièges de vos élèves et enseignants.", l.Id, ct);
                }
                continue;
            }
            if (l.Status == LicenseStatuses.PastDue && l.GraceUntil is DateTime gu && gu <= now)
            {
                l.Status = LicenseStatuses.Expired;
                await NotifyInstitutionAsync(l.InstitutionId, "Licence WinPlus expirée", "Le renouvellement n'a pas pu être payé : la licence a pris fin.", l.Id, ct);
                expired++;
                continue;
            }
            if (l.Status == LicenseStatuses.Trialing || !l.AutoRenew || l.CancelAtPeriodEnd)
            {
                l.Status = l.CancelAtPeriodEnd ? LicenseStatuses.Cancelled : LicenseStatuses.Expired;
                await NotifyInstitutionAsync(l.InstitutionId, "Licence WinPlus terminée", "Votre licence a pris fin. Souscrivez une licence pour rétablir les sièges.", l.Id, ct);
                expired++;
                continue;
            }
            var holder = await _db.Institutions.Where(i => i.Id == l.InstitutionId).Select(i => i.WalletOwnerUserId).FirstOrDefaultAsync(ct);
            var perSeat = l.IsNegotiated ? l.PricePerSeatTtc : LicensePolicy.PricePerSeatFor(l.Term, LicensePolicy.YearlyPricePerSeat(l.StudentSeats));
            var amount = perSeat * l.StudentSeats;
            var newEnd = l.Term == LicenseTerms.Quarter ? l.EndDate.AddMonths(3) : LicensePolicy.EndFor(LicenseTerms.SchoolYear, l.EndDate.AddDays(1));
            try
            {
                if (holder is not int h) throw new InsufficientWalletBalanceException(0, amount);
                await _wallet.RunLockedAsync(h, async () =>
                {
                    if (amount > 0)
                        await _wallet.DebitAsync(new WalletEntry(h, WalletEntryTypes.InstitutionLicensePayment, amount,
                            $"institution_license:{l.Id}:{l.EndDate:yyyyMMdd}", $"Renouvellement licence WinPlus ({l.StudentSeats} élèves)",
                            "InstitutionLicense", l.Id, OwnerType: WalletOwnerTypes.Institution));
                    return true;
                });
                l.StartDate = l.EndDate;
                l.EndDate = newEnd;
                l.PricePerSeatTtc = perSeat;
                l.TotalPriceTtc = amount;
                l.Status = LicenseStatuses.Active;
                l.GraceUntil = null;
                l.RenewalNoticeSentAt = null;
                l.AiBudgetTokens = AiBudget(l.StudentSeats, l.Term);
                await NotifyInstitutionAsync(l.InstitutionId, "Licence WinPlus renouvelée", $"Licence renouvelée jusqu'au {newEnd:dd/MM/yyyy} : {amount:0} XAF débités.", l.Id, ct);
                renewed++;
            }
            catch (InsufficientWalletBalanceException)
            {
                if (l.Status != LicenseStatuses.PastDue)
                {
                    l.Status = LicenseStatuses.PastDue;
                    l.GraceUntil = l.EndDate.AddDays(LicensePolicy.GraceDays);
                    await NotifyInstitutionAsync(l.InstitutionId, "Renouvellement de licence en attente",
                        $"Le solde de l'établissement ne couvre pas le renouvellement ({amount:0} XAF). Les sièges restent actifs jusqu'au {l.GraceUntil:dd/MM/yyyy} : rechargez le portefeuille.", l.Id, ct);
                }
                grace++;
            }
            l.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(ct);
        if (renewed + grace + expired > 0) _entitlements.Invalidate();
        return (renewed, grace, expired);
    }
}

/// <summary>
/// Donateur de droits « siège de licence » (étude 2.3, décision 18.21) :
///   - élève titulaire d'un siège (rattachement accepté) : catalogue, plan
///     d'étude, session guidée, Exam Coach, bonus WinAI de 400 000 tokens ;
///   - enseignant titulaire d'un siège (affiliation acceptée) : droits de
///     classe du plan Pro, génération de quiz, correction assistée, référence
///     WinAI d'au moins 1 000 000 tokens, et part du plan Pro (75 %) si son
///     plan personnel est inférieur.
/// </summary>
public class LicenseEntitlementDonor : IEntitlementDonor
{
    public async Task<EntitlementDonation?> DonateAsync(ApplicationDbContext db, int userId, string role, DateTime now, CancellationToken ct)
    {
        var kind = role == "teacher" ? SeatKinds.Teacher : role == "student" ? SeatKinds.Student : null;
        if (kind == null) return null;
        var seat = await db.Set<InstitutionSeat>().AsNoTracking()
            .Where(s => s.UserId == userId && s.Kind == kind && s.ReleasedAt == null)
            .Select(s => new { s.LicenseId, s.License!.InstitutionId, s.License.Status, s.License.StartDate, s.License.EndDate, s.License.GraceUntil })
            .ToListAsync(ct);
        foreach (var s in seat)
        {
            var inForce = ((s.Status == LicenseStatuses.Active || s.Status == LicenseStatuses.Trialing) && s.StartDate <= now && s.EndDate > now)
                          || (s.Status == LicenseStatuses.PastDue && s.GraceUntil > now);
            if (!inForce) continue;
            var stillLinked = kind == SeatKinds.Student
                ? await db.InstitutionStudents.AsNoTracking().AnyAsync(x => x.InstitutionId == s.InstitutionId && x.StudentId == userId && x.IsActive
                                                                          && x.ConfirmationStatus == InstitutionAttachmentStatuses.Accepted, ct)
                : await db.InstitutionTeacherLinks.AsNoTracking().AnyAsync(x => x.InstitutionId == s.InstitutionId && x.TeacherId == userId && x.Status == "accepted", ct);
            if (!stillLinked) continue;
            var label = $"Siège de licence de l'établissement {s.InstitutionId}";
            if (kind == SeatKinds.Student)
                return new EntitlementDonation("license", label, s.LicenseId, new Dictionary<string, string>
                {
                    [EntitlementCatalog.CatalogAccess] = "true",
                    [EntitlementCatalog.AiStudyPlan] = "true",
                    [EntitlementCatalog.AiStudySession] = "true",
                    [EntitlementCatalog.AiExamCoach] = "true",
                }, BonusTokens: LicensePolicy.StudentBonusTokens);
            return new EntitlementDonation("license", label, s.LicenseId, new Dictionary<string, string>
            {
                [EntitlementCatalog.TeacherMaxClasses] = "10",
                [EntitlementCatalog.TeacherMaxStudentsPerClass] = "100",
                [EntitlementCatalog.AiQuizGeneration] = "true",
                [EntitlementCatalog.AiCorrectionAssist] = "true",
                [EntitlementCatalog.TeacherRevenueShare] = LicensePolicy.TeacherSeatRevenueSharePercent.ToString(),
            }, ReferenceTokensFloor: LicensePolicy.TeacherReferenceTokens);
        }
        return null;
    }
}
