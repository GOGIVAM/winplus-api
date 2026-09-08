using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Fil de discussion d'une formation (Module 7, 3C). Distinct de la
/// messagerie directe (DirectMessage) : c'est un canal public à tous les
/// inscrits de la formation, avec Q&amp;A automatique WinAI optionnel.
/// </summary>
public class CourseChannelMessage
{
    public int Id { get; set; }

    public int CourseId { get; set; }

    /// <summary>Null si le message est généré par WinAI.</summary>
    public int? SenderUserId { get; set; }

    public bool IsAiGenerated { get; set; } = false;
    public double? AiConfidence { get; set; }

    /// <summary>true si la question a été signalée au professeur faute de confiance suffisante de WinAI.</summary>
    public bool TaggedProfessor { get; set; } = false;

    [Required, MaxLength(2000)]
    public required string Content { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(CourseId))]
    public Course? Course { get; set; }

    [ForeignKey(nameof(SenderUserId))]
    public User? Sender { get; set; }
}

/// <summary>
/// Journal des interactions Q&amp;A WinAI par formation (US-3C, "WinAI_InteractionLog"
/// du référentiel) — persisté côté C# plutôt que Python pour rester la seule
/// source de vérité du schéma (même convention que le reste du projet : le
/// service Python est un calcul sans état, jamais un propriétaire de table).
/// </summary>
public class WinAIInteractionLog
{
    public int Id { get; set; }

    public int CourseId { get; set; }
    public int StudentUserId { get; set; }
    public int? QuestionMessageId { get; set; }

    [Required, MaxLength(2000)]
    public required string Question { get; set; }

    [MaxLength(4000)]
    public string? Answer { get; set; }

    public double? Confidence { get; set; }

    /// <summary>answered | tag_professor.</summary>
    [MaxLength(20)]
    public string Action { get; set; } = "answered";

    public bool CorrectedByTeacher { get; set; } = false;
    [MaxLength(4000)]
    public string? CorrectedAnswer { get; set; }
    public DateTime? CorrectedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(CourseId))]
    public Course? Course { get; set; }
}
