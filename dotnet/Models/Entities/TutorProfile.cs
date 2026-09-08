using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Profil "Mode Répétiteur" d'un professeur — cours particuliers, distinct du
/// mode "Professeur Catalogue" (publication d'épreuves/formations). Un même
/// compte User (Role = "teacher") peut avoir les deux modes actifs en même
/// temps ; ce profil n'existe que si l'utilisateur a activé le mode Répétiteur
/// au moins une fois (voir Workflow 1 — Onboarding Répétiteur).
/// </summary>
public class TutorProfile
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>Titre libre : "Prof de Maths — Terminale C/D".</summary>
    [MaxLength(150)]
    public string? Title { get; set; }

    /// <summary>Bio courte adressée à l'élève, 300 caractères max (US-PRO du référentiel).</summary>
    [MaxLength(300)]
    public string? TutorBio { get; set; }

    /// <summary>Lien YouTube/Vimeo ou URL d'upload direct.</summary>
    [MaxLength(500)]
    public string? VideoUrl { get; set; }

    /// <summary>Structuré | Interactif | Mixte.</summary>
    [MaxLength(30)]
    public string? TeachingStyle { get; set; }

    // ── Tarification ──────────────────────────────────────────────────────
    public decimal? HourlyRateXaf { get; set; }
    public bool TrialSessionEnabled { get; set; } = false;
    public decimal? TrialSessionPriceXaf { get; set; } // null = gratuite

    // ── Modes d'intervention ─────────────────────────────────────────────
    public bool OffersAtStudentHome { get; set; } = false;
    public bool OffersAtTutorHome { get; set; } = false;
    public bool OffersOnline { get; set; } = false;
    public bool OffersNeutralPlace { get; set; } = false;

    /// <summary>Adresse approximative (quartier) si OffersAtTutorHome.</summary>
    [MaxLength(200)]
    public string? TutorHomeAddressHint { get; set; }

    // ── Disponibilités / agenda ───────────────────────────────────────────
    /// <summary>Délai de préavis minimum en heures : 12, 24 ou 48.</summary>
    public int NoticeHours { get; set; } = 24;

    /// <summary>Nombre max de séances par semaine ; null = illimité.</summary>
    public int? MaxSessionsPerWeek { get; set; }

    /// <summary>Statut "En vacances" : profil visible mais réservations bloquées.</summary>
    public bool IsOnVacation { get; set; } = false;

    // ── Vérification / crédibilité ────────────────────────────────────────
    /// <summary>Diplôme validé par l'équipe WinPlus → badge "Vérifié Diplôme".</summary>
    public bool IsDiplomaVerified { get; set; } = false;

    // ── État du mode ──────────────────────────────────────────────────────
    /// <summary>Mode Répétiteur activé (profil visible dans la recherche élève).</summary>
    public bool IsActive { get; set; } = false;

    /// <summary>Dernière étape complétée de l'onboarding (1-5), pour reprendre où l'utilisateur s'était arrêté.</summary>
    public int OnboardingStep { get; set; } = 0;

    // ── Politique d'annulation (référentiel §I.C) ───────────────────────────
    /// <summary>Remboursement total si annulation à plus de N heures de la séance.</summary>
    public int FullRefundHours { get; set; } = 24;
    /// <summary>Pourcentage remboursé entre NoRefundHours et FullRefundHours (0-100).</summary>
    public int PartialRefundPercent { get; set; } = 50;
    /// <summary>Aucun remboursement en-deçà de N heures avant la séance.</summary>
    public int NoRefundHours { get; set; } = 2;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public ICollection<TutorSubject> Subjects { get; set; } = new List<TutorSubject>();
    public ICollection<TutorLevel> Levels { get; set; } = new List<TutorLevel>();
    public ICollection<TutorSpecialty> Specialties { get; set; } = new List<TutorSpecialty>();
    public ICollection<TutorInterventionZone> InterventionZones { get; set; } = new List<TutorInterventionZone>();
    public ICollection<TutorPackage> Packages { get; set; } = new List<TutorPackage>();
    public ICollection<TutorAvailabilitySlot> AvailabilitySlots { get; set; } = new List<TutorAvailabilitySlot>();
    public ICollection<TutorVerificationDocument> VerificationDocuments { get; set; } = new List<TutorVerificationDocument>();
}

/// <summary>Matière enseignée en cours particulier (multiselect, US-PRO-02).</summary>
public class TutorSubject
{
    public int Id { get; set; }
    public int TutorProfileId { get; set; }

    [Required, MaxLength(100)]
    public required string Subject { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }
}

/// <summary>Niveau couvert : 6ème à Tle, BEPC, BAC, prépas, concours (US-PRO-02).</summary>
public class TutorLevel
{
    public int Id { get; set; }
    public int TutorProfileId { get; set; }

    [Required, MaxLength(100)]
    public required string Level { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }
}

/// <summary>Spécialité en tag libre : "Préparation concours", "Rattrapage express"...</summary>
public class TutorSpecialty
{
    public int Id { get; set; }
    public int TutorProfileId { get; set; }

    [Required, MaxLength(100)]
    public required string Label { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }
}

/// <summary>Zone géographique couverte pour les cours à domicile (US-PRO-07).</summary>
public class TutorInterventionZone
{
    public int Id { get; set; }
    public int TutorProfileId { get; set; }

    [Required, MaxLength(100)]
    public required string City { get; set; }

    [MaxLength(100)]
    public string? Quartier { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }
}

/// <summary>Forfait multi-séances à prix dégressif (US-PRO-10).</summary>
public class TutorPackage
{
    public int Id { get; set; }
    public int TutorProfileId { get; set; }

    [Required, MaxLength(100)]
    public required string Name { get; set; }

    public int SessionsCount { get; set; }
    public decimal TotalPriceXaf { get; set; }
    public bool IsActive { get; set; } = true;

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }
}

/// <summary>
/// Créneau hebdomadaire type, reconductible automatiquement chaque semaine
/// (US-PRO-08). DayOfWeek : 0 = dimanche ... 6 = samedi (convention .NET DayOfWeek).
/// </summary>
public class TutorAvailabilitySlot
{
    public int Id { get; set; }
    public int TutorProfileId { get; set; }

    public int DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool IsActive { get; set; } = true;

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }
}

/// <summary>Diplôme/relevé de notes uploadé pour le badge "Vérifié Diplôme" (US-PRO-03).</summary>
public class TutorVerificationDocument
{
    public int Id { get; set; }
    public int TutorProfileId { get; set; }

    [Required, MaxLength(500)]
    public required string DocumentUrl { get; set; }

    /// <summary>pending | approved | rejected</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedByUserId { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }
}
