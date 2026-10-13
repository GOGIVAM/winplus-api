using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Gère le profil "Mode Répétiteur" (Module 1  professeur_complete.md).
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
            .Include(p => p.Rates)
            .Include(p => p.RateModifiers)
            .Include(p => p.DurationDiscounts)
            .Include(p => p.GroupDiscounts)
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
            // déjà  on l'utilise au lieu de renvoyer un 500 au client ;
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
        profile.Rates = new List<TutorRate>();
        profile.RateModifiers = new List<TutorRateModifier>();
        profile.DurationDiscounts = new List<TutorDurationDiscount>();
        profile.GroupDiscounts = new List<TutorGroupDiscount>();
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
            .Include(p => p.Rates)
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

        // Lot 9.5 (17.17) : un tuteur non vérifié (ou au présentiel suspendu)
        // ENREGISTRE ses choix présentiels, qui restent inactifs (non proposés,
        // non réservables, non affichés publiquement : TutorInPersonPolicy)
        // tant qu'il n'est pas vérifié, puis redeviennent actifs d'eux-mêmes à
        // la vérification ou à la levée de la suspension. Plus de refus ici :
        // enregistrer l'étape « Intervention » ne fait plus perdre ces choix.
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

        // Lot 9.2 (17.10) : réglages d'annulation validés dans les limites
        // plateforme (TutorPolicy), refusés avec un message clair au lieu
        // d'être ramenés silencieusement. Validés seulement quand l'un d'eux
        // est envoyé : enregistrer une autre étape du profil ne bloque jamais.
        if (request.FullRefundHours.HasValue || request.PartialRefundPercent.HasValue || request.NoRefundHours.HasValue)
        {
            var current = TutorPolicy.Effective(profile);
            var full = request.FullRefundHours ?? current.FullRefundHours;
            var partial = request.PartialRefundPercent ?? current.PartialRefundPercent;
            var none = request.NoRefundHours ?? current.NoRefundHours;
            TutorPolicy.Validate(full, partial, none);
            profile.FullRefundHours = full;
            profile.PartialRefundPercent = partial;
            profile.NoRefundHours = none;
        }

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
            // Forfaits historiques (inertes) des anciens clients : seules les
            // lignes « bundle » sont remplacées, jamais l'offre mensuelle (lot 9.4).
            var bundles = profile.Packages.Where(p => p.Kind != TutorPackageKinds.Monthly).ToList();
            _context.TutorPackages.RemoveRange(bundles);
            foreach (var b in bundles) profile.Packages.Remove(b);
            foreach (var p in request.Packages.Take(3)) // "jusqu'à 3 forfaits" (US-PRO-10)
                profile.Packages.Add(new TutorPackage { TutorProfileId = profile.Id, Name = Truncate(p.Name, 100), SessionsCount = p.SessionsCount, TotalPriceXaf = p.TotalPriceXaf, IsActive = p.IsActive, Kind = TutorPackageKinds.Bundle });
        }

        // Lot 9.4 (suivi 17.7) : offre d'engagement mensuel, remise bornée côté serveur.
        if (request.MonthlyEnabled.HasValue || request.MonthlyDiscountPercent.HasValue)
        {
            if (request.MonthlyDiscountPercent is int pct && (pct < 0 || pct > TutorPackageKinds.MaxMonthlyDiscountPercent))
                throw new InvalidOperationException($"La remise mensuelle est comprise entre 0 % et {TutorPackageKinds.MaxMonthlyDiscountPercent} %.");
            var monthly = profile.Packages.FirstOrDefault(p => p.Kind == TutorPackageKinds.Monthly);
            if (monthly == null)
            {
                monthly = new TutorPackage { TutorProfileId = profile.Id, Name = "Engagement mensuel", Kind = TutorPackageKinds.Monthly, IsActive = false };
                profile.Packages.Add(monthly);
            }
            if (request.MonthlyEnabled.HasValue) monthly.IsActive = request.MonthlyEnabled.Value;
            if (request.MonthlyDiscountPercent.HasValue) monthly.DiscountPercent = request.MonthlyDiscountPercent.Value;
            monthly.UpdatedAt = DateTime.UtcNow;
        }

        if (request.AvailabilitySlots != null)
            ReplaceAvailabilitySlots(profile, request.AvailabilitySlots);

        // Grille tarifaire (lot 9.1, 17.3) : validée entièrement avant toute
        // écriture, pour qu'une règle invalide ne laisse pas une grille à moitié
        // remplacée. N'affecte jamais le PriceXaf des réservations existantes.
        if (request.Rates != null)
        {
            var rates = TutorPricing.ValidateRates(request.Rates, profile.Id);
            _context.TutorRates.RemoveRange(profile.Rates);
            profile.Rates = rates;
        }
        if (request.RateModifiers != null)
        {
            var modifiers = TutorPricing.ValidateModifiers(request.RateModifiers, profile.Id);
            _context.TutorRateModifiers.RemoveRange(profile.RateModifiers);
            profile.RateModifiers = modifiers;
        }
        if (request.DurationDiscounts != null)
        {
            var discounts = TutorPricing.ValidateDurationDiscounts(request.DurationDiscounts, profile.Id);
            _context.TutorDurationDiscounts.RemoveRange(profile.DurationDiscounts);
            profile.DurationDiscounts = discounts;
        }

        // Séances de groupe (lot 9.3, 17.3) : méthode, taille max, paliers ou
        // prix fixe, validés ensemble. N'affecte jamais le prix des
        // réservations existantes (figé à la création).
        if (request.UpdateGroupPricing == true)
        {
            var group = TutorPricing.ValidateGroupPricing(request.GroupPricingMethod, request.MaxGroupSize,
                request.GroupFlatPricePercent, request.GroupDiscounts, profile.Id);
            profile.GroupPricingMethod = group.Method;
            profile.MaxGroupSize = group.MaxGroupSize;
            profile.GroupFlatPricePercent = group.FlatPercent;
            _context.TutorGroupDiscounts.RemoveRange(profile.GroupDiscounts);
            profile.GroupDiscounts = group.Discounts;
        }

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
            .Include(p => p.Rates).Include(p => p.RateModifiers).Include(p => p.DurationDiscounts).Include(p => p.GroupDiscounts)
            .FirstOrDefaultAsync(p => p.UserId == userId && p.IsActive);
        return profile == null ? null : await MapToDtoAsync(profile, isPublicView: true);
    }

    public async Task<List<TutorSearchResultDto>> SearchAsync(string? subject, string? level, decimal? maxHourlyRateXaf, bool verifiedOnly, int page, int pageSize, string? mode = null, string? city = null, bool availableSoon = false, double? minRating = null, string? sort = null, string? topics = null, bool inPersonVerifiedOnly = false)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _context.TutorProfiles
            .Include(p => p.Subjects).Include(p => p.Levels).Include(p => p.Specialties)
            .Include(p => p.InterventionZones).Include(p => p.AvailabilitySlots)
            .Include(p => p.Rates)
            .Include(p => p.User)
            .Where(p => p.IsActive && !p.IsOnVacation)
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(subject))
            query = query.Where(p => p.Subjects.Any(s => s.Subject.ToLower() == subject.ToLower()));
        if (!string.IsNullOrWhiteSpace(level))
            query = query.Where(p => p.Levels.Any(l => l.Level.ToLower() == level.ToLower()));
        if (verifiedOnly)
            query = query.Where(p => p.IsDiplomaVerified);
        // Lot 9.5 (17.11) : un filtre présentiel ne renvoie que des tuteurs
        // vérifiés dont le présentiel n'est pas suspendu (cohérent avec le
        // refus serveur à la réservation). « in_person » = l'un des trois modes.
        query = mode switch
        {
            "online" => query.Where(p => p.OffersOnline),
            "student_home" => query.Where(p => p.OffersAtStudentHome && p.InPersonVerifiedAt != null && p.InPersonSuspendedAt == null),
            "tutor_home" => query.Where(p => p.OffersAtTutorHome && p.InPersonVerifiedAt != null && p.InPersonSuspendedAt == null),
            "neutral_place" => query.Where(p => p.OffersNeutralPlace && p.InPersonVerifiedAt != null && p.InPersonSuspendedAt == null),
            "in_person" => query.Where(p => (p.OffersAtStudentHome || p.OffersAtTutorHome || p.OffersNeutralPlace)
                                            && p.InPersonVerifiedAt != null && p.InPersonSuspendedAt == null),
            _ => query,
        };
        if (inPersonVerifiedOnly)
            query = query.Where(p => p.InPersonVerifiedAt != null && p.InPersonSuspendedAt == null);
        if (!string.IsNullOrWhiteSpace(city))
            query = query.Where(p => p.InterventionZones.Any(z => z.City.ToLower() == city.ToLower()));

        var candidates = await query.ToListAsync();

        // Lot 9.1 : le tarif comparé est le « à partir de » de la grille
        // (plus bas tarif de base), plus seulement HourlyRateXaf.
        var fromRates = candidates.ToDictionary(p => p.Id, p => TutorPricing.FromHourlyRate(TutorPricing.Grid.FromProfile(p)));
        if (maxHourlyRateXaf.HasValue)
            candidates = candidates.Where(p => fromRates[p.Id] is decimal r && r <= maxHourlyRateXaf.Value).ToList();

        // Sujets de séance (17.5) : classement par correspondance avec ce que
        // le répétiteur couvre (matières, spécialités, titre, bio) et avec les
        // sujets des séances qu'il a déjà données ou acceptées.
        var topicList = TutorTopicMatcher.ParseTopicsQuery(topics);
        var matchedTopics = new Dictionary<int, List<string>>();
        if (topicList.Count > 0 && candidates.Count > 0)
        {
            var ids = candidates.Select(p => p.Id).ToList();
            var pastTopics = await _context.TutorBookingTopics.AsNoTracking()
                .Where(t => ids.Contains(t.TutorBooking!.TutorProfileId)
                    && (t.TutorBooking.Status == "confirmed" || t.TutorBooking.Status == "completed"))
                .Select(t => new { t.TutorBooking!.TutorProfileId, t.Label })
                .ToListAsync();
            var pastByTutor = pastTopics.GroupBy(x => x.TutorProfileId).ToDictionary(g => g.Key, g => g.Select(x => x.Label).ToList());
            foreach (var p in candidates)
            {
                var corpus = p.Subjects.Select(s => s.Subject)
                    .Concat(p.Specialties.Select(s => s.Label))
                    .Append(p.Title).Append(p.TutorBio)
                    .Concat(pastByTutor.GetValueOrDefault(p.Id) ?? new List<string>());
                matchedTopics[p.Id] = TutorTopicMatcher.MatchedTopics(topicList, corpus);
            }
        }
        int TopicScore(TutorProfile p) => matchedTopics.TryGetValue(p.Id, out var m) ? m.Count : 0;

        // "Disponibilité immédiate" (US-REP-01) : approximation par les créneaux
        // hebdo déclarés sur les 48h à venir, en respectant le préavis minimum.
        // Ne vérifie pas les réservations déjà prises sur ce créneau précis
        // (ça exigerait de croiser TutorBookings pour toute la page)  un
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

        // Tri par pertinence (note × volume de séances × réactivité, US-REP-01) 
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
            "price-asc" => candidates.OrderBy(p => fromRates[p.Id] ?? decimal.MaxValue).ToList(),
            "price-desc" => candidates.OrderByDescending(p => fromRates[p.Id] ?? 0).ToList(),
            "next-slot" => candidates.OrderBy(p => nextSlots[p.Id] ?? DateTime.MaxValue).ToList(),
            // Pertinence : d'abord le nombre de sujets couverts (si l'élève en
            // a saisi), puis la note, le volume et la réactivité. Les profils
            // sans correspondance restent listés, après.
            _ => candidates
                .OrderByDescending(TopicScore)
                .ThenByDescending(RelevanceScore)
                .ThenByDescending(p => p.IsDiplomaVerified)
                .ThenBy(p => fromRates[p.Id])
                .ToList(),
        };

        var results = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        // Lot 9.5 : badge « Caution : nom de l'institution » (cautions valides seulement).
        var endorsements = await TutorVerificationService.EndorsingInstitutionNamesAsync(_context, results.Select(p => p.Id).ToList());

        return results.Select(p => new TutorSearchResultDto
        {
            IsInPersonVerified = TutorInPersonPolicy.IsEligible(p),
            OffersInPerson = TutorInPersonPolicy.OffersInPerson(p),
            OffersOnline = p.OffersOnline,
            EndorsedByInstitutions = endorsements.GetValueOrDefault(p.Id) ?? new List<string>(),
            UserId = p.UserId,
            FullName = p.User != null ? $"{p.User.FirstName} {p.User.LastName}".Trim() : null,
            AvatarUrl = p.User?.AvatarUrl,
            Title = p.Title,
            HourlyRateXaf = p.HourlyRateXaf,
            FromHourlyRateXaf = fromRates[p.Id],
            MatchedTopics = matchedTopics.GetValueOrDefault(p.Id) ?? new List<string>(),
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
    /// explicitement les champs réservés au professeur propriétaire  plutôt
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
        var endorsedBy = (await TutorVerificationService.EndorsingInstitutionNamesAsync(_context, new List<int> { profile.Id }))
            .GetValueOrDefault(profile.Id) ?? new List<string>();
        // Lot 9.5 (17.11) : modes présentiels EFFECTIFS, publics comme pour le
        // propriétaire (un tuteur non vérifié ou suspendu voit ses modes
        // présentiels à « non », comme le public ; les réglages enregistrés
        // restent en base et reviennent dès qu'il est vérifié ou que la
        // suspension est levée, s'il ne les a pas décochés entre-temps).
        // 17.17 : le propriétaire reçoit aussi ses choix ENREGISTRÉS
        // (Saved…), affichés cochés mais grisés « actif après vérification ».
        var eligible = TutorInPersonPolicy.IsEligible(profile);

        return new TutorProfileDto
        {
            SavedOffersAtStudentHome = isPublicView ? null : profile.OffersAtStudentHome,
            SavedOffersAtTutorHome = isPublicView ? null : profile.OffersAtTutorHome,
            SavedOffersNeutralPlace = isPublicView ? null : profile.OffersNeutralPlace,
            IsInPersonVerified = TutorInPersonPolicy.IsVerified(profile),
            InPersonSuspended = TutorInPersonPolicy.IsSuspended(profile),
            InPersonAllowed = eligible,
            InPersonBlockedReason = isPublicView ? null : TutorInPersonPolicy.BlockReason(profile),
            InPersonVerificationRoutes = isPublicView || string.IsNullOrEmpty(profile.InPersonVerificationRoutes)
                ? new List<string>()
                : profile.InPersonVerificationRoutes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            EndorsedByInstitutions = endorsedBy,
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
            OffersAtStudentHome = TutorInPersonPolicy.OffersAtStudentHome(profile),
            OffersAtTutorHome = TutorInPersonPolicy.OffersAtTutorHome(profile),
            OffersOnline = profile.OffersOnline,
            OffersNeutralPlace = TutorInPersonPolicy.OffersNeutralPlace(profile),
            TutorHomeAddressHint = eligible ? profile.TutorHomeAddressHint : (isPublicView ? null : profile.TutorHomeAddressHint),
            NoticeHours = profile.NoticeHours,
            MaxSessionsPerWeek = profile.MaxSessionsPerWeek,
            IsOnVacation = profile.IsOnVacation,
            // Réglages effectifs (bornés par la politique plateforme, lot 9.2) :
            // ceux qui s'appliquent réellement au remboursement.
            FullRefundHours = TutorPolicy.Effective(profile).FullRefundHours,
            PartialRefundPercent = TutorPolicy.Effective(profile).PartialRefundPercent,
            NoRefundHours = TutorPolicy.Effective(profile).NoRefundHours,
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
            // Forfaits historiques seulement (anciens clients) ; l'offre mensuelle est exposée à part.
            Packages = profile.Packages.Where(p => p.Kind != TutorPackageKinds.Monthly)
                .Select(p => new TutorPackageDto { Id = p.Id, Name = p.Name, SessionsCount = p.SessionsCount, TotalPriceXaf = p.TotalPriceXaf, IsActive = p.IsActive }).ToList(),
            MonthlyEnabled = profile.Packages.Any(p => p.Kind == TutorPackageKinds.Monthly && p.IsActive),
            MonthlyDiscountPercent = TutorMonthlyPlan.ClampDiscount(profile.Packages.FirstOrDefault(p => p.Kind == TutorPackageKinds.Monthly)?.DiscountPercent ?? 0),
            AvailabilitySlots = profile.AvailabilitySlots.Select(s => new TutorAvailabilitySlotDto
            {
                Id = s.Id,
                DayOfWeek = s.DayOfWeek,
                StartTime = s.StartTime.ToString(@"hh\:mm"),
                EndTime = s.EndTime.ToString(@"hh\:mm"),
                IsActive = s.IsActive,
            }).ToList(),
            Rates = profile.Rates
                .OrderBy(r => r.Subject == null).ThenBy(r => r.Subject).ThenBy(r => r.Mode)
                .Select(r => new TutorRateDto { Id = r.Id, Subject = r.Subject, Mode = r.Mode, HourlyRateXaf = r.HourlyRateXaf }).ToList(),
            RateModifiers = profile.RateModifiers.Select(m => new TutorRateModifierDto
            {
                Id = m.Id,
                Label = m.Label,
                DaysOfWeek = TutorPricing.FromMask(m.DaysOfWeekMask),
                StartTime = m.StartTime.HasValue ? TutorPricing.Hhmm(m.StartTime.Value) : null,
                EndTime = m.EndTime.HasValue ? TutorPricing.Hhmm(m.EndTime.Value) : null,
                PercentAdjustment = m.PercentAdjustment,
            }).ToList(),
            DurationDiscounts = profile.DurationDiscounts.OrderBy(d => d.MinDurationMinutes)
                .Select(d => new TutorDurationDiscountDto { Id = d.Id, MinDurationMinutes = d.MinDurationMinutes, PercentDiscount = d.PercentDiscount }).ToList(),
            FromHourlyRateXaf = TutorPricing.FromHourlyRate(TutorPricing.Grid.FromProfile(profile)),
            GroupPricingMethod = TutorPricing.GroupPricing.FromProfile(profile).Method,
            MaxGroupSize = TutorPricing.GroupPricing.FromProfile(profile).MaxGroupSize,
            GroupFlatPricePercent = profile.GroupPricingMethod == TutorPricing.GroupFlat ? profile.GroupFlatPricePercent : null,
            GroupDiscounts = profile.GroupDiscounts.OrderBy(d => d.MinGroupSize)
                .Select(d => new TutorGroupDiscountDto { Id = d.Id, MinGroupSize = d.MinGroupSize, PercentDiscount = d.PercentDiscount }).ToList(),
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
        Check(profile.HourlyRateXaf is > 0 || profile.Rates.Any(r => r.HourlyRateXaf > 0), 15, "hourlyRate", "Tarif horaire");
        Check(profile.AvailabilitySlots.Count > 0, 10, "availability", "Disponibilités hebdomadaires");
        Check(!string.IsNullOrWhiteSpace(profile.TutorBio), 10, "bio", "Bio courte");
        Check(!string.IsNullOrWhiteSpace(profile.Title), 5, "title", "Titre professionnel");
        Check(!string.IsNullOrWhiteSpace(profile.VideoUrl), 5, "video", "Vidéo d'introduction");

        return new TutorProfileCompletionDto { Score = Math.Max(0, score), MissingItems = missing };
    }
}
