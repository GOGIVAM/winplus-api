using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Historique consultable des alertes WinAI destinées à un parent au sujet d'un enfant lié.
/// Jusqu'ici, ces alertes (voir backend/python/routes/parent_alert_routes.py) étaient recalculées
/// à la volée à chaque appel et jamais persistées — un parent ne pouvait pas consulter une alerte
/// déjà vue. Cette table lui donne un historique, distincte de DirectMessage et de Notification
/// (rôles différents, ne pas fusionner).
///
/// Type : "BaissePerformance" | "Inactivite" | "Surmenage" | "Felicitations" | "AnxieteExamen"
/// Severity : "Low" | "Medium" | "High"
/// </summary>
public class ParentAlert
{
    public int Id { get; set; }

    public int ParentId { get; set; }

    public int ChildId { get; set; }

    [Required]
    [MaxLength(30)]
    public string Type { get; set; } = string.Empty;

    [Required]
    [MaxLength(10)]
    public string Severity { get; set; } = "Low";

    /// <summary>Texte bienveillant destiné au parent — jamais de vocabulaire clinique/diagnostique.</summary>
    [Required]
    public string Content { get; set; } = string.Empty;

    public bool IsRead { get; set; } = false;

    /// <summary>Moment où le signal a été détecté dans l'activité de l'enfant (peut différer de CreatedAt).</summary>
    public DateTime DetectedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ParentId))]
    public User? Parent { get; set; }

    [ForeignKey(nameof(ChildId))]
    public User? Child { get; set; }
}
