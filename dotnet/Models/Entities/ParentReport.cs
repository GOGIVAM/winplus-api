using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Historique consultable des rapports destinés à un parent (hebdomadaire automatique, à la demande
/// d'un enseignant, capsule hebdomadaire, album de fin d'année). Jusqu'ici ces rapports étaient soit
/// envoyés uniquement par email sans trace (WeeklyParentReportService), soit renvoyés en texte brut
/// à l'appelant sans persistance (rapport à la demande)  et ce dernier finissait par erreur dans
/// DirectMessage faute de canal dédié. Cette table les regroupe, distincte de DirectMessage et de
/// Notification (rôles différents, ne pas fusionner).
///
/// ReportType : "Hebdomadaire" | "ALaDemande" | "CapsuleHebdo" | "AlbumAnnuel" | "Portefeuille"
/// EmitterType : "System" | "Teacher"
///
/// "Portefeuille" (MonthlyPortfolioService) : portrait stable de l'apprenant,
/// recalculé une fois par mois  jamais de score ni de comparaison entre
/// enfants. Content contient un JSON {"regularite","autonomie","curiosite"}
/// (trois textes descriptifs), pas de texte libre comme pour "ALaDemande".
/// </summary>
public class ParentReport
{
    public int Id { get; set; }

    public int ParentId { get; set; }

    /// <summary>Enfant concerné. Nullable : un rapport peut, à terme, couvrir plusieurs enfants (ex. futur récapitulatif consolidé).</summary>
    public int? ChildId { get; set; }

    [Required]
    [MaxLength(20)]
    public string ReportType { get; set; } = string.Empty;

    /// <summary>Contenu principal (rapport hebdomadaire, rapport à la demande, album annuel).</summary>
    public string? Content { get; set; }

    /// <summary>Résumé court (3 lignes max) réservé au ReportType "CapsuleHebdo".</summary>
    [MaxLength(500)]
    public string? CapsuleText { get; set; }

    [Required]
    [MaxLength(10)]
    public string EmitterType { get; set; } = "System";

    /// <summary>Null si EmitterType = "System" (généré automatiquement, sans émetteur humain).</summary>
    public int? EmitterId { get; set; }

    public bool IsRead { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ParentId))]
    public User? Parent { get; set; }

    [ForeignKey(nameof(ChildId))]
    public User? Child { get; set; }

    [ForeignKey(nameof(EmitterId))]
    public User? Emitter { get; set; }
}
