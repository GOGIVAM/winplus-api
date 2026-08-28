using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Mouvement de crédits mensuels d'un parent (S3-1).
/// Type "allocation" = dotation du plan, "consumption" = achat pour un enfant.
/// Le solde est la somme des mouvements de la période courante — jamais une
/// valeur codée en dur.
/// </summary>
public class ParentCreditLedger
{
    public int Id { get; set; }

    public int ParentId { get; set; }

    /// <summary>allocation | consumption | refund</summary>
    [Required, MaxLength(20)]
    public required string EntryType { get; set; }

    /// <summary>Montant en XAF, toujours positif ; le signe vient de EntryType.</summary>
    public decimal Amount { get; set; }

    public int? ChildId { get; set; }

    public int? OrderId { get; set; }

    [MaxLength(300)]
    public string? Label { get; set; }

    /// <summary>Premier jour du mois de rattachement (UTC).</summary>
    public DateTime PeriodStart { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ParentId))]
    public User? Parent { get; set; }

    [ForeignKey(nameof(ChildId))]
    public User? Child { get; set; }
}
