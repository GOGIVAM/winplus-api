namespace Backend.Models.Entities;

/// <summary>
/// Alerte de similarité entre deux copies marquée "Faux positif" par le
/// professeur (US-COR-03) — la similarité est recalculée à chaque appel
/// (pas de score stocké), seule la décision de l'ignorer est persistée.
/// SubmissionAId est toujours le plus petit des deux id (ordre canonique),
/// pour qu'une paire ne soit jamais stockée deux fois dans des sens opposés.
/// </summary>
public class SubmissionSimilarityDismissal
{
    public int Id { get; set; }
    public int SubmissionAId { get; set; }
    public int SubmissionBId { get; set; }
    public int DismissedByUserId { get; set; }
    public DateTime DismissedAt { get; set; } = DateTime.UtcNow;
}
