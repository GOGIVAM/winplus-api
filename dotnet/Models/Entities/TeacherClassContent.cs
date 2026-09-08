using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Contenu du catalogue assigné par un professeur à l'une de ses classes
/// (Module 2, US-CAT-04). WinPlus déduit le montant une seule fois au
/// professeur ; tous les élèves de la classe y accèdent ensuite sans payer
/// individuellement (voir TeacherClassContentService).
/// </summary>
public class TeacherClassContent
{
    public int Id { get; set; }

    public int TeacherClassId { get; set; }

    public int SubjectId { get; set; }

    public int AssignedByUserId { get; set; }

    /// <summary>Montant réellement débité au professeur pour cette assignation (0 si déjà possédé/gratuit).</summary>
    public decimal PriceChargedXaf { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TeacherClassId))]
    public TeacherClass? TeacherClass { get; set; }

    [ForeignKey(nameof(SubjectId))]
    public Subject? Subject { get; set; }
}
