using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Fiche de révision WinAI générée par un répétiteur pour un élève donné
/// (US-REP-11, Module 6). Une seule fiche par couple répétiteur/élève —
/// régénérée écrase la précédente (le professeur peut la modifier avant envoi).
/// </summary>
public class TutorRevisionSheet
{
    public int Id { get; set; }
    public int TutorUserId { get; set; }
    public int StudentUserId { get; set; }
    public string? Subject { get; set; }
    public string Content { get; set; } = null!;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(StudentUserId))]
    public User? Student { get; set; }
}

/// <summary>
/// Rapport mensuel WinAI de coaching pédagogique (US-REP-12, Module 6).
/// Généré automatiquement le 1er de chaque mois par TutorCoachingReportService,
/// ou à la demande via l'endpoint "generate-now".
/// </summary>
public class TutorCoachingReport
{
    public int Id { get; set; }
    public int TutorUserId { get; set; }
    public string MonthLabel { get; set; } = null!;
    public string Content { get; set; } = null!;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
