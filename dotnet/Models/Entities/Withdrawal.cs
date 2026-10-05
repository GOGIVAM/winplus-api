using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

/// <summary>
/// Demande de retrait Mobile Money du solde WinPlus.
///
/// Lot 2, Module 2 (décision §15 du suivi) : le virement est automatisé par
/// l'API de transfert NotchPay. La demande réserve immédiatement les fonds
/// dans le journal de portefeuille (écriture <c>WithdrawalRequested</c>), puis
/// le transfert est créé chez NotchPay avec la référence propre à
/// l'application <see cref="TransferReference"/>, qui permet de le retrouver
/// (réconciliation) même si la réponse du fournisseur a été perdue. Le statut
/// suit le statut réel du transfert ; un échec restitue les fonds par une
/// contre-passation.
///
/// Statuts :
/// <list type="bullet">
///   <item><c>pending</c> : demande enregistrée, fonds réservés, transfert pas encore confirmé par NotchPay ;</item>
///   <item><c>processing</c> : transfert accepté, en cours chez NotchPay ;</item>
///   <item><c>completed</c> : transfert abouti ;</item>
///   <item><c>failed</c> : transfert échoué, fonds restitués ;</item>
///   <item><c>cancelled</c> : transfert annulé, fonds restitués.</item>
/// </list>
/// Une demande antérieure à l'automatisation (virement manuel) n'a pas de
/// <see cref="TransferReference"/>.
/// </summary>
public class Withdrawal
{
    public int Id { get; set; }
    public int UserId { get; set; }

    /// <summary>mtn | orange.</summary>
    [MaxLength(20)]
    public string Operator { get; set; } = "mtn";

    /// <summary>Numéro normalisé au format international sans « + » (237XXXXXXXXX).</summary>
    [MaxLength(30)]
    public string Phone { get; set; } = null!;

    public decimal AmountXaf { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    /// <summary>Motif d'une action administrateur (action de secours, traitement manuel historique).</summary>
    [MaxLength(300)]
    public string? AdminNote { get; set; }

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }

    /// <summary>Référence propre à l'application, transmise à NotchPay (unique) ; null pour un retrait manuel historique.</summary>
    [MaxLength(60)]
    public string? TransferReference { get; set; }

    /// <summary>Identifiant du transfert chez NotchPay (trf_…), une fois connu.</summary>
    [MaxLength(80)]
    public string? ProviderTransferId { get; set; }

    /// <summary>Motif d'échec exploitable, renvoyé par NotchPay ou constaté par l'application.</summary>
    [MaxLength(300)]
    public string? FailureReason { get; set; }

    /// <summary>Dernière consultation du statut chez NotchPay.</summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>Identifiant de la soumission côté client (double clic, rejeu réseau) ; unique par utilisateur.</summary>
    [MaxLength(64)]
    public string? ClientRequestId { get; set; }

    /// <summary>Demande en échec dont celle-ci est le rejeu (action de secours administrateur).</summary>
    public int? RetryOfWithdrawalId { get; set; }

    /// <summary>Clôture définitive d'une demande en échec par un administrateur.</summary>
    public DateTime? ClosedAt { get; set; }
    public int? ClosedByUserId { get; set; }

    /// <summary>Administrateur ayant déclenché un rejeu ou un traitement manuel.</summary>
    public int? ActionByUserId { get; set; }

    /// <summary>
    /// Anomalie détectée et signalée, jamais corrigée silencieusement (par
    /// exemple un transfert abouti chez NotchPay après restitution des fonds).
    /// </summary>
    [MaxLength(300)]
    public string? Anomaly { get; set; }
}

/// <summary>Valeurs de statut d'un retrait.</summary>
public static class WithdrawalStatus
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    public static readonly string[] All = { Pending, Processing, Completed, Failed, Cancelled };

    /// <summary>Retrait en cours : fonds réservés, issue non encore connue.</summary>
    public static bool IsInFlight(string? status) => status is Pending or Processing;
}
