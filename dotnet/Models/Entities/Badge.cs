using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Module 24 (lot 8, rattrapage 16.2) : catalogue des badges plateforme.
/// Premier catalogue volontairement restreint (décision §16.2 du suivi),
/// basé uniquement sur des données déjà en base (aucune nouvelle mesure à
/// instrumenter) : quiz réussis, série de jours actifs (même calcul que
/// UsersController.GetProfileStatistics), formation terminée, score parfait.
/// Table alimentée par la migration additive (seed), pas par un écran admin
/// pour l'instant  étendre le catalogue plus tard se fait par migration.
/// </summary>
public class BadgeDefinition
{
    public int Id { get; set; }

    /// <summary>Identifiant stable utilisé par le code (jamais affiché).</summary>
    [Required, MaxLength(50)]
    public string Code { get; set; } = null!;

    [Required, MaxLength(150)]
    public string Title { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Indice d'icône côté client (nom libre, ex. "quiz", "streak", "certificate").</summary>
    [MaxLength(50)]
    public string? Icon { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<UserBadge> UserBadges { get; set; } = new List<UserBadge>();
}

/// <summary>Badge débloqué par un utilisateur (unique par utilisateur+badge).</summary>
public class UserBadge
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int BadgeDefinitionId { get; set; }

    public DateTime UnlockedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(BadgeDefinitionId))]
    public virtual BadgeDefinition? BadgeDefinition { get; set; }
}

/// <summary>Codes stables du catalogue (décision §16.2) — évite les chaînes magiques dispersées.</summary>
public static class BadgeCodes
{
    public const string FirstQuizPassed = "first_quiz_passed";
    public const string TenQuizzesPassed = "ten_quizzes_passed";
    public const string PerfectQuizScore = "perfect_quiz_score";
    public const string FirstCourseCompleted = "first_course_completed";
    public const string Streak7 = "streak_7";
    public const string Streak30 = "streak_30";
}
