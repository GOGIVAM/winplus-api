using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

/// <summary>
/// Écriture du journal de portefeuille (Module 1, décision §14 du suivi).
///
/// Source de vérité unique de tous les soldes de la plateforme (professeur,
/// parent, trésorerie plateforme). Le solde disponible d'un propriétaire est
/// la somme des écritures <c>confirmed</c> qui lui appartiennent ; aucune
/// sommation des tables métier (commandes, réservations, commissions,
/// retraits) n'intervient plus dans un calcul de solde.
///
/// Règles :
/// <list type="bullet">
///   <item>une écriture n'est jamais supprimée ni modifiée dans son montant ;
///   seule sa transition de statut <c>pending</c> → <c>confirmed</c> ou
///   <c>pending</c> → <c>reversed</c> est admise ;</item>
///   <item>une écriture déjà confirmée ne se corrige que par une contre-passation
///   (<see cref="WalletEntryTypes.Reversal"/>, montant opposé, liée par
///   <see cref="ReversesEntryId"/>) ;</item>
///   <item><see cref="IdempotencyKey"/> est unique en base : un événement rejoué
///   (webhook reçu deux fois, tâche de fond relancée, double clic) ne peut pas
///   créer deux écritures.</item>
/// </list>
/// </summary>
public class WalletTransaction
{
    public long Id { get; set; }

    /// <summary>teacher | parent | user | platform (voir <see cref="WalletOwnerTypes"/>).</summary>
    [MaxLength(20)]
    public string OwnerType { get; set; } = WalletOwnerTypes.Teacher;

    /// <summary>Utilisateur propriétaire ; null pour la trésorerie plateforme.</summary>
    public int? OwnerId { get; set; }

    /// <summary>Type d'écriture (voir <see cref="WalletEntryTypes"/>).</summary>
    [MaxLength(40)]
    public string EntryType { get; set; } = null!;

    /// <summary>Montant signé en XAF, entier (positif = crédit, négatif = débit).</summary>
    public decimal Amount { get; set; }

    /// <summary>pending | confirmed | reversed (voir <see cref="WalletEntryStatus"/>).</summary>
    [MaxLength(20)]
    public string Status { get; set; } = WalletEntryStatus.Confirmed;

    /// <summary>Type de l'entité métier d'origine (OrderItem, Order, TutorBooking, AffiliateCommission, Withdrawal, WalletTopUp, TeacherClassContent, AdminCredit…).</summary>
    [MaxLength(40)]
    public string? SourceType { get; set; }

    public int? SourceId { get; set; }

    /// <summary>Garde d'unicité de l'événement d'origine (unique en base).</summary>
    [MaxLength(120)]
    public string IdempotencyKey { get; set; } = null!;

    /// <summary>Écriture contre-passée par celle-ci (pour une <see cref="WalletEntryTypes.Reversal"/>).</summary>
    public long? ReversesEntryId { get; set; }

    /// <summary>Libellé lisible affiché dans l'historique.</summary>
    [MaxLength(300)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Utilisateur qui a déclenché l'écriture (administrateur pour un crédit manuel ou une action de secours).</summary>
    public int? CreatedByUserId { get; set; }

    /// <summary>Date de l'événement métier (vente, libération d'escrow…), distincte de la date d'écriture pour la reprise d'historique.</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Date de passage à <c>confirmed</c> ou <c>reversed</c>.</summary>
    public DateTime? SettledAt { get; set; }

    /// <summary>
    /// Échéance facultative. Inutilisée par le portefeuille professeur ; prévue
    /// pour la dotation mensuelle expirable du portefeuille parent (Module 14).
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>Types de propriétaire d'un portefeuille.</summary>
public static class WalletOwnerTypes
{
    public const string Teacher = "teacher";
    public const string Parent = "parent";
    /// <summary>Tout autre compte individuel (par exemple un administrateur auteur d'un contenu vendu).</summary>
    public const string User = "user";
    public const string Platform = "platform";

    /// <summary>Catégorie de portefeuille déduite du rôle du compte au moment de l'écriture.</summary>
    public static string ForRole(string? role) => (role ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "teacher" => Teacher,
        "parent" => Parent,
        _ => User,
    };
}

/// <summary>Types d'écriture (décision §14 du suivi).</summary>
public static class WalletEntryTypes
{
    public const string CatalogSale = "CatalogSale";
    public const string TutoringRevenue = "TutoringRevenue";
    public const string AffiliateCommission = "AffiliateCommission";
    public const string Recharge = "Recharge";
    public const string AdminCredit = "AdminCredit";
    public const string WithdrawalRequested = "WithdrawalRequested";
    public const string WithdrawalProcessed = "WithdrawalProcessed";
    public const string PlatformCommission = "PlatformCommission";
    public const string Reversal = "Reversal";

    /// <summary>Achat catalogue réglé avec le solde (paiement par solde du professeur).</summary>
    public const string BalancePurchase = "BalancePurchase";

    /// <summary>Contenu assigné à une classe, facturé sur le solde du professeur.</summary>
    public const string ClassAssignment = "ClassAssignment";

    /// <summary>Solde d'ouverture (réservé à une reprise par solde unique ; non utilisé par la reprise actuelle, qui rejoue les événements).</summary>
    public const string OpeningBalance = "OpeningBalance";
}

public static class WalletEntryStatus
{
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Reversed = "reversed";
}

/// <summary>
/// Intention de recharge personnelle (Module 3). La commande porteuse du
/// paiement NotchPay est liée ici par une donnée structurée, relue à la
/// confirmation du paiement : jamais par un texte libre dans Order.Notes.
/// </summary>
public class WalletTopUp
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>Commande porteuse (sans ligne de contenu) passée dans le circuit de paiement existant.</summary>
    public int OrderId { get; set; }

    public decimal AmountXaf { get; set; }

    /// <summary>pending | completed | failed.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    /// <summary>Identifiant de la soumission côté client (double clic) ; unique par utilisateur.</summary>
    [MaxLength(64)]
    public string? ClientRequestId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }
}
