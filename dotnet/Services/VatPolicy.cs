namespace Backend.Services;

/// <summary>
/// TVA des achats du catalogue et des formations.
///
/// Lot 9.7b (suivi 18.21, défaut F11) : les prix du catalogue (épreuves,
/// livres) et des formations sont des prix TTC saisis par le vendeur ou
/// l'administrateur : le prix saisi est le prix payé. La TVA n'est plus ajoutée
/// à la commande, elle est calculée À L'INTÉRIEUR du prix TTC, côté serveur,
/// pour la facture et les écritures :
///   HT  = arrondi(TTC / 1,1925) à l'XAF ;
///   TVA = TTC − HT (jamais arrondie séparément, donc HT + TVA = TTC exactement).
/// Le revenu du vendeur et la commission de la plateforme se calculent sur ce
/// HT déduit (OrderItem.PriceAtPurchase reste le prix HT de la ligne), sans
/// changer les règles de répartition.
///
/// Décision 10.1 (toujours valable) : Order.TotalAmount est le montant TTC
/// payé, comparé sans tolérance par le contrôle de montant du webhook NotchPay.
///
/// Arrondi : le XAF n'a pas de sous-unité, au plus proche, moitié vers le haut.
///
/// Les abonnements ne sont pas concernés (chantier A du 9.7b) : prix TTC du
/// plan, affiché et payé tel quel.
/// </summary>
public static class VatPolicy
{
    /// <summary>Taux de TVA (19,25 %, TVA CEMAC au Cameroun).</summary>
    public const decimal CatalogRate = 0.1925m;

    /// <summary>
    /// Lot 10.8 : taux par pays du client (ISO 3166-1 alpha-2). Seul le
    /// Cameroun est ouvert : la TVA d'un service numérique vendu hors du
    /// Cameroun reste à trancher avec un expert-comptable (étude 10, décision
    /// Q9 c), et aucune vente hors Cameroun n'est faite d'ici là.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, decimal> RatesByCountry = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
    {
        ["CM"] = CatalogRate,
    };

    /// <summary>Pays où la vente est ouverte.</summary>
    public static IEnumerable<string> OpenCountries => RatesByCountry.Keys;

    /// <summary>Taux du pays (Cameroun si absent) ; un pays non ouvert à la vente lève une erreur plutôt qu'un taux deviné.</summary>
    public static decimal RateFor(string? country)
    {
        var code = string.IsNullOrWhiteSpace(country) ? "CM" : country.Trim();
        return RatesByCountry.TryGetValue(code, out var rate)
            ? rate
            : throw new NotSupportedException($"Vente non ouverte pour le pays {code} : taux de TVA non fixé.");
    }

    /// <summary>Prix hors taxe déduit d'un prix TTC (XAF entier).</summary>
    public static decimal ExclTaxFromInclTax(decimal amountInclTax) => ExclTaxFromInclTax(amountInclTax, null);

    /// <summary>Prix hors taxe déduit d'un prix TTC, au taux du pays du client.</summary>
    public static decimal ExclTaxFromInclTax(decimal amountInclTax, string? country)
    {
        if (amountInclTax <= 0) return 0m;
        return decimal.Round(amountInclTax / (1m + RateFor(country)), 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>TVA contenue dans un prix TTC : TTC − HT déduit.</summary>
    public static decimal TaxIncludedIn(decimal amountInclTax) => TaxIncludedIn(amountInclTax, null);

    /// <summary>TVA contenue dans un prix TTC, au taux du pays du client.</summary>
    public static decimal TaxIncludedIn(decimal amountInclTax, string? country) =>
        amountInclTax <= 0 ? 0m : amountInclTax - ExclTaxFromInclTax(amountInclTax, country);

    /// <summary>
    /// Ancienne règle (prix HT + 19,25 %), conservée pour la règle de migration
    /// des prix vers le TTC (SQL_CourseSalesTtc97b.sql) : le nouveau prix TTC est
    /// le prix réellement payé avant le 9.7b, soit HT + arrondi(HT × 19,25 %).
    /// </summary>
    public static decimal LegacyTotalInclTax(decimal amountExclTax)
    {
        if (amountExclTax <= 0) return 0m;
        return amountExclTax + decimal.Round(amountExclTax * CatalogRate, 0, MidpointRounding.AwayFromZero);
    }
}
