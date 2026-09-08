using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Copie d'un élève pour un devoir (Module 4 — Corrections). Deux origines
/// possibles (<see cref="Source"/>) : l'élève l'a soumise lui-même depuis son
/// espace, ou le professeur l'a uploadée pour lui (copie papier scannée).
/// Porte aussi la correction elle-même (note/commentaire/brouillon) : pas de
/// table séparée, une copie n'a jamais qu'une correction à la fois.
/// </summary>
public class Submission
{
    public int Id { get; set; }

    public int AssignmentId { get; set; }

    public int StudentId { get; set; }

    /// <summary>Réponse texte (saisie ou collée) ; peut coexister avec un fichier.</summary>
    public string? Content { get; set; }

    [MaxLength(500)]
    public string? FileUrl { get; set; }

    /// <summary>student | teacher_upload</summary>
    [MaxLength(20)]
    public string Source { get; set; } = "student";

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    // ── Correction ───────────────────────────────────────────────────────
    /// <summary>pending | draft | corrected</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    public decimal? Score { get; set; }

    public string? Comment { get; set; }

    /// <summary>methodological | calculation | conceptual | none — dernière analyse WinAI.</summary>
    [MaxLength(30)]
    public string? ErrorType { get; set; }

    public DateTime? DraftUpdatedAt { get; set; }

    public DateTime? GradedAt { get; set; }

    public int? GradedByUserId { get; set; }

    [ForeignKey(nameof(AssignmentId))]
    public Assignment? Assignment { get; set; }

    [ForeignKey(nameof(StudentId))]
    public User? Student { get; set; }
}
