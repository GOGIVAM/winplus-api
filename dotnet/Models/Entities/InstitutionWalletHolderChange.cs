using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

/// <summary>
/// Changement de titulaire du portefeuille d'une institution (suivi 17.14),
/// décidé par un administrateur WinPlus. Trace qui a agi, quand, l'ancien et
/// le nouveau titulaire et le montant transféré ; les deux écritures du
/// registre (débit de l'ancien titulaire, crédit du nouveau) portent
/// SourceType « InstitutionWalletHolderChange » et l'identifiant de cette ligne.
/// Jamais modifiée ni supprimée.
/// </summary>
public class InstitutionWalletHolderChange
{
    public int Id { get; set; }

    public int InstitutionId { get; set; }

    /// <summary>Null si le portefeuille n'avait encore jamais été ouvert (aucun titulaire).</summary>
    public int? OldHolderUserId { get; set; }

    public int NewHolderUserId { get; set; }

    /// <summary>Solde disponible transféré (0 si le portefeuille était vide).</summary>
    public decimal AmountXaf { get; set; }

    public int ChangedByUserId { get; set; }

    [MaxLength(300)]
    public string? Reason { get; set; }

    /// <summary>Identifiant de la soumission (double clic, rejeu réseau) : unique par institution.</summary>
    [MaxLength(64)]
    public string? ClientRequestId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
