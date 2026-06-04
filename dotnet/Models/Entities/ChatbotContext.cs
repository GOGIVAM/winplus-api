using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Contexte utilisateur pour personnaliser les réponses du chatbot
/// </summary>
public class ChatbotContext
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// ID utilisateur unique (relation 1:1)
    /// </summary>
    [Required]
    public int UserId { get; set; }

    /// <summary>
    /// Niveau d'études (primaire, collège, lycée, prépa, université)
    /// </summary>
    [MaxLength(50)]
    public string? EducationLevel { get; set; }

    /// <summary>
    /// Classe/Année (6ème, 3ème, Terminale, L1, etc.)
    /// </summary>
    [MaxLength(50)]
    public string? Grade { get; set; }

    /// <summary>
    /// Objectifs d'apprentissage de l'utilisateur - JSON array
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? UserObjectives { get; set; }

    /// <summary>
    /// Matières suivies avec progression - JSON array
    /// [{"subjectId": 1, "title": "Maths", "progress": 45}]
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? EnrolledSubjects { get; set; }

    /// <summary>
    /// Activités récentes de l'utilisateur - JSON array
    /// [{"type": "quiz", "subjectId": 1, "score": 85, "at": "2026-02-01"}]
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? RecentActivity { get; set; }

    /// <summary>
    /// Historique de navigation récent - JSON array
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? NavigationHistory { get; set; }

    /// <summary>
    /// Préférences utilisateur - JSON object
    /// {"language": "fr", "theme": "dark", "notifications": true}
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? Preferences { get; set; }

    /// <summary>
    /// Forces identifiées de l'utilisateur - JSON array
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? Strengths { get; set; }

    /// <summary>
    /// Faiblesses identifiées - JSON array
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? Weaknesses { get; set; }

    /// <summary>
    /// Style d'apprentissage préféré
    /// </summary>
    [MaxLength(50)]
    public string? LearningStyle { get; set; }

    /// <summary>
    /// Date de création
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Date de dernière mise à jour
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("UserId")]
    public virtual User User { get; set; } = null!;
}
