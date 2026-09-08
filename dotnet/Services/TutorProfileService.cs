using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Gère le profil "Mode Répétiteur" (Module 1 — professeur_complete.md).
/// Une même colonne User.Role = "teacher" porte les deux modes d'exercice
/// (Professeur Catalogue et Répétiteur) : TutorProfile n'existe que si le
/// professeur a au moins commencé l'onboarding Répétiteur (Workflow 1).
/// </summary>
public class TutorProfileService : ITutorProfileService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TutorProfileService> _logger;

    public TutorProfileService(ApplicationDbContext context, ILogger<TutorProfileService> logger)
    {
        _context = context;
        _logger = logger;
    }

    private async Task<TutorProfile> GetOrCreateEntityAsync(int userId)
    {
        var profile = await _context.TutorProfiles
            .Include(p => p.Subjects)
            .Include(p => p.Levels)
            .Include(p => p.Specialties)
            .Include(p => p.InterventionZones)
            .Include(p => p.Packages)
            .Include(p => p.AvailabilitySlots)
            .Include(p => p.VerificationDocuments)
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile != null) return profile;

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("Utilisateur introuvable.");
        if (user.Role != "teacher")
            throw new InvalidOperationException("Seul un compte Professeur peut activer le Mode Répétiteur.");

        profile = new TutorProfile { UserId = userId };
        _context.TutorProfiles.Add(profile);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Double clic / double appel (ex. effet React invoqué deux fois) :
            // l'index unique sur UserId a rejeté la deuxième création
            // concurrente. Ce n'est pas une vraie erreur si la ligne existe
            // déjà — on l'utilise au lieu de renvoyer un 500 au client ;
            // sinon, l'échec était pour une autre raison, à relancer.
            _context.Entry(profile).State = EntityState.Detached;
            var existing = await _context.TutorProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            if (existing == null) throw;
            profile = existing;
        }

        // Recharge avec les collections vides mais initialisées (évite un null-check partout).
        profile.Subjects = new List<TutorSubject>();
        profile.Levels = new List<TutorLevel>();
        profile.Specialties = new List<TutorSpecialty>();
        profile.InterventionZones = new List<TutorInterventionZone>();
        profile.Packages = new List<TutorPackage>();
        profile.AvailabilitySlots = new List<TutorAvailabilitySlot>();
        profile.VerificationDocuments = new List<TutorVerificationDocument>();
        return profile;
    }

    public async Task<TutorProfileDto> GetOrCreateAsync(int userId)
    {
        var profile = await GetOrCreateEntityAsync(userId);
        return await MapToDtoAsync(profile);
    }

    public async Task<TutorOnboardingStatusDto> GetStatusAsync(int userId)
    {
        var profile = await _context.TutorProfiles.AsNoTracking()
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile == null)
            return new TutorOnboardingStatusDto { Started = false, OnboardingStep = 0, CompletionScore = 0, IsActive = false, MissingItems = new() };

        var hasPhoto = !string.IsNullOrWhiteSpace(profile.User?.AvatarUrl) || !string.IsNullOrWhiteSpace(profile.User?.ProfileImageUrl);
        var completion = ComputeCompletion(profile, hasPhoto);
        return new TutorOnboardingStatusDto
        {
            Started = true,
            OnboardingStep = profile.OnboardingStep,
            CompletionScore = completion.Score,
            IsActive = profile.IsActive,
            MissingItems = completion.MissingItems,
        };
    }

    public async Task<TutorProfileDto> UpdateAsync(int userId, UpdateTutorProfileRequestDto request)
    {
        var profile = await GetOrCreateEntityAsync(userId);

        // Tronqué systématiquement à la longueur de colonne : sans ça, un champ
        // trop long (copier-coller d'une bio depuis ailleurs, par ex.) ne
        // remontait pas une erreur de validation claire mais un 500 brut de
        // Postgres ("value too long for type character varying(n)").
        if (request.Title != null) profile.Title = Truncate(request.Title, 150);
        if (request.TutorBio != null) profile.TutorBio = Truncate(request.TutorBio, 300);
        if (request.VideoUrl != null) profile.VideoUrl = Truncate(request.VideoUrl, 500);
        if (request.TeachingStyle != null) profile.TeachingStyle = Truncate(request.TeachingStyle, 30);

        if (request.OffersAtStudentHome.HasValue) profile.OffersAtStudentHome = request.OffersAtStudentHome.Value;
        if (request.OffersAtTutorHome.HasValue) profile.OffersAtTutorHome = request.OffersAtTutorHome.Value;
        if (request.OffersOnline.HasValue) profile.OffersOnline = request.OffersOnline.Value;
        if (request.OffersNeutralPlace.HasValue) profile.OffersNeutralPlace = request.OffersNeutralPlace.Value;
        if (request.TutorHomeAddressHint != null) profile.TutorHomeAddressHint = Truncate(request.TutorHomeAddressHint, 200);

        if (request.HourlyRateXaf.HasValue) profile.HourlyRateXaf = request.HourlyRateXaf.Value;
        if (request.TrialSessionEnabled.HasValue) profile.TrialSessionEnabled = request.TrialSessionEnabled.Value;
        if (request.TrialSessionPriceXaf.HasValue) profile.TrialSessionPriceXaf = request.TrialSessionPriceXaf.Value;

        if (request.NoticeHours.HasValue) profile.NoticeHours = request.NoticeHours.Value is 12 or 24 or 48 ? request.NoticeHours.Value : 24;
        if (request.MaxSessionsPerWeek.HasValue) profile.MaxSessionsPerWeek = request.MaxSessionsPerWeek.Value;

        if (request.FullRefundHours.HasValue) profile.FullRefundHours = Math.Clamp(request.FullRefundHours.Value, 0, 168);
        if (request.PartialRefundPercent.HasValue) profile.PartialRefundPercent = Math.Clamp(request.PartialRefundPercent.Value, 0, 100);
        if (request.NoRefundHours.HasValue) profile.NoRefundHours = Math.Clamp(request.NoRefundHours.Value, 0, profile.FullRefundHours);

        if (request.OnboardingStep.HasValue) profile.OnboardingStep = Math.Clamp(request.OnboardingStep.Value, 0, 5);

        if (request.Subjects != null) ReplaceCollection(profile.Subjects, request.Subjects, s => new TutorSubject { TutorProfileId = profile.Id, Subject = Truncate(s, 100) });
        if (request.Levels != null) ReplaceCollection(profile.Levels, request.Levels, l => new TutorLevel { TutorProfileId = profile.Id, Level = Truncate(l, 100) });
        if (request.Specialties != null) ReplaceCollection(profile.Specialties, request.Specialties, s => new TutorSpecialty { TutorProfileId = profile.Id, Label = Truncate(s, 100) });

        if (request.InterventionZones != null)
        {
            _context.TutorInterventionZones.RemoveRange(profile.InterventionZones);
            profile.InterventionZones = request.InterventionZones
                .Select(z => new TutorInterventionZone
                {
                    TutorProfileId = profile.Id,
                    City = Truncate(z.City, 100),
                    Quartier = z.Quartier == null ? null : Truncate(z.Quartier, 100),
                })
                .ToList();
        }

        if (request.Packages != null)
        {
            _context.TutorPackages.RemoveRange(profile.Packages);
            profile.Packages = request.Packages
                .Take(3) // "jusqu'à 3 forfaits" (US-PRO-10)
                .Select(p => new TutorPackage { TutorProfileId = profile.Id, Name = Truncate(p.Name, 100), SessionsCount = p.SessionsCount, TotalPriceXaf = p.TotalPriceXaf, IsActive = p.IsActive })
                .ToList();
        }

        if (request.AvailabilitySlots != null)
            ReplaceAvailabilitySlots(profile, request.AvailabilitySlots);

        profile.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return await MapToDtoAsync(profile);
    }

    /// <summary>
    /// Sauvegarde instantanée de la grille de disponibilités (US-PRO-08),
    /// hors du flux d'onboarding par étape. Tant que le module Sessions
    /// (réservations réelles) n'existe pas, le plafond "séances max/semaine"
    /// est appliqué au nombre de créneaux actifs déclarés : au-delà, la
    /// grille refuse le créneau en trop plutôt que de le sauvegarder
    /// silencieusement.
    /// </summary>
    public async Task<TutorProfileDto> UpdateAvailabilityAsync(int userId, List<TutorAvailabilitySlotDto> slots)
    {
        var profile = await GetOrCreateEntityAsync(userId);

        var activeCount = slots.Count(s => s.IsActive);
        if (profile.MaxSessionsPerWeek is int max && activeCount > max)
            throw new InvalidOperationException($"Tu as atteint ta limite de {max} séance(s) par semaine : désactive un créneau avant d'en ajouter un autre, ou augmente ta limite dans tes paramètres.");

        ReplaceAvailabilitySlots(profile, slots);
        profile.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return await MapToDtoAsync(profile);
    }

    private void ReplaceAvailabilitySlots(TutorProfile profile, List<TutorAvailabilitySlotDto> slots)
    {
        _context.TutorAvailabilitySlots.RemoveRange(profile.AvailabilitySlots);
        profile.AvailabilitySlots = slots
            .Select(s => new TutorAvailabilitySlot
            {
                TutorProfileId = profile.Id,
                DayOfWeek = s.DayOfWeek,
                StartTime = TimeSpan.Parse(s.StartTime),
                EndTime = TimeSpan.Parse(s.EndTime),
                IsActive = s.IsActive,
            })
            .ToList();
    }

    public async Task<TutorProfileDto> ActivateAsync(int userId)
    {
        var profile = await GetOrCreateEntityAsync(userId);

        // Photo de profil obligatoire pour un répétiteur public (référentiel,
        // section "Identité et crédibilité") : pas d'initiales en avatar face
        // à un élève qui doit choisir entre plusieurs profils.
        var user = profile.User ?? await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        var hasPhoto = !string.IsNullOrWhiteSpace(user?.AvatarUrl) || !string.IsNullOrWhiteSpace(user?.ProfileImageUrl);
        if (!hasPhoto)
            throw new InvalidOperationException("Ajoute une photo de profil avant d'activer ton profil répétiteur : elle est obligatoire pour un profil public.");

        var completion = ComputeCompletion(profile, hasPhoto);
        if (completion.Score < 60)
            throw new InvalidOperationException("Complète au moins les matières, niveaux, un mode d'intervention et ton tarif horaire avant d'activer ton profil.");

        profile.IsActive = true;
        profile.OnboardingStep = 5;
        profile.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        _logger.LogInformation("Mode Répétiteur activé pour l'utilisateur {UserId}", userId);
        return await MapToDtoAsync(profile);
    }

    public async Task<TutorProfileDto> SetVacationAsync(int userId, bool isOnVacation)
    {
        var profile = await GetOrCreateEntityAsync(userId);
        profile.IsOnVacation = isOnVacation;
        profile.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return await MapToDtoAsync(profile);
    }

    public async Task<TutorVerificationDocumentDto> SubmitVerificationDocumentAsync(int userId, string documentUrl)
    {
        // Contrairement aux champs texte libre (tronqués silencieusement plus
        // haut), une URL tronquée serait cassée plutôt que raccourcie
        // proprement : on rejette avec un message clair au lieu de laisser
        // Postgres échouer sur "value too long for type character varying(500)".
        if (documentUrl.Length > 500)
            throw new InvalidOperationException("Le lien du document est invalide (trop long).");

        var profile = await GetOrCreateEntityAsync(userId);
        var doc = new TutorVerificationDocument
        {
            TutorProfileId = profile.Id,
            DocumentUrl = documentUrl,
            Status = "pending",
        };
        _context.TutorVerificationDocuments.Add(doc);
        await _context.SaveChangesAsync();
        return new TutorVerificationDocumentDto
        {
            Id = doc.Id,
            DocumentUrl = doc.DocumentUrl,
            Status = doc.Status,
            SubmittedAt = doc.SubmittedAt,
        };
    }

    public async Task<TutorProfileCompletionDto> GetCompletionAsync(int userId)
    {
        var profile = await GetOrCreateEntityAsync(userId);
        var hasPhoto = !string.IsNullOrWhiteSpace(profile.User?.AvatarUrl) || !string.IsNullOrWhiteSpace(profile.User?.ProfileImageUrl);
        return ComputeCompletion(profile, hasPhoto);
    }

    public async Task<TutorProfileDto?> GetPublicProfileAsync(int userId)
    {
        var profile = await _context.TutorProfiles
            .Include(p => p.Subjects).Include(p => p.Levels).Include(p => p.Specialties)
            .Include(p => p.InterventionZones).Include(p => p.Packages).Include(p => p.AvailabilitySlots)
            .FirstOrDefaultAsync(p => p.UserId == userId && p.IsActive);
        return profile == null ? null : await MapToDtoAsync(profile, isPublicView: true);
    }

    public async Task<List<TutorSearchResultDto>> SearchAsync(string? subject, string? level, decimal? maxHourlyRateXaf, bool verifiedOnly, int page, int pageSize, string? mode = null, string? city = null, bool availableSoon = false, double? minRating = null, string? sort = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _context.TutorProfiles
            .Include(p => p.Subjects).Include(p => p.Levels)
            .Include(p => p.InterventionZones).Include(p => p.AvailabilitySlots)
            .Include(p => p.User)
            .Where(p => p.IsActive && !p.IsOnVacation)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(subject))
            query = query.Where(p => p.Subjects.Any(s => s.Subject.ToLower() == subject.ToLower()));
        if (!string.IsNullOrWhiteSpace(level))
            query = query.Where(p => p.Levels.Any(l => l.Level.ToLower() == level.ToLower()));
        if (maxHourlyRateXaf.HasValue)
            query = query.Where(p => p.HourlyRateXaf != null && p.HourlyRateXaf <= maxHourlyRateXaf.Value);
        if (verifiedOnly)
            query = query.Where(p => p.IsDiplomaVerified);
        query = mode switch
        {
            "online" => query.Where(p => p.OffersOnline),
            "student_home" => query.Where(p => p.OffersAtStudentHome),
            "tutor_home" => query.Where(p => p.OffersAtTutorHome),
            "neutral_place" => query.Where(p => p.OffersNeutralPlace),
            _ => query,
        };
        if (!string.IsNullOrWhiteSpace(city))
            query = query.Where(p => p.InterventionZones.Any(z => z.City.ToLower() == city.ToLower()));

        var candidates = await query.ToListAsync();

        // "Disponibilité immédiate" (US-REP-01) : approximation par les créneaux
        // hebdo déclarés sur les 48h à venir, en respectant le préavis minimum.
        // Ne vérifie pas les réservations déjà prises sur ce créneau précis
        // (ça exigerait de croiser TutorBookings pour toute la page) — un
        // répétiteur peut donc apparaître "disponible" alors que ce créneau
        // exact est déjà pris ; la fermeture réelle a lieu au moment de réserver
        // (TutorBookingService).
        if (availableSoon)
        {
            var now = DateTime.UtcNow;
            candidates = candidates.Where(p =>
            {
                for (var d = 0; d < 2; d++)
                {
                    var date = DateOnly.FromDateTime(now).AddDays(d);
                    var dow = (int)date.DayOfWeek;
                    if (p.AvailabilitySlots.Any(s => s.IsActive && s.DayOfWeek == dow &&
                        date.ToDateTime(TimeOnly.FromTimeSpan(s.StartTime), DateTimeKind.Utc) >= now.AddHours(p.NoticeHours)))
                        return true;
                }
                return false;
            }).ToList();
        }

        // Tri par pertinence (note × volume de séances × réactivité, US-REP-01) —
        // calculé en batch pour éviter le N+1 sur la page de résultats.
        var candidateIds = candidates.Select(p => p.Id).ToList();
        var completedCounts = await _context.TutorBookings
            .Where(b => candidateIds.Contains(b.TutorProfileId) && b.Status == "completed")
            .GroupBy(b => b.TutorProfileId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        var respondedCounts = await _context.TutorBookings
            .Where(b => candidateIds.Contains(b.TutorProfileId) && RespondedStatuses.Contains(b.Status))
            .GroupBy(b => b.TutorProfileId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        var expiredCounts = await _context.TutorBookings
            .Where(b => candidateIds.Contains(b.TutorProfileId) && b.Status == "expired")
            .GroupBy(b => b.TutorProfileId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        var ratingStats = await _context.TutorReviews
            .Where(r => candidateIds.Contains(r.TutorProfileId))
            .GroupBy(r => r.TutorProfileId)
            .Select(g => new { g.Key, Avg = g.Average(r => (double)r.Rating), Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => (x.Avg, x.Count));

        // Note minimale (US-REP-01) : filtre après le calcul batch des notes,
        // pour ne pas refaire une requête par candidat.
        if (minRating.HasValue)
            candidates = candidates.Where(p => ratingStats.TryGetValue(p.Id, out var rs) && rs.Avg >= minRating.Value).ToList();

        double RelevanceScore(TutorProfile p)
        {
            var completed = completedCounts.GetValueOrDefault(p.Id);
            var responded = respondedCounts.GetValueOrDefault(p.Id);
            var expired = expiredCounts.GetValueOrDefault(p.Id);
            var sample = responded + expired;
            var reactivity = sample > 0 ? (double)responded / sample : 1.0; // neutre pour un profil sans historique
            var avgRating = ratingStats.TryGetValue(p.Id, out var rs) ? rs.Avg : 3.0; // neutre tant qu'il n'a pas d'avis
            return avgRating * (1 + completed) * reactivity;
        }

        // Prochain créneau réservable (14 jours, même logique honnête que
        // `availableSoon` : approximé depuis la grille déclarée, sans croiser
        // les réservations déjà prises pour ne pas exploser le coût de la page).
        DateTime? NextSlot(TutorProfile p)
        {
            var now = DateTime.UtcNow;
            for (var d = 0; d < 14; d++)
            {
                var date = DateOnly.FromDateTime(now).AddDays(d);
                var dow = (int)date.DayOfWeek;
                var slot = p.AvailabilitySlots
                    .Where(s => s.IsActive && s.DayOfWeek == dow)
                    .Select(s => date.ToDateTime(TimeOnly.FromTimeSpan(s.StartTime), DateTimeKind.Utc))
                    .Where(dt => dt >= now.AddHours(p.NoticeHours))
                    .OrderBy(dt => dt)
                    .FirstOrDefault();
                if (slot != default) return slot;
            }
            return null;
        }

        var nextSlots = candidates.ToDictionary(p => p.Id, NextSlot);

        var ordered = sort switch
        {
            "price-asc" => candidates.OrderBy(p => p.HourlyRateXaf ?? decimal.MaxValue).ToList(),
            "price-desc" => candidates.OrderByDescending(p => p.HourlyRateXaf ?? 0).ToList(),
            "next-slot" => candidates.OrderBy(p => nextSlots[p.Id] ?? DateTime.MaxValue).ToList(),
            _ => candidates
                .OrderByDescending(RelevanceScore)
                .ThenByDescending(p => p.IsDiplomaVerified)
                .ThenBy(p => p.HourlyRateXaf)
                .ToList(),
        };

        var results = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return results.Select(p => new TutorSearchResultDto
        {
            UserId = p.UserId,
            FullName = p.User != null ? $"{p.User.FirstName} {p.User.LastName}".Trim() : null,
            AvatarUrl = p.User?.AvatarUrl,
            Title = p.Title,
            HourlyRateXaf = p.HourlyRateXaf,
            IsDiplomaVerified = p.IsDiplomaVerified,
            AverageRating = ratingStats.TryGetValue(p.Id, out var pr) ? pr.Avg : null,
            ReviewCount = ratingStats.TryGetValue(p.Id, out var prc) ? prc.Count : 0,
            Subjects = p.Subjects.Select(s => s.Subject).ToList(),
            Levels = p.Levels.Select(l => l.Level).ToList(),
            NextAvailableSlot = nextSlots.GetValueOrDefault(p.Id),
        }).ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static void ReplaceCollection<TEntity>(ICollection<TEntity> current, List<string> values, Func<string, TEntity> factory) where TEntity : class
    {
        current.Clear();
        foreach (var v in values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase))
            current.Add(factory(v));
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private const int ExperiencedSessionThreshold = 10;
    private const int MinRespondedSampleForReactivity = 5;
    private const double HighlyResponsiveThreshold = 0.8;
    private static readonly string[] RespondedStatuses = { "confirmed", "rejected", "completed", "disputed" };

    /// <summary>
    /// Réputation réelle (Module 6/US-REP-08) : "Expérimenté" et "Très réactif"
    /// sont désormais calculés à partir des vraies réservations plutôt que
    /// hardcodés à false ; la note vient du module Avis (TutorReview).
    /// </summary>
    private async Task<(bool IsExperienced, bool IsHighlyResponsive, double? AverageRating, int ReviewCount)> ComputeReputationAsync(int tutorProfileId)
    {
        var completedCount = await _context.TutorBookings
            .CountAsync(b => b.TutorProfileId == tutorProfileId && b.Status == "completed");

        var respondedCount = await _context.TutorBookings
            .CountAsync(b => b.TutorProfileId == tutorProfileId && RespondedStatuses.Contains(b.Status));
        var expiredCount = await _context.TutorBookings
            .CountAsync(b => b.TutorProfileId == tutorProfileId && b.Status == "expired");
        var sample = respondedCount + expiredCount;
        var isHighlyResponsive = sample >= MinRespondedSampleForReactivity && (double)respondedCount / sample >= HighlyResponsiveThreshold;

        var ratings = await _context.TutorReviews
            .Where(r => r.TutorProfileId == tutorProfileId)
            .Select(r => r.Rating)
            .ToListAsync();

        return (
            completedCount >= ExperiencedSessionThreshold,
            isHighlyResponsive,
            ratings.Count == 0 ? null : ratings.Average(),
            ratings.Count
        );
    }

    /// <summary>
    /// isPublicView=true (fiche vue par un élève, GetPublicProfileAsync) masque
    /// explicitement les champs réservés au professeur propriétaire — plutôt
    /// que de compter sur le fait que GetPublicProfileAsync n'inclut pas
    /// VerificationDocuments : un futur Include ajouté là-bas sans y penser
    /// exposerait sinon le motif de refus de diplôme (donnée admin) à
    /// n'importe quel visiteur de la fiche publique.
    /// </summary>
    private async Task<TutorProfileDto> MapToDtoAsync(TutorProfile profile, bool isPublicView = false)
    {
        var user = profile.User ?? await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == profile.UserId);
        var latestDoc = isPublicView ? null : profile.VerificationDocuments
            .OrderByDescending(d => d.SubmittedAt)
            .FirstOrDefault();
        var reputation = await ComputeReputationAsync(profile.Id);

        return new TutorProfileDto
        {
            Id = profile.Id,
            UserId = profile.UserId,
            FullName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : null,
            AvatarUrl = user?.AvatarUrl ?? user?.ProfileImageUrl,
            Title = profile.Title,
            TutorBio = profile.TutorBio,
            VideoUrl = profile.VideoUrl,
            TeachingStyle = profile.TeachingStyle,
            HourlyRateXaf = profile.HourlyRateXaf,
            TrialSessionEnabled = profile.TrialSessionEnabled,
            TrialSessionPriceXaf = profile.TrialSessionPriceXaf,
            OffersAtStudentHome = profile.OffersAtStudentHome,
            OffersAtTutorHome = profile.OffersAtTutorHome,
            OffersOnline = profile.OffersOnline,
            OffersNeutralPlace = profile.OffersNeutralPlace,
            TutorHomeAddressHint = profile.TutorHomeAddressHint,
            NoticeHours = profile.NoticeHours,
            MaxSessionsPerWeek = profile.MaxSessionsPerWeek,
            IsOnVacation = profile.IsOnVacation,
            FullRefundHours = profile.FullRefundHours,
            PartialRefundPercent = profile.PartialRefundPercent,
            NoRefundHours = profile.NoRefundHours,
            IsDiplomaVerified = profile.IsDiplomaVerified,
            IsExperienced = reputation.IsExperienced,
            IsHighlyResponsive = reputation.IsHighlyResponsive,
            AverageRating = reputation.AverageRating,
            ReviewCount = reputation.ReviewCount,
            IsActive = profile.IsActive,
            // Progression d'onboarding et score de complétude : usage interne au
            // propriétaire du profil, sans intérêt (et sans raison d'être exposés)
            // pour un élève qui consulte la fiche publique.
            OnboardingStep = isPublicView ? 0 : profile.OnboardingStep,
            CompletionScore = isPublicView ? 0 : ComputeCompletion(profile, !string.IsNullOrWhiteSpace(user?.AvatarUrl) || !string.IsNullOrWhiteSpace(user?.ProfileImageUrl)).Score,
            Subjects = profile.Subjects.Select(s => s.Subject).ToList(),
            Levels = profile.Levels.Select(l => l.Level).ToList(),
            Specialties = profile.Specialties.Select(s => s.Label).ToList(),
            InterventionZones = profile.InterventionZones.Select(z => new TutorZoneDto { Id = z.Id, City = z.City, Quartier = z.Quartier }).ToList(),
            Packages = profile.Packages.Select(p => new TutorPackageDto { Id = p.Id, Name = p.Name, SessionsCount = p.SessionsCount, TotalPriceXaf = p.TotalPriceXaf, IsActive = p.IsActive }).ToList(),
            AvailabilitySlots = profile.AvailabilitySlots.Select(s => new TutorAvailabilitySlotDto
            {
                Id = s.Id,
                DayOfWeek = s.DayOfWeek,
                StartTime = s.StartTime.ToString(@"hh\:mm"),
                EndTime = s.EndTime.ToString(@"hh\:mm"),
                IsActive = s.IsActive,
            }).ToList(),
            PendingOrLatestDocument = latestDoc == null ? null : new TutorVerificationDocumentDto
            {
                Id = latestDoc.Id,
                DocumentUrl = latestDoc.DocumentUrl,
                Status = latestDoc.Status,
                RejectionReason = latestDoc.RejectionReason,
                SubmittedAt = latestDoc.SubmittedAt,
                ReviewedAt = latestDoc.ReviewedAt,
            },
            CreatedAt = profile.CreatedAt,
            UpdatedAt = profile.UpdatedAt,
        };
    }

    /// <summary>
    /// Score de complétude 0-100 (US-PRO-01, US-PRO-05). Pondération simple :
    /// chaque item manquant coûte des points, pensée pour que "matières +
    /// niveaux + au moins un mode d'intervention + tarif horaire" (le
    /// minimum viable pour apparaître dans une recherche utile) pèse la
    /// majorité du score.
    /// </summary>
    private static TutorProfileCompletionDto ComputeCompletion(TutorProfile profile, bool hasPhoto)
    {
        var missing = new List<TutorProfileMissingItemDto>();
        var score = 100;

        void Check(bool present, int weight, string field, string label)
        {
            if (!present)
            {
                score -= weight;
                missing.Add(new TutorProfileMissingItemDto { Field = field, Label = label });
            }
        }

        // Photo obligatoire pour activer (voir ActivateAsync) : listée en
        // premier dans les éléments manquants pour qu'elle soit visible tôt,
        // pas seulement au moment du refus d'activation.
        Check(hasPhoto, 10, "photo", "Photo de profil");
        Check(profile.Subjects.Count > 0, 20, "subjects", "Matières enseignées");
        Check(profile.Levels.Count > 0, 15, "levels", "Niveaux couverts");
        Check(profile.OffersAtStudentHome || profile.OffersAtTutorHome || profile.OffersOnline || profile.OffersNeutralPlace, 15, "interventionMode", "Au moins un mode d'intervention");
        Check(profile.HourlyRateXaf is > 0, 15, "hourlyRate", "Tarif horaire");
        Check(profile.AvailabilitySlots.Count > 0, 10, "availability", "Disponibilités hebdomadaires");
        Check(!string.IsNullOrWhiteSpace(profile.TutorBio), 10, "bio", "Bio courte");
        Check(!string.IsNullOrWhiteSpace(profile.Title), 5, "title", "Titre professionnel");
        Check(!string.IsNullOrWhiteSpace(profile.VideoUrl), 5, "video", "Vidéo d'introduction");

        return new TutorProfileCompletionDto { Score = Math.Max(0, score), MissingItems = missing };
    }
}
