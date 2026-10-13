using Backend.Models.Entities;

namespace Backend.Services;

/// <summary>
/// Réglages serveur de la politique de tutorat (lot 9.2, suivi 17.8 et 17.9),
/// lus dans la configuration, avec des valeurs par défaut documentées. Aucun
/// secret : ces clés peuvent être posées dans la configuration ou en variables
/// d'environnement (<c>TutorBooking__DisputeWindowHours</c>…).
/// </summary>
public sealed record TutorPolicySettings(int DisputeWindowHours, decimal NotchPayFeePercent)
{
    /// <summary>Délai de contestation après la fin de séance (17.8) ; défaut 24 h.</summary>
    public const int DefaultDisputeWindowHours = 24;

    /// <summary>
    /// Frais NotchPay d'un encaissement Mobile Money, en pourcentage du montant
    /// payé. NotchPay ne renvoie pas ses frais dans la réponse d'initiation ni
    /// dans le webhook reçus par ce projet (aucun champ <c>fee</c> lu jusqu'ici,
    /// <c>Payment.FeeAmount</c> n'est jamais renseigné) : le taux est donc un
    /// réglage serveur, à aligner sur le contrat NotchPay. Défaut 3 %.
    /// </summary>
    public const decimal DefaultNotchPayFeePercent = 3m;

    public TimeSpan DisputeWindow => TimeSpan.FromHours(DisputeWindowHours);

    public static TutorPolicySettings From(IConfiguration? configuration)
    {
        var hours = configuration?.GetValue<int?>("TutorBooking:DisputeWindowHours") ?? DefaultDisputeWindowHours;
        var fee = configuration?.GetValue<decimal?>("TutorBooking:NotchPayFeePercent") ?? DefaultNotchPayFeePercent;
        // Bornes de sécurité : une faute de frappe ne doit ni supprimer la
        // fenêtre de contestation, ni bloquer l'argent du tuteur des semaines,
        // ni retenir des frais absurdes.
        return new TutorPolicySettings(Math.Clamp(hours, 1, 168), Math.Clamp(fee, 0m, 10m));
    }

    public static TutorPolicySettings Default { get; } = new(DefaultDisputeWindowHours, DefaultNotchPayFeePercent);
}

/// <summary>
/// Limites plateforme des réglages d'annulation du tuteur (17.10). Seule
/// source de ces bornes : validées à l'enregistrement du profil, appliquées au
/// calcul du remboursement, exposées par <c>GET /api/tutor-policy</c> pour que
/// le web et le mobile affichent exactement les mêmes valeurs.
///
/// Choix (2026-10-11) :
/// <list type="bullet">
///   <item>remboursement intégral si annulation au moins N h avant, N entre
///   12 et 48 : en dessous de 12 h le tuteur ne peut plus revendre le
///   créneau, au-delà de 48 h la règle devient dissuasive pour l'élève ;</item>
///   <item>part remboursée entre les deux seuils : 0 à 100 % ;</item>
///   <item>aucun remboursement en dessous de M h, M entre 0 et 12, et jamais
///   au-dessus du seuil intégral (garanti par les bornes : 12 ≤ 12).</item>
/// </list>
/// </summary>
public static class TutorPolicy
{
    public const int FullRefundHoursMin = 12;
    public const int FullRefundHoursMax = 48;
    public const int PartialRefundPercentMin = 0;
    public const int PartialRefundPercentMax = 100;
    public const int NoRefundHoursMin = 0;
    public const int NoRefundHoursMax = 12;

    public const int DefaultFullRefundHours = 24;
    public const int DefaultPartialRefundPercent = 50;
    public const int DefaultNoRefundHours = 2;

    /// <summary>Date de la version de la politique publique (décisions de la Partie 17).</summary>
    public const string Version = "2026-10-12";

    public sealed record CancellationSettings(int FullRefundHours, int PartialRefundPercent, int NoRefundHours);

    /// <summary>
    /// Réglages effectifs d'un tuteur : ses valeurs ramenées dans les bornes.
    /// Filet de sécurité si une valeur hors bornes subsiste en base (le script
    /// SQL_TutorWalletPaymentRefundsAndPolicy.sql les corrige une fois pour toutes).
    /// </summary>
    public static CancellationSettings Effective(TutorProfile? profile)
    {
        if (profile == null) return new(DefaultFullRefundHours, DefaultPartialRefundPercent, DefaultNoRefundHours);
        var full = Math.Clamp(profile.FullRefundHours, FullRefundHoursMin, FullRefundHoursMax);
        var partial = Math.Clamp(profile.PartialRefundPercent, PartialRefundPercentMin, PartialRefundPercentMax);
        var none = Math.Clamp(profile.NoRefundHours, NoRefundHoursMin, Math.Min(NoRefundHoursMax, full));
        return new(full, partial, none);
    }

    /// <summary>Lève <see cref="InvalidOperationException"/> (message affichable) si un réglage sort des bornes.</summary>
    public static void Validate(int fullRefundHours, int partialRefundPercent, int noRefundHours)
    {
        if (fullRefundHours < FullRefundHoursMin || fullRefundHours > FullRefundHoursMax)
            throw new InvalidOperationException(
                $"Le remboursement intégral doit s'appliquer pour une annulation entre {FullRefundHoursMin} h et {FullRefundHoursMax} h avant la séance.");
        if (partialRefundPercent < PartialRefundPercentMin || partialRefundPercent > PartialRefundPercentMax)
            throw new InvalidOperationException(
                $"La part remboursée doit être comprise entre {PartialRefundPercentMin} % et {PartialRefundPercentMax} %.");
        if (noRefundHours < NoRefundHoursMin || noRefundHours > NoRefundHoursMax)
            throw new InvalidOperationException(
                $"Le seuil sans remboursement doit être compris entre {NoRefundHoursMin} h et {NoRefundHoursMax} h avant la séance.");
        if (noRefundHours > fullRefundHours)
            throw new InvalidOperationException("Le seuil sans remboursement ne peut pas dépasser le seuil de remboursement intégral.");
    }
}

/// <summary>Cause d'un remboursement de séance : détermine qui supporte les frais NotchPay (17.9).</summary>
public enum TutorRefundCause
{
    /// <summary>Le payeur annule : politique du tuteur, frais NotchPay déduits du remboursement.</summary>
    PayerCancelled,
    /// <summary>Le tuteur annule : remboursement intégral, frais retenus sur le tuteur.</summary>
    TutorCancelled,
    /// <summary>Le tuteur refuse la demande : idem.</summary>
    TutorDeclined,
    /// <summary>Le tuteur ne répond pas dans le délai : idem.</summary>
    TutorNoResponse,
    /// <summary>Litige tranché en faveur du payeur : remboursement intégral, frais absorbés par la plateforme.</summary>
    DisputeRefundFull,
    /// <summary>Litige tranché par un remboursement partiel fixé par l'admin : frais absorbés par la plateforme.</summary>
    DisputeRefundPartial,
    /// <summary>
    /// Lot 9.5 (17.17) : demande présentielle en attente refusée d'office parce
    /// que le présentiel du tuteur est suspendu (seuil de signalements).
    /// Remboursement intégral, frais absorbés par la
    /// plateforme : mesure conservatoire automatique, sans faute établie du
    /// tuteur avant l'examen de l'admin (règle la plus proche : litige gagné par
    /// le payeur, et non refus du tuteur, qui lui ferait supporter les frais).
    /// </summary>
    InPersonBlocked,
}

/// <summary>Qui supporte les frais NotchPay d'une séance remboursée.</summary>
public static class TutorFeeBearer
{
    /// <summary>Aucun frais (paiement par portefeuille, ou rien à retenir).</summary>
    public const string None = "none";
    public const string Payer = "payer";
    public const string Tutor = "tutor";
    public const string Platform = "platform";
}

/// <summary>Moyen de paiement d'une séance (17.6).</summary>
public static class TutorPaymentMethods
{
    public const string MobileMoney = "mobile_money";
    public const string Wallet = "wallet";

    public static bool IsWallet(string? method) => string.Equals(method, Wallet, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Décision de remboursement d'une séance.
/// <para><see cref="RefundXaf"/> : crédit au portefeuille du payeur.</para>
/// <para><see cref="FeeXaf"/> : frais NotchPay du paiement d'origine (0 si payé par portefeuille).</para>
/// <para><see cref="FeeRetainedXaf"/> : part des frais retenue sur le payeur (déduite du remboursement) ou à retenir sur le tuteur ; 0 si absorbés par la plateforme.</para>
/// <para><see cref="TutorRetainedXaf"/> : montant brut conservé pour le tuteur (partagé avec la plateforme à la commission habituelle).</para>
/// </summary>
public sealed record TutorRefundDecision(
    TutorRefundCause Cause,
    int RefundPercent,
    decimal RefundXaf,
    decimal FeeXaf,
    string FeeBorneBy,
    decimal FeeRetainedXaf,
    decimal TutorRetainedXaf);

/// <summary>
/// Règle de remboursement du lot 9.2 (suivi 17.9), pure et testée
/// (TutorRefundRulesTests). Montants en XAF entiers ; P = prix payé,
/// F = frais NotchPay du paiement d'origine (0 si portefeuille).
///
/// <list type="bullet">
///   <item><b>Le payeur annule</b> : montant remboursable R = P × pourcentage
///   de la politique du tuteur. Remboursement = max(0, R − F). Le tuteur
///   conserve P − R (commission plateforme habituelle). Frais : payeur.</item>
///   <item><b>Le tuteur annule, refuse ou ne répond pas</b> : remboursement P.
///   Frais F retenus sur le tuteur (dans la limite de son solde disponible,
///   voir TutorRefundService). Le tuteur ne conserve rien.</item>
///   <item><b>Litige gagné par le payeur</b> : remboursement P, frais F
///   absorbés par la plateforme. Litige partiel : remboursement du montant
///   fixé par l'admin, le tuteur conserve le reste, frais absorbés par la
///   plateforme.</item>
/// </list>
/// </summary>
public static class TutorRefundRules
{
    /// <summary>Frais NotchPay d'un encaissement de <paramref name="priceXaf"/> au taux donné, arrondis au XAF.</summary>
    public static decimal NotchPayFee(decimal priceXaf, decimal feePercent) =>
        priceXaf <= 0 || feePercent <= 0 ? 0m : RevenueSplit.Xaf(priceXaf * feePercent / 100m);

    /// <summary>Pourcentage remboursable selon la politique (effective) du tuteur et le délai restant avant la séance.</summary>
    public static int RefundPercentFor(double hoursUntilSession, TutorPolicy.CancellationSettings policy)
    {
        if (hoursUntilSession >= policy.FullRefundHours) return 100;
        if (hoursUntilSession < policy.NoRefundHours) return 0;
        return Math.Clamp(policy.PartialRefundPercent, 0, 100);
    }

    public static TutorRefundDecision Compute(
        TutorRefundCause cause, decimal priceXaf, decimal paymentFeeXaf,
        int refundPercent = 100, decimal? disputeAmountXaf = null)
    {
        var price = RevenueSplit.Xaf(Math.Max(0, priceXaf));
        var fee = Math.Clamp(RevenueSplit.Xaf(paymentFeeXaf), 0, price);

        switch (cause)
        {
            case TutorRefundCause.PayerCancelled:
            {
                var percent = Math.Clamp(refundPercent, 0, 100);
                var refundable = RevenueSplit.Xaf(price * percent / 100m);
                var feeRetained = Math.Min(fee, refundable);
                var bearer = feeRetained > 0 ? TutorFeeBearer.Payer : TutorFeeBearer.None;
                return new(cause, percent, refundable - feeRetained, fee, bearer, feeRetained, price - refundable);
            }

            case TutorRefundCause.TutorCancelled:
            case TutorRefundCause.TutorDeclined:
            case TutorRefundCause.TutorNoResponse:
                return new(cause, 100, price, fee, fee > 0 ? TutorFeeBearer.Tutor : TutorFeeBearer.None, fee, 0m);

            case TutorRefundCause.DisputeRefundFull:
            case TutorRefundCause.InPersonBlocked:
                return new(cause, 100, price, fee, fee > 0 ? TutorFeeBearer.Platform : TutorFeeBearer.None, 0m, 0m);

            case TutorRefundCause.DisputeRefundPartial:
            {
                var amount = Math.Clamp(RevenueSplit.Xaf(disputeAmountXaf ?? 0), 0, price);
                var percent = price > 0 ? (int)Math.Round(amount * 100m / price, MidpointRounding.AwayFromZero) : 0;
                return new(cause, percent, amount, fee, fee > 0 ? TutorFeeBearer.Platform : TutorFeeBearer.None, 0m, price - amount);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(cause), cause, null);
        }
    }
}
