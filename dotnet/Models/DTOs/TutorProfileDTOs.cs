namespace Backend.Models.DTOs;

/// <summary>Profil répétiteur complet, tel que renvoyé au professeur (édition) ou à un élève (fiche publique).</summary>
public class TutorProfileDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string? FullName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Title { get; set; }
    public string? TutorBio { get; set; }
    public string? VideoUrl { get; set; }
    public string? TeachingStyle { get; set; }

    public decimal? HourlyRateXaf { get; set; }
    public bool TrialSessionEnabled { get; set; }
    public decimal? TrialSessionPriceXaf { get; set; }

    public bool OffersAtStudentHome { get; set; }
    public bool OffersAtTutorHome { get; set; }
    public bool OffersOnline { get; set; }
    public bool OffersNeutralPlace { get; set; }
    public string? TutorHomeAddressHint { get; set; }

    public int NoticeHours { get; set; }
    public int? MaxSessionsPerWeek { get; set; }
    public bool IsOnVacation { get; set; }

    public int FullRefundHours { get; set; }
    public int PartialRefundPercent { get; set; }
    public int NoRefundHours { get; set; }

    public bool IsDiplomaVerified { get; set; }
    /// <summary>≥10 séances marquées "Effectuée".</summary>
    public bool IsExperienced { get; set; }
    /// <summary>≥80% des demandes reçues acceptées/refusées avant expiration (sur au moins 5 demandes).</summary>
    public bool IsHighlyResponsive { get; set; }
    public double? AverageRating { get; set; }
    public int ReviewCount { get; set; }

    public bool IsActive { get; set; }
    public int OnboardingStep { get; set; }
    public int CompletionScore { get; set; }

    public List<string> Subjects { get; set; } = new();
    public List<string> Levels { get; set; } = new();
    public List<string> Specialties { get; set; } = new();
    public List<TutorZoneDto> InterventionZones { get; set; } = new();
    public List<TutorPackageDto> Packages { get; set; } = new();
    public List<TutorAvailabilitySlotDto> AvailabilitySlots { get; set; } = new();
    public TutorVerificationDocumentDto? PendingOrLatestDocument { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class TutorZoneDto
{
    public int? Id { get; set; }
    public string City { get; set; } = null!;
    public string? Quartier { get; set; }
}

public class TutorPackageDto
{
    public int? Id { get; set; }
    public string Name { get; set; } = null!;
    public int SessionsCount { get; set; }
    public decimal TotalPriceXaf { get; set; }
    public bool IsActive { get; set; } = true;
}

public class TutorAvailabilitySlotDto
{
    public int? Id { get; set; }
    public int DayOfWeek { get; set; }
    /// <summary>Format "HH:mm".</summary>
    public string StartTime { get; set; } = null!;
    public string EndTime { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}

public class TutorVerificationDocumentDto
{
    public int Id { get; set; }
    public string DocumentUrl { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? RejectionReason { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

/// <summary>
/// Corps des étapes 1 à 4 de l'onboarding (US-PRO-05) : chaque étape est
/// sauvegardée indépendamment, tous les champs sont donc optionnels — seuls
/// ceux fournis sont mis à jour (upsert partiel).
/// </summary>
public class UpdateTutorProfileRequestDto
{
    public string? Title { get; set; }
    public string? TutorBio { get; set; }
    public string? VideoUrl { get; set; }
    public string? TeachingStyle { get; set; }

    public List<string>? Subjects { get; set; }
    public List<string>? Levels { get; set; }
    public List<string>? Specialties { get; set; }

    public bool? OffersAtStudentHome { get; set; }
    public bool? OffersAtTutorHome { get; set; }
    public bool? OffersOnline { get; set; }
    public bool? OffersNeutralPlace { get; set; }
    public string? TutorHomeAddressHint { get; set; }
    public List<TutorZoneDto>? InterventionZones { get; set; }

    public decimal? HourlyRateXaf { get; set; }
    public bool? TrialSessionEnabled { get; set; }
    public decimal? TrialSessionPriceXaf { get; set; }
    public List<TutorPackageDto>? Packages { get; set; }

    public int? NoticeHours { get; set; }
    public int? MaxSessionsPerWeek { get; set; }
    public List<TutorAvailabilitySlotDto>? AvailabilitySlots { get; set; }

    public int? FullRefundHours { get; set; }
    public int? PartialRefundPercent { get; set; }
    public int? NoRefundHours { get; set; }

    /// <summary>Étape atteinte (1-5), pour reprendre l'onboarding où l'utilisateur s'était arrêté.</summary>
    public int? OnboardingStep { get; set; }
}

/// <summary>
/// Corps dédié à la grille de disponibilités (US-PRO-08) : sauvegarde
/// instantanée à chaque toggle de créneau, indépendante du reste du profil
/// (pas de bouton "Enregistrer" séparé côté front).
/// </summary>
public class UpdateTutorAvailabilityRequestDto
{
    public List<TutorAvailabilitySlotDto> Slots { get; set; } = new();
}

public class UploadTutorVerificationDocumentRequestDto
{
    public string DocumentUrl { get; set; } = null!;
}

public class TutorProfileCompletionDto
{
    public int Score { get; set; }
    public List<TutorProfileMissingItemDto> MissingItems { get; set; } = new();
}

public class TutorProfileMissingItemDto
{
    public string Field { get; set; } = null!;
    public string Label { get; set; } = null!;
}

/// <summary>Résultat d'une recherche de répétiteur côté élève (Module B du référentiel).</summary>
public class TutorSearchResultDto
{
    public int UserId { get; set; }
    public string? FullName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Title { get; set; }
    public decimal? HourlyRateXaf { get; set; }
    public bool IsDiplomaVerified { get; set; }
    public double? AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public List<string> Subjects { get; set; } = new();
    public List<string> Levels { get; set; } = new();
    /// <summary>Premier créneau réservable dans les 14 prochains jours (null si aucun).</summary>
    public DateTime? NextAvailableSlot { get; set; }
}
