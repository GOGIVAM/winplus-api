using Backend.Data;
using Backend.Models.Entities;
using Backend.Services.Entitlements;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Recharge de tokens WinAI par le portefeuille (lot 9.7b, décisions 8.5 et
/// 18.17). Le prix au million et la taille d'un paquet sont fixés par
/// l'administration (réglages winai.topup.*). Les tokens achetés forment une
/// réserve (<see cref="AiTokenCredit"/>) utilisée seulement quand les limites
/// de session ou de semaine du plan sont atteintes (AiQuotaService). Droit
/// requis : <c>ai.topup</c>. Une offre « bonus_tokens » ou une remise ciblant
/// « token_topup » s'applique au paiement.
/// </summary>
public interface ITokenTopUpService
{
    /// <summary>Vrai : la recharge est disponible (le portefeuille existe depuis le lot 2).</summary>
    bool IsAvailable { get; }

    /// <summary>Message affiché au blocage 402 quand la recharge n'est pas proposée.</summary>
    string UnavailableReason { get; }

    Task<TokenTopUpQuote> QuoteAsync(int userId, int packs, string? promoCode, CancellationToken ct = default);

    /// <summary>Débite le portefeuille et crédite la réserve. Idempotent sur <paramref name="requestId"/>.</summary>
    Task<TokenTopUpResult> TopUpAsync(int userId, int packs, string? promoCode, string requestId, CancellationToken ct = default);

    /// <summary>Tokens rechargés encore disponibles (interne, jamais affiché en nombre brut, 8.3).</summary>
    Task<long> RemainingAsync(int userId, CancellationToken ct = default);
}

public sealed record TokenTopUpQuote(bool Allowed, int Packs, long Tokens, decimal PriceTtc, decimal DiscountTtc, decimal AmountDueTtc, long BonusTokens, string? Error);

public readonly record struct TokenTopUpResult(bool Succeeded, string Message, decimal AmountTtc = 0, long Tokens = 0);

public class TokenTopUpService : ITokenTopUpService
{
    public const int MaxPacks = 40;

    private readonly ApplicationDbContext _db;
    private readonly IWalletService _wallet;
    private readonly IEntitlementService _entitlements;
    private readonly IPromotionService _promotions;
    private readonly IPlatformSettings? _settings;

    public TokenTopUpService(ApplicationDbContext db, IWalletService wallet, IEntitlementService entitlements, IPromotionService promotions, IPlatformSettings? settings = null)
    {
        _db = db;
        _wallet = wallet;
        _entitlements = entitlements;
        _promotions = promotions;
        _settings = settings;
    }

    public bool IsAvailable => true;

    public string UnavailableReason => "Rechargez WinAI depuis votre portefeuille, ou passez à un plan supérieur.";

    private int PricePerMillion => _settings?.GetInt(PlatformSettingDefinitions.WinAiTopUpPricePerMillion) ?? 2000;
    private int PackTokens => _settings?.GetInt(PlatformSettingDefinitions.WinAiTopUpPackTokens) ?? 250_000;

    public async Task<TokenTopUpQuote> QuoteAsync(int userId, int packs, string? promoCode, CancellationToken ct = default)
    {
        packs = Math.Clamp(packs, 1, MaxPacks);
        var tokens = (long)packs * PackTokens;
        var price = Math.Round((decimal)tokens * PricePerMillion / 1_000_000m, 0, MidpointRounding.AwayFromZero);
        var ent = await _entitlements.GetAsync(userId, ct);
        if (!ent.Has(EntitlementCatalog.AiTopup))
            return new TokenTopUpQuote(false, packs, tokens, price, 0, price, 0, "La recharge WinAI n'est pas incluse dans votre plan.");
        var offer = await _promotions.QuoteAsync(userId, new PromotionContext(PromoCodeScopes.TokenTopUp, price), promoCode, ct);
        if (!string.IsNullOrWhiteSpace(promoCode) && !offer.Valid)
            return new TokenTopUpQuote(false, packs, tokens, price, 0, price, 0, offer.Error);
        var discount = offer.Valid ? offer.Discount : 0;
        return new TokenTopUpQuote(true, packs, tokens, price, discount, Math.Max(0, price - discount), offer.Valid ? offer.BonusTokens : 0, null);
    }

    public async Task<TokenTopUpResult> TopUpAsync(int userId, int packs, string? promoCode, string requestId, CancellationToken ct = default)
    {
        var key = $"token_topup:{userId}:{(string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId.Trim())}";
        if (key.Length > 80) key = key[..80];
        var existing = await _db.AiTokenCredits.AsNoTracking().FirstOrDefaultAsync(c => c.IdempotencyKey == key, ct);
        if (existing != null) return new TokenTopUpResult(true, "Recharge déjà enregistrée.", existing.PricePaidXaf, existing.Tokens);

        var quote = await QuoteAsync(userId, packs, promoCode, ct);
        if (!quote.Allowed) return new TokenTopUpResult(false, quote.Error ?? "Recharge impossible.");
        var role = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Role).FirstAsync(ct);
        try
        {
            await _wallet.RunLockedAsync(userId, async () =>
            {
                if (quote.AmountDueTtc > 0)
                {
                    var label = $"Recharge WinAI ({quote.Packs} paquet{(quote.Packs > 1 ? "s" : "")})";
                    if (role == "parent")
                        await _wallet.DebitParentWalletAsync(userId, quote.AmountDueTtc, key, label, "AiTokenCredit", 0);
                    else
                        await _wallet.DebitAsync(new WalletEntry(userId, WalletEntryTypes.TokenTopUp, quote.AmountDueTtc, key, label, "AiTokenCredit"));
                }
                _db.AiTokenCredits.Add(new AiTokenCredit
                {
                    UserId = userId,
                    Tokens = quote.Tokens + quote.BonusTokens,
                    PricePaidXaf = quote.AmountDueTtc,
                    Origin = "topup",
                    IdempotencyKey = key,
                    CreatedAt = DateTime.UtcNow,
                });
                await _db.SaveChangesAsync(ct);
                return true;
            });
        }
        catch (InsufficientWalletBalanceException ex)
        {
            return new TokenTopUpResult(false, ex.Message);
        }
        if (!string.IsNullOrWhiteSpace(promoCode))
        {
            var offer = await _promotions.QuoteAsync(userId, new PromotionContext(PromoCodeScopes.TokenTopUp, quote.PriceTtc), promoCode, ct);
            if (offer.Valid && offer.Promo != null)
                await _promotions.RecordUsageAsync(offer.Promo.Id, userId, null, null, PromoCodeScopes.TokenTopUp, quote.DiscountTtc, ct);
        }
        return new TokenTopUpResult(true, "Recharge WinAI effectuée.", quote.AmountDueTtc, quote.Tokens + quote.BonusTokens);
    }

    public async Task<long> RemainingAsync(int userId, CancellationToken ct = default) => await RemainingAsync(_db, userId, ct);

    public static async Task<long> RemainingAsync(ApplicationDbContext db, int userId, CancellationToken ct = default)
    {
        var bought = await db.AiTokenCredits.AsNoTracking().Where(c => c.UserId == userId).SumAsync(c => (long?)c.Tokens, ct) ?? 0;
        if (bought <= 0) return 0;
        var used = await db.AiTokenUsages.AsNoTracking().Where(u => u.UserId == userId && u.FromTopUp).SumAsync(u => (long?)u.TokensCharged, ct) ?? 0;
        return Math.Max(0, bought - used);
    }
}
