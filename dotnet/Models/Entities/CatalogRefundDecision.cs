using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

/// <summary>
/// Décision de remboursement au portefeuille d'une commande catalogue
/// approuvée par un administrateur (lot 9.7a, suivi 18.5). Figée une seule
/// fois, dans la transaction de l'approbation (clé primaire = commande) ;
/// les écritures du registre en sont déduites par
/// <c>WalletService.SyncOrderAsync</c>, idempotent et rejoué par la
/// réconciliation. Une commande remboursée avant le 9.7a n'a pas de décision :
/// elle n'est jamais créditée rétroactivement.
///
/// Règle des frais du 9.2 (payeur à l'origine de la demande) : la part payée
/// par Mobile Money est remboursée moins les frais NotchPay de l'encaissement
/// d'origine ; la part payée par le solde professeur est restituée par la
/// contre-passation de son débit (déjà existante, hors de cette décision) ;
/// un achat réglé par le portefeuille parent est recrédité en entier (aucun
/// frais).
/// </summary>
public class CatalogRefundDecision
{
    [Key]
    public int OrderId { get; set; }

    /// <summary>Acheteur crédité.</summary>
    public int BuyerUserId { get; set; }

    /// <summary>Montant total de la commande au moment de la décision.</summary>
    public decimal OrderTotalXaf { get; set; }

    /// <summary>Part réglée par le solde professeur (restituée par contre-passation, pas créditée ici).</summary>
    public decimal BalancePartXaf { get; set; }

    /// <summary>Frais NotchPay de l'encaissement d'origine, retenus sur le remboursement.</summary>
    public decimal FeeXaf { get; set; }

    /// <summary>Crédit au portefeuille de l'acheteur.</summary>
    public decimal RefundXaf { get; set; }

    /// <summary>mobile_money | parent_wallet | balance.</summary>
    [MaxLength(20)]
    public string PaymentKind { get; set; } = "mobile_money";

    public int? DecidedByUserId { get; set; }

    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;
}
