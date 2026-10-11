using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Grille tarifaire du répétiteur (lot 9.1, suivi Partie 17.3). Trois tables
/// simples plutôt qu'un bloc JSON : chaque règle se valide, s'édite et se
/// requête (recherche, suggestion WinAI) individuellement, comme les autres
/// collections du profil (matières, niveaux, créneaux).
///
/// Le calcul du prix à partir de ces règles est fait par
/// <see cref="Backend.Services.TutorPricing"/>, seule source de vérité du prix
/// d'une séance : le client affiche toujours un devis renvoyé par le serveur.
///
/// Rétrocompatibilité : tant que le répétiteur n'a saisi aucun
/// <see cref="TutorRate"/>, <see cref="TutorProfile.HourlyRateXaf"/> sert de
/// tarif de base pour toutes les matières et tous les modes.
///
/// Point d'extension 9.3 (séances de groupe) : la méthode de prix de groupe
/// (prix par élève dégressif selon la taille, ou prix fixe pour le groupe)
/// viendra s'ajouter ici comme une règle supplémentaire du même profil ; rien
/// n'est modélisé avant d'en connaître la forme exacte.
/// </summary>
public class TutorRate
{
    public int Id { get; set; }
    public int TutorProfileId { get; set; }

    /// <summary>Matière concernée ; null = toutes les matières du répétiteur.</summary>
    [MaxLength(100)]
    public string? Subject { get; set; }

    /// <summary>"online" | "in_person" (domicile élève, domicile tuteur, lieu neutre) ; null = tous les modes.</summary>
    [MaxLength(20)]
    public string? Mode { get; set; }

    /// <summary>Tarif de base pour une heure, facturé au prorata des tranches de 30 min.</summary>
    public decimal HourlyRateXaf { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }
}

/// <summary>
/// Modulation par créneau (17.3) : majoration (pourcentage positif) ou
/// minoration (négatif) appliquée aux tranches de 30 min qui commencent dans
/// la fenêtre. Ex. « Soir » lun-ven 18:00-21:00 +20 %, « Week-end » sam-dim +15 %.
/// </summary>
public class TutorRateModifier
{
    public int Id { get; set; }
    public int TutorProfileId { get; set; }

    [Required, MaxLength(60)]
    public string Label { get; set; } = "";

    /// <summary>Jours concernés en masque de bits (bit n = DayOfWeek n, 0 = dimanche) ; 0 = tous les jours.</summary>
    public int DaysOfWeekMask { get; set; }

    /// <summary>Début de la fenêtre horaire ; null (avec EndTime null) = toute la journée.</summary>
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }

    /// <summary>De -50 à +100 (%).</summary>
    public int PercentAdjustment { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }
}

/// <summary>
/// Dégressivité selon la durée (17.3) : à partir de MinDurationMinutes, remise
/// de PercentDiscount % sur toute la séance. Seul le palier le plus élevé
/// atteint s'applique (les paliers ne se cumulent pas).
/// </summary>
public class TutorDurationDiscount
{
    public int Id { get; set; }
    public int TutorProfileId { get; set; }

    public int MinDurationMinutes { get; set; }

    /// <summary>De 1 à 50 (%).</summary>
    public int PercentDiscount { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }
}

/// <summary>
/// Sujet de séance saisi par celui qui réserve (17.5) : texte court, plusieurs
/// par réservation. Visible du répétiteur dans ses demandes, utilisé par la
/// recherche de répétiteurs. La génération de supports à partir de ces sujets
/// relève du lot 9.6.
/// </summary>
public class TutorBookingTopic
{
    public int Id { get; set; }
    public int TutorBookingId { get; set; }

    [Required, MaxLength(100)]
    public string Label { get; set; } = "";

    [ForeignKey(nameof(TutorBookingId))]
    public TutorBooking? TutorBooking { get; set; }
}
