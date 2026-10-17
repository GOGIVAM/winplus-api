using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Lot 9.7b (étude 1.4) : enfant désigné par le parent comme couvert par son
/// plan (catalogue, bonus WinAI, alertes) quand il a plus d'enfants liés et
/// acceptés que <c>children.max</c>. Sans ligne, la règle par défaut
/// s'applique : les liens acceptés les plus anciens. Une ligne par enfant
/// choisi ; toutes les lignes d'un parent portent la date du dernier choix,
/// qui sert à limiter les changements (un par cycle d'abonnement, voir
/// <c>ParentCoverage</c>). Schéma : Migrations/SQL_ParentChildCoverage97b.sql.
/// </summary>
public class ParentChildCoverage
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public int StudentId { get; set; }
    public DateTime SelectedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ParentId))]
    public User? Parent { get; set; }

    [ForeignKey(nameof(StudentId))]
    public User? Student { get; set; }
}
