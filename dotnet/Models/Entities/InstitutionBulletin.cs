using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

/// <summary>
/// Bulletin d'un élève partagé par son établissement (à lui et à ses parents).
/// Contenu figé au partage dans DataJson. Schéma posé par
/// Migrations/SQL_InstitutionBulletins.sql.
/// </summary>
public class InstitutionBulletin
{
    public int Id { get; set; }
    public int InstitutionId { get; set; }
    public int StudentId { get; set; }
    public int CreatedByUserId { get; set; }

    [MaxLength(100)]
    public string PeriodLabel { get; set; } = string.Empty;

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    /// <summary>Appréciation de l'établissement (texte libre, éventuellement proposée par WinAI).</summary>
    public string? Appreciation { get; set; }

    /// <summary>Résultats calculés au moment du partage (JSON).</summary>
    public string DataJson { get; set; } = "{}";

    public DateTime SharedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public Institution? Institution { get; set; }
}
