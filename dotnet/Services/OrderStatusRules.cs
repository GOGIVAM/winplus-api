namespace Backend.Services;

/// <summary>
/// Règles de statut d'une commande côté client (annulation, nouveau paiement),
/// partagées par tous les chemins qui peuvent changer le statut d'une commande
/// à la demande de son propriétaire.
///
/// Décision 4.D du suivi : l'accès au contenu et le revenu du vendeur d'une
/// commande déjà réglée, ou en demande de remboursement, ne changent qu'après
/// une décision administrateur explicite. Chaque chemin client qui pouvait
/// ramener une telle commande à un état annulable contournait cette règle :
/// <list type="bullet">
///   <item>10.3 <c>POST /orders/{id}/cancel</c> sur une commande <c>refund_requested</c> ;</item>
///   <item>10.5 <c>POST /payments/initiate</c> et <c>POST /payments/{id}/retry</c>,
///   qui repassaient la commande en <c>pending</c> (ou <c>failed</c>), donc annulable ;</item>
///   <item>10.6 <c>POST /orders/{id}/cancel</c> sur une commande <c>paid</c>
///   (achat parent → enfant en crédits).</item>
///   <item>10.11 annulation ou nouveau paiement sur une commande <c>refunded</c>
///   (trace du remboursement effacée, double débit sur une commande payée par solde).</item>
/// </list>
/// Les messages sont centralisés ici pour que l'annulation directe et les
/// chemins de paiement renvoient exactement la même explication.
/// </summary>
public static class OrderStatusRules
{
    /// <summary>Constat commun à tous les refus liés à un remboursement en cours.</summary>
    public const string RefundPendingNotice =
        "Cette commande fait l'objet d'une demande de remboursement en cours d'examen.";

    public const string RefundPendingCancelMessage =
        RefundPendingNotice + " Elle ne peut pas être annulée : le remboursement sera traité par un administrateur.";

    public const string RefundPendingPaymentMessage =
        RefundPendingNotice + " Aucun nouveau paiement ne peut être lancé : le remboursement sera traité par un administrateur.";

    public const string CompletedCancelMessage =
        "Impossible d'annuler une commande déjà complétée";

    public const string PaidCancelMessage =
        "Cette commande a déjà été réglée avec des crédits. Elle ne peut pas être annulée : "
        + "pour un remboursement, contacte le support, la demande sera traitée par un administrateur.";

    public const string AlreadySettledPaymentMessage =
        "Cette commande est déjà réglée : aucun nouveau paiement ne peut être lancé.";

    /// <summary>
    /// Décision 10.11 : une commande <c>refunded</c> est close par une décision
    /// administrateur. L'annuler effacerait la trace du remboursement ; la
    /// repayer produirait un double débit (commande payée par solde, déjà
    /// recréditée) sans jamais rouvrir l'accès proprement.
    /// </summary>
    public const string RefundedNotice = "Cette commande a déjà été remboursée.";

    public const string RefundedCancelMessage =
        RefundedNotice + " Elle ne peut plus être annulée.";

    public const string RefundedPaymentMessage =
        RefundedNotice + " Aucun nouveau paiement ne peut être lancé : pour racheter ce contenu, passe une nouvelle commande.";

    private static string Normalize(string? status) => (status ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Motif de refus d'une annulation demandée par le client, ou null si
    /// l'annulation est permise.
    /// </summary>
    public static string? CancellationBlockReason(string? status) => Normalize(status) switch
    {
        "completed" => CompletedCancelMessage,
        "paid" => PaidCancelMessage,
        "refund_requested" => RefundPendingCancelMessage,
        "refunded" => RefundedCancelMessage,
        _ => null,
    };

    /// <summary>
    /// Motif de refus d'un nouveau paiement (initiation ou relance) sur la
    /// commande, ou null si le paiement est permis.
    ///
    /// Une commande déjà réglée (<c>completed</c>, <c>paid</c>) est aussi
    /// refusée : l'échec ou l'expiration de ce paiement superflu la ferait
    /// passer en <c>failed</c>, puis la relance en <c>pending</c>, deux états
    /// annulables le même contournement que pour <c>refund_requested</c>,
    /// en plus d'un double encaissement.
    /// </summary>
    public static string? PaymentBlockReason(string? status) => Normalize(status) switch
    {
        "refund_requested" => RefundPendingPaymentMessage,
        "completed" or "paid" => AlreadySettledPaymentMessage,
        "refunded" => RefundedPaymentMessage,
        _ => null,
    };

    /// <summary>
    /// Statut protégé : commande réglée (<c>completed</c>, <c>paid</c>), en
    /// demande de remboursement (<c>refund_requested</c>) ou remboursée
    /// (<c>refunded</c>, décision 10.11). Même liste que celle
    /// de <see cref="PaymentBlockReason"/>, dont elle est dérivée pour ne pas
    /// en maintenir une copie.
    /// </summary>
    public static bool IsProtected(string? status) => PaymentBlockReason(status) != null;

    /// <summary>
    /// Décision 10.9 : une transition automatique (expiration d'un paiement,
    /// webhook ou synchronisation du fournisseur) ne doit jamais faire passer
    /// une commande protégée vers un statut non protégé (<c>failed</c>,
    /// <c>pending</c>, <c>cancelled</c>…), qui la rendrait de nouveau annulable
    /// ou payable. Retourne true si la mise à jour du statut de la commande
    /// doit être ignorée.
    ///
    /// Décision 10.11 : <c>refunded</c> est en plus terminal pour ces chemins
    /// automatiques. Un paiement resté en cours avant la demande de
    /// remboursement et confirmé après l'approbation ne doit pas la ramener en
    /// <c>completed</c> (statut pourtant protégé) : cela rouvrirait l'accès et
    /// relancerait l'activation d'un abonnement sur une commande remboursée.
    /// </summary>
    public static bool BlocksAutomaticTransition(string? currentStatus, string? targetStatus)
        => Normalize(currentStatus) == RefundedStatus
            ? Normalize(targetStatus) != RefundedStatus
            : IsProtected(currentStatus) && !IsProtected(targetStatus);

    private const string RefundedStatus = "refunded";
}

/// <summary>
/// Paiement refusé parce que l'état de la commande ne le permet pas (voir
/// <see cref="OrderStatusRules.PaymentBlockReason"/>). Dérive de
/// <see cref="InvalidOperationException"/> pour rester compatible avec les
/// gestionnaires existants, mais permet au contrôleur de renvoyer le motif
/// tel quel plutôt que le code générique « payment_rejected » réservé aux
/// refus de l'opérateur.
/// </summary>
public class OrderNotPayableException : InvalidOperationException
{
    public OrderNotPayableException(string message) : base(message) { }
}
