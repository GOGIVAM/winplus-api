namespace Backend.Utils;

/// <summary>
/// Lot 10.8 : normalisation des numéros de téléphone et détection de
/// l'opérateur Mobile Money, en un seul endroit (auparavant copiée dans
/// PaymentService, TeachingSessionService, TutorBookingService,
/// TutorEngagementService et WithdrawalService).
///
/// Le paiement reste camerounais (MTN et Orange via NotchPay, décision du
/// 2026-10-18) : les autres pays de la table servent à reconnaître le pays
/// d'un numéro, jamais à payer.
/// </summary>
public static class PhoneNumbers
{
    /// <param name="Code">Code pays ISO 3166-1 alpha-2.</param>
    /// <param name="DialCode">Indicatif international, sans « + ».</param>
    /// <param name="NationalLength">Nombre de chiffres du numéro national.</param>
    public sealed record CountryDialing(string Code, string DialCode, int NationalLength);

    /// <summary>Pays CEMAC (étude 10, C.3). Longueurs à recouper avec les plans de numérotation nationaux.</summary>
    public static readonly IReadOnlyList<CountryDialing> Cemac = new[]
    {
        new CountryDialing("CM", "237", 9),
        new CountryDialing("GA", "241", 8),
        new CountryDialing("CG", "242", 9),
        new CountryDialing("TD", "235", 8),
        new CountryDialing("CF", "236", 8),
        new CountryDialing("GQ", "240", 9),
    };

    public const string Cameroon = "CM";
    private const string CameroonDialCode = "237";

    /// <summary>Préfixes Orange Cameroun (trois premiers chiffres du numéro national) ; les autres mobiles vont à MTN.</summary>
    private static readonly HashSet<string> OrangeCameroonPrefixes = new()
    {
        "655", "656", "657", "658", "659", "690", "691", "692", "693", "694", "695", "696", "697", "698", "699",
    };

    public static string Digits(string? phone) => new((phone ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary>
    /// Chiffres du numéro au format international, sans « + » : 9 chiffres
    /// locaux reçoivent l'indicatif 237 ; un numéro déjà préfixé est inchangé.
    /// </summary>
    public static string ToE164Digits(string? phone)
    {
        var digits = Digits(phone);
        return digits.Length == 9 ? CameroonDialCode + digits : digits;
    }

    /// <summary>
    /// Retrait vers un mobile camerounais : 9 chiffres commençant par 6, avec ou
    /// sans 237 / 00237 ; renvoie 237 + 9 chiffres, ou null si le numéro est refusé.
    /// </summary>
    public static string? CameroonMobileOrNull(string? phone)
    {
        var digits = Digits(phone);
        if (digits.StartsWith("00237")) digits = digits[2..];
        if (digits.Length == 9 && digits[0] == '6') return CameroonDialCode + digits;
        if (digits.Length == 12 && digits.StartsWith("2376")) return digits;
        return null;
    }

    /// <summary>Canal NotchPay d'un numéro camerounais : « cm.orange » pour les préfixes Orange, sinon « cm.mtn ».</summary>
    public static string CameroonMobileMoneyChannel(string? phone)
    {
        var s = (phone ?? string.Empty).TrimStart('+');
        if (s.StartsWith(CameroonDialCode) && s.Length > 9) s = s[3..];
        if (s.Length < 3) return "cm.mtn";
        return OrangeCameroonPrefixes.Contains(s[..3]) ? "cm.orange" : "cm.mtn";
    }

    /// <summary>
    /// Pays d'un numéro saisi au format international (+241…, 00241…) parmi les
    /// pays CEMAC ; un numéro local de 9 chiffres est camerounais ; sinon null.
    /// </summary>
    public static string? CountryOf(string? phone)
    {
        var raw = (phone ?? string.Empty).Trim();
        var digits = Digits(raw);
        if (digits.StartsWith("00")) digits = digits[2..];
        else if (!raw.StartsWith('+') && digits.Length == 9) return Cameroon;
        foreach (var c in Cemac)
            if (digits.StartsWith(c.DialCode) && digits.Length == c.DialCode.Length + c.NationalLength) return c.Code;
        return null;
    }
}
