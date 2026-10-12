using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Profil "Mode Répétiteur" d'un professeur  cours particuliers, distinct du
/// mode "Professeur Catalogue" (publication d'épreuves/formations). Un même
/// compte User (Role = "teacher") peut avoir les deux modes actifs en même
/// temps ; ce profil n'existe que si l'utilisateur a activé le mode Répétiteur
/// au moins une fois (voir Workflow 1  Onboarding Répétiteur).
/// </summary>
public class TutorProfile
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>Titre libre : "Prof de Maths  Terminale C/D".</summary>
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
    /// <summary>
    /// Diplôme validé par l'équipe WinPlus → badge "Vérifié Diplôme".
    /// Lot 9.5 (17.11) : badge FACULTATIF, sans effet sur le présentiel.
    /// </summary>
    public bool IsDiplomaVerified { get; set; } = false;

    // ── Présentiel (lot 9.5, suivi 17.11, 17.12) ──────────────────────────
    // Cache du statut calculé par TutorVerificationService.RecomputeAsync à
    // partir des voies valides (TutorVerification.cs). Jamais saisi à la main.
    // Présentiel possible = InPersonVerifiedAt non nul ET InPersonSuspendedAt nul
    // (voir TutorInPersonPolicy).

    /// <summary>Depuis quand le tuteur est vérifié pour le présentiel ; null = non vérifié.</summary>
    public DateTime? InPersonVerifiedAt { get; set; }

    /// <summary>Voies valides, séparées par des virgules : identity, interview, institution.</summary>
    [MaxLength(60)]
    public string? InPersonVerificationRoutes { get; set; }

    /// <summary>Présentiel suspendu (seuil de signalements atteint) jusqu'à l'examen d'un admin.</summary>
    public DateTime? InPersonSuspendedAt { get; set; }
    [MaxLength(300)]
    public string? InPersonSuspensionReason { get; set; }

    /// <summary>Dernière levée de suspension : les signalements antérieurs ne recomptent pas pour le seuil.</summary>
    public DateTime? InPersonSuspensionLiftedAt { get; set; }

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

    // ── Séances de groupe (lot 9.3, suivi 17.3) ──────────────────────────
    /// <summary>
    /// null = pas de séance de groupe (une réservation = un élève) ;
    /// per_student = prix par élève dégressif selon la taille du groupe
    /// (paliers <see cref="GroupDiscounts"/>) ; flat = prix fixe pour le groupe
    /// (prix individuel × <see cref="GroupFlatPricePercent"/> %).
    /// </summary>
    [MaxLength(20)]
    public string? GroupPricingMethod { get; set; }

    /// <summary>Taille maximale d'un groupe (2 à 20) ; 1 = séances individuelles seulement.</summary>
    public int MaxGroupSize { get; set; } = 1;

    /// <summary>Méthode flat : prix du groupe en % du prix individuel de la même séance (100 à 1000).</summary>
    public int? GroupFlatPricePercent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public ICollection<TutorGroupDiscount> GroupDiscounts { get; set; } = new List<TutorGroupDiscount>();

    public ICollection<TutorSubject> Subjects { get; set; } = new List<TutorSubject>();
    public ICollection<TutorLevel> Levels { get; set; } = new List<TutorLevel>();
    public ICollection<TutorSpecialty> Specialties { get; set; } = new List<TutorSpecialty>();
    public ICollection<TutorInterventionZone> InterventionZones { get; set; } = new List<TutorInterventionZone>();
    public ICollection<TutorPackage> Packages { get; set; } = new List<TutorPackage>();
    public ICollection<TutorAvailabilitySlot> AvailabilitySlots { get; set; } = new List<TutorAvailabilitySlot>();
    public ICollection<TutorVerificationDocument> VerificationDocuments { get; set; } = new List<TutorVerificationDocument>();

    // ── Grille tarifaire (lot 9.1, voir TutorPricing.cs) ──────────────────
    public ICollection<TutorRate> Rates { get; set; } = new List<TutorRate>();
    public ICollection<TutorRateModifier> RateModifiers { get; set; } = new List<TutorRateModifier>();
    public ICollection<TutorDurationDiscount> DurationDiscounts { get; set; } = new List<TutorDurationDiscount>();
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

/// <summary>
/// Offre du tuteur. Historiquement (US-PRO-10) un « forfait N séances à prix
/// total fixe », jamais branché sur la réservation (Kind = bundle, conservé
/// tel quel pour les anciens clients, sans effet). Depuis le lot 9.4 (suivi
/// 17.7), la ligne Kind = monthly est l'offre d'engagement mensuel réelle du
/// tuteur : activée ou non (<see cref="IsActive"/>), avec une remise mensuelle
/// facultative (<see cref="DiscountPercent"/>, 0 à 30 %). Au plus une ligne
/// monthly par profil (index unique partiel). Le prix d'un engagement n'est
/// jamais lu ici : chaque séance est chiffrée par TutorPricing.Compute, puis la
/// remise s'applique (voir TutorMonthlyPlan).
/// </summary>
public class TutorPackage
{
    public int Id { get; set; }
    public int TutorProfileId { get; set; }

    [Required, MaxLength(100)]
    public required string Name { get; set; }

    /// <summary>Forfait historique uniquement (inutilisé pour l'offre mensuelle).</summary>
    public int SessionsCount { get; set; }
    /// <summary>Forfait historique uniquement (inutilisé pour l'offre mensuelle).</summary>
    public decimal TotalPriceXaf { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>bundle (forfait historique, inerte) | monthly (offre d'engagement mensuel, lot 9.4).</summary>
    [MaxLength(20)]
    public string Kind { get; set; } = TutorPackageKinds.Bundle;

    /// <summary>Remise mensuelle en % (0 à <see cref="TutorPackageKinds.MaxMonthlyDiscountPercent"/>), offre monthly seulement.</summary>
    public int DiscountPercent { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }
}

/// <summary>Nature d'une ligne <see cref="TutorPackage"/> (lot 9.4).</summary>
public static class TutorPackageKinds
{
    public const string Bundle = "bundle";
    public const string Monthly = "monthly";

    /// <summary>Borne serveur de la remise mensuelle (règle produit 9.4 : 0 à 30 %).</summary>
    public const int MaxMonthlyDiscountPercent = 30;
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
