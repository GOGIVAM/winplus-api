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
        await _context.SaveChangesAsync();

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

    public async Task<TutorProfileDto> UpdateAsync(int userId, UpdateTutorProfileRequestDto request)
    {
        var profile = await GetOrCreateEntityAsync(userId);

        if (request.Title != null) profile.Title = request.Title;
        if (request.TutorBio != null) profile.TutorBio = Truncate(request.TutorBio, 300);
        if (request.VideoUrl != null) profile.VideoUrl = request.VideoUrl;
        if (request.TeachingStyle != null) profile.TeachingStyle = request.TeachingStyle;

        if (request.OffersAtStudentHome.HasValue) profile.OffersAtStudentHome = request.OffersAtStudentHome.Value;
        if (request.OffersAtTutorHome.HasValue) profile.OffersAtTutorHome = request.OffersAtTutorHome.Value;
        if (request.OffersOnline.HasValue) profile.OffersOnline = request.OffersOnline.Value;
        if (request.OffersNeutralPlace.HasValue) profile.OffersNeutralPlace = request.OffersNeutralPlace.Value;
        if (request.TutorHomeAddressHint != null) profile.TutorHomeAddressHint = request.TutorHomeAddressHint;

        if (request.HourlyRateXaf.HasValue) profile.HourlyRateXaf = request.HourlyRateXaf.Value;
        if (request.TrialSessionEnabled.HasValue) profile.TrialSessionEnabled = request.TrialSessionEnabled.Value;
        if (request.TrialSessionPriceXaf.HasValue) profile.TrialSessionPriceXaf = request.TrialSessionPriceXaf.Value;

        if (request.NoticeHours.HasValue) profile.NoticeHours = request.NoticeHours.Value is 12 or 24 or 48 ? request.NoticeHours.Value : 24;
        if (request.MaxSessionsPerWeek.HasValue) profile.MaxSessionsPerWeek = request.MaxSessionsPerWeek.Value;

        if (request.OnboardingStep.HasValue) profile.OnboardingStep = Math.Clamp(request.OnboardingStep.Value, 0, 5);

        if (request.Subjects != null) ReplaceCollection(profile.Subjects, request.Subjects, s => new TutorSubject { TutorProfileId = profile.Id, Subject = s });
        if (request.Levels != null) ReplaceCollection(profile.Levels, request.Levels, l => new TutorLevel { TutorProfileId = profile.Id, Level = l });
        if (request.Specialties != null) ReplaceCollection(profile.Specialties, request.Specialties, s => new TutorSpecialty { TutorProfileId = profile.Id, Label = s });

        if (request.InterventionZones != null)
        {
            _context.TutorInterventionZones.RemoveRange(profile.InterventionZones);
            profile.InterventionZones = request.InterventionZones
                .Select(z => new TutorInterventionZone { TutorProfileId = profile.Id, City = z.City, Quartier = z.Quartier })
                .ToList();
        }

        if (request.Packages != null)
        {
            _context.TutorPackages.RemoveRange(profile.Packages);
            profile.Packages = request.Packages
                .Take(3) // "jusqu'à 3 forfaits" (US-PRO-10)
                .Select(p => new TutorPackage { TutorProfileId = profile.Id, Name = p.Name, SessionsCount = p.SessionsCount, TotalPriceXaf = p.TotalPriceXaf, IsActive = p.IsActive })
                .ToList();
        }

        if (request.AvailabilitySlots != null)
        {
            _context.TutorAvailabilitySlots.RemoveRange(profile.AvailabilitySlots);
            profile.AvailabilitySlots = request.AvailabilitySlots
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

        profile.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return await MapToDtoAsync(profile);
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
        return profile == null ? null : await MapToDtoAsync(profile);
    }

    public async Task<List<TutorSearchResultDto>> SearchAsync(string? subject, string? level, decimal? maxHourlyRateXaf, bool verifiedOnly, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _context.TutorProfiles
            .Include(p => p.Subjects).Include(p => p.Levels)
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

        var results = await query
            .OrderByDescending(p => p.IsDiplomaVerified)
            .ThenBy(p => p.HourlyRateXaf)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Note/avis : dépend du Module 6 (séances confirmées) qui n'existe pas
        // encore — on renvoie null/0 plutôt qu'une valeur fabriquée.
        return results.Select(p => new TutorSearchResultDto
        {
            UserId = p.UserId,
            FullName = p.User != null ? $"{p.User.FirstName} {p.User.LastName}".Trim() : null,
            AvatarUrl = p.User?.AvatarUrl,
            Title = p.Title,
            HourlyRateXaf = p.HourlyRateXaf,
            IsDiplomaVerified = p.IsDiplomaVerified,
            AverageRating = null,
            ReviewCount = 0,
            Subjects = p.Subjects.Select(s => s.Subject).ToList(),
            Levels = p.Levels.Select(l => l.Level).ToList(),
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

    private async Task<TutorProfileDto> MapToDtoAsync(TutorProfile profile)
    {
        var user = profile.User ?? await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == profile.UserId);
        var latestDoc = profile.VerificationDocuments
            .OrderByDescending(d => d.SubmittedAt)
            .FirstOrDefault();

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
            IsDiplomaVerified = profile.IsDiplomaVerified,
            // Badges "Expérimenté" / "Très réactif" : dépendent du nombre de
            // séances effectuées et du taux de réponse, deux métriques qui
            // n'existent qu'une fois le Module 6 (réservations) en place.
            IsExperienced = false,
            IsHighlyResponsive = false,
            IsActive = profile.IsActive,
            OnboardingStep = profile.OnboardingStep,
            CompletionScore = ComputeCompletion(profile, !string.IsNullOrWhiteSpace(user?.AvatarUrl) || !string.IsNullOrWhiteSpace(user?.ProfileImageUrl)).Score,
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
