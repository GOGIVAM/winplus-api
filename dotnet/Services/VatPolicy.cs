namespace Backend.Services;

/// <summary>
/// TVA appliquée aux achats du catalogue (décision 10.1 du suivi).
///
/// La taxe est calculée et stockée côté serveur, dans <c>Order.TotalAmount</c> :
/// le client n'a plus à la recalculer, il affiche et paie le total renvoyé par
/// la création de commande. Le contrôle de montant du webhook NotchPay
/// (PaymentService.DetectAmountMismatchAsync) compare le montant payé à ce
/// total TTC, sans tolérance.
///
/// Taux : 19,25 % (TVA CEMAC, Cameroun), le même que celui affiché par le web
/// (CartContext.TAX_RATE, pages panier et paiement). Aucun paramètre serveur
/// ne portait ce taux jusqu'ici : il est centralisé ici, seul endroit à
/// modifier.
///
/// Arrondi : le XAF n'a pas de sous-unité, la taxe est arrondie à l'unité,
/// au plus proche, moitié vers le haut (identique au Math.round du web pour
/// un montant positif).
///
/// Périmètre : commandes du catalogue (OrderService). Les abonnements ne sont
/// pas concernés : aucun client (web SubscribeModal, mobile
/// subscription_service) ni le serveur (SubscriptionsController.Purchase)
/// n'ajoute de taxe au prix du plan, affiché et payé tel quel. Les y
/// soumettre changerait le prix payé par les abonnés : décision produit
/// distincte, non prise ici.
/// </summary>
public static class VatPolicy
{
    /// <summary>Taux de TVA du catalogue (19,25 %).</summary>
    public const decimal CatalogRate = 0.1925m;

    /// <summary>Montant de TVA en XAF pour une base hors taxe.</summary>
    public static decimal TaxOn(decimal amountExclTax)
    {
        if (amountExclTax <= 0) return 0m;
        return decimal.Round(amountExclTax * CatalogRate, 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>Total toutes taxes comprises pour une base hors taxe.</summary>
    public static decimal TotalInclTax(decimal amountExclTax) =>
        amountExclTax + TaxOn(amountExclTax);
}
