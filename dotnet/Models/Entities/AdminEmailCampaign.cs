using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

/// <summary>
/// Envoi d'e-mail de masse depuis l'administration (lot 9.7a, suivi 18.1) :
/// confirmé par l'administrateur avec le nombre de destinataires, puis envoyé
/// en tâche de fond par <c>AdminEmailCampaignWorker</c> (la requête HTTP ne
/// dépasse plus jamais la coupure de 90 s de nginx). Le rapport
/// (envoyés, échecs, désabonnés) est mis à jour au fil de l'envoi.
/// </summary>
public class AdminEmailCampaign
{
    public int Id { get; set; }

    /// <summary>all | students | teachers | parents | institutions | custom.</summary>
    [MaxLength(20)]
    public string Target { get; set; } = "all";

    [MaxLength(256)]
    public string? CustomEmail { get; set; }

    [MaxLength(300)]
    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>queued | sending | completed | failed.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "queued";

    /// <summary>Destinataires retenus après filtrage des préférences.</summary>
    public int RecipientCount { get; set; }

    public int SentCount { get; set; }

    public int FailedCount { get; set; }

    /// <summary>Comptes exclus parce qu'ils ont désactivé ces e-mails.</summary>
    public int OptedOutCount { get; set; }

    [MaxLength(500)]
    public string? Error { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }
}
