using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;

namespace Backend.Services;

public interface IAffiliateService
{
    Task<AffiliateAccountDto> GetOrCreateMyAccountAsync(int userId);
    Task<AffiliateStatsDto?> GetMyStatsAsync(int userId);
    Task<List<AffiliateCommissionDto>> GetMyCommissionsAsync(int userId);
    Task<bool> RecordClickAsync(TrackAffiliateClickRequest request);
    Task RecordCommissionForOrderAsync(int orderId);

    // Admin
    Task<AffiliateSettingsDto> GetSettingsAsync();
    Task<AffiliateSettingsDto> UpdateSettingsAsync(UpdateAffiliateSettingsRequest request);
    Task<List<AdminAffiliateAccountDto>> ListAccountsAsync();
    Task<bool> SetAccountStatusAsync(int accountId, string status);
    Task RecalculateAllRatesAsync(CancellationToken ct = default);
}

public class AffiliateService : IAffiliateService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IFastApiClient _fastApi;
    private readonly ILogger<AffiliateService> _logger;

    public AffiliateService(ApplicationDbContext db, IConfiguration configuration, IFastApiClient fastApi, ILogger<AffiliateService> logger)
    {
        _db = db;
        _configuration = configuration;
        _fastApi = fastApi;
        _logger = logger;
    }

    private string FrontendUrl => (_configuration["App:FrontendUrl"] ?? "https://winplus.cm").TrimEnd('/');

    private AffiliateAccountDto ToDto(AffiliateAccount a) =>
        new(a.Id, a.Code, $"{FrontendUrl}/?ref={a.Code}", a.CommissionRate, a.Status, a.CreatedAt);

    public async Task<AffiliateAccountDto> GetOrCreateMyAccountAsync(int userId)
    {
        var existing = await _db.AffiliateAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId);
        if (existing != null) return ToDto(existing);

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("Utilisateur introuvable");

        if (!string.Equals(user.Role, "teacher", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Le programme d'affiliation est réservé aux comptes professeur/tuteur.");

        var settings = await GetOrCreateSettingsEntityAsync();
        var startingRate = Math.Min(5m, settings.CommissionRateCapPercent);

        var account = new AffiliateAccount
        {
            UserId = userId,
            Code = await GenerateUniqueCodeAsync(user),
            CommissionRate = startingRate,
            Status = "active",
        };
        _db.AffiliateAccounts.Add(account);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Compte affilié créé pour l'utilisateur {UserId} avec le code {Code}", userId, account.Code);
        return ToDto(account);
    }

    private async Task<string> GenerateUniqueCodeAsync(User user)
    {
        var baseSlug = (user.FirstName ?? "aff")
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .Aggregate(string.Empty, (acc, c) => acc + c);
        if (baseSlug.Length < 3) baseSlug = "AFF";
        if (baseSlug.Length > 8) baseSlug = baseSlug[..8];

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var candidate = $"{baseSlug}{Random.Shared.Next(100, 999)}";
            var taken = await _db.AffiliateAccounts.AsNoTracking().AnyAsync(a => a.Code == candidate);
            if (!taken) return candidate;
        }
        // Filet de sécurité si 20 collisions de suite (statistiquement quasi impossible).
        return $"AFF{Guid.NewGuid():N}"[..12].ToUpperInvariant();
    }

    public async Task<AffiliateStatsDto?> GetMyStatsAsync(int userId)
    {
        var account = await _db.AffiliateAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId);
        if (account == null) return null;

        var totalClicks = await _db.AffiliateClicks.AsNoTracking().CountAsync(c => c.AffiliateAccountId == account.Id);
        var commissions = await _db.AffiliateCommissions.AsNoTracking()
            .Where(c => c.AffiliateAccountId == account.Id)
            .ToListAsync();

        return new AffiliateStatsDto(
            ToDto(account),
            totalClicks,
            commissions.Count(c => c.Status != "reversed"),
            commissions.Where(c => c.Status == "pending").Sum(c => c.CommissionAmount),
            commissions.Where(c => c.Status == "confirmed").Sum(c => c.CommissionAmount),
            commissions.Where(c => c.Status == "paid").Sum(c => c.CommissionAmount));
    }

    public async Task<List<AffiliateCommissionDto>> GetMyCommissionsAsync(int userId)
    {
        var account = await _db.AffiliateAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId);
        if (account == null) return new List<AffiliateCommissionDto>();

        return await _db.AffiliateCommissions.AsNoTracking()
            .Where(c => c.AffiliateAccountId == account.Id)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new AffiliateCommissionDto(
                c.Id, c.OrderId, c.Order.OrderNumber, c.OrderAmount, c.CommissionRateApplied,
                c.CommissionAmount, c.Status, c.CreatedAt, c.ConfirmedAt))
            .ToListAsync();
    }

    public async Task<bool> RecordClickAsync(TrackAffiliateClickRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.VisitorToken))
            return false;

        var account = await _db.AffiliateAccounts.FirstOrDefaultAsync(a => a.Code == request.Code && a.Status == "active");
        if (account == null) return false;

        // Anti-spam simple v1 : un rechargement de page ne doit pas gonfler le
        // compteur de clics utilisé ensuite comme signal de qualité pour le
        // calcul du taux — un même visiteur ne compte qu'une fois par jour.
        var today = DateTime.UtcNow.Date;
        var alreadyClickedToday = await _db.AffiliateClicks.AsNoTracking()
            .AnyAsync(c => c.AffiliateAccountId == account.Id && c.VisitorToken == request.VisitorToken && c.ClickedAt >= today);
        if (alreadyClickedToday) return true;

        _db.AffiliateClicks.Add(new AffiliateClick
        {
            AffiliateAccountId = account.Id,
            VisitorToken = request.VisitorToken,
            LandingPath = request.LandingPath,
        });
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task RecordCommissionForOrderAsync(int orderId)
    {
        try
        {
            // Idempotent : le webhook de paiement peut être livré plusieurs fois.
            var already = await _db.AffiliateCommissions.AsNoTracking().AnyAsync(c => c.OrderId == orderId);
            if (already) return;

            var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null || string.IsNullOrWhiteSpace(order.ReferralCode)) return;

            var settings = await GetOrCreateSettingsEntityAsync();
            var attributionCutoff = order.OrderDate.AddDays(-settings.AttributionWindowDays);
            // Le code est capturé au clic ; on ne peut pas revalider l'âge exact du
            // clic ici sans le rejouer côté frontend, donc on fait confiance au
            // frontend pour n'envoyer un code que dans sa fenêtre — voir
            // affiliateTracking.ts. On vérifie seulement que le compte existe et
            // qu'il ne s'agit pas d'un auto-parrainage.

            var account = await _db.AffiliateAccounts.FirstOrDefaultAsync(a => a.Code == order.ReferralCode && a.Status == "active");
            if (account == null) return;

            if (order.UserId.HasValue && order.UserId.Value == account.UserId)
            {
                _logger.LogWarning("Auto-parrainage détecté et ignoré pour la commande {OrderId} (affilié {UserId})", orderId, account.UserId);
                return;
            }

            var commissionAmount = Math.Round(order.TotalAmount * account.CommissionRate / 100m, 2);
            if (commissionAmount <= 0) return;

            _db.AffiliateCommissions.Add(new AffiliateCommission
            {
                AffiliateAccountId = account.Id,
                OrderId = order.Id,
                BuyerUserId = order.UserId,
                OrderAmount = order.TotalAmount,
                CommissionRateApplied = account.CommissionRate,
                CommissionAmount = commissionAmount,
                Status = "pending",
            });
            await _db.SaveChangesAsync();

            _logger.LogInformation("Commission d'affiliation créée : {Amount} XAF pour l'affilié {UserId} sur la commande {OrderId}",
                commissionAmount, account.UserId, orderId);
        }
        catch (Exception ex)
        {
            // Ne doit jamais faire échouer la confirmation de paiement elle-même.
            _logger.LogError(ex, "Erreur lors de l'attribution de la commission d'affiliation pour la commande {OrderId}", orderId);
        }
    }

    private async Task<AffiliateSettings> GetOrCreateSettingsEntityAsync()
    {
        var settings = await _db.AffiliateSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings != null) return settings;

        settings = new AffiliateSettings { Id = 1 };
        _db.AffiliateSettings.Add(settings);
        await _db.SaveChangesAsync();
        return settings;
    }

    public async Task<AffiliateSettingsDto> GetSettingsAsync()
    {
        var s = await GetOrCreateSettingsEntityAsync();
        return new AffiliateSettingsDto(s.CommissionRateCapPercent, s.AttributionWindowDays, s.HoldPeriodDays);
    }

    public async Task<AffiliateSettingsDto> UpdateSettingsAsync(UpdateAffiliateSettingsRequest request)
    {
        var s = await GetOrCreateSettingsEntityAsync();
        s.CommissionRateCapPercent = Math.Clamp(request.CommissionRateCapPercent, 0, 100);
        s.AttributionWindowDays = Math.Clamp(request.AttributionWindowDays, 1, 365);
        s.HoldPeriodDays = Math.Clamp(request.HoldPeriodDays, 0, 90);
        s.UpdatedAt = DateTime.UtcNow;

        // Le plafond peut avoir baissé : aucun affilié ne doit rester au-dessus.
        var overCapAccounts = await _db.AffiliateAccounts
            .Where(a => a.CommissionRate > s.CommissionRateCapPercent)
            .ToListAsync();
        foreach (var a in overCapAccounts) a.CommissionRate = s.CommissionRateCapPercent;

        await _db.SaveChangesAsync();
        return new AffiliateSettingsDto(s.CommissionRateCapPercent, s.AttributionWindowDays, s.HoldPeriodDays);
    }

    public async Task<List<AdminAffiliateAccountDto>> ListAccountsAsync()
    {
        var accounts = await _db.AffiliateAccounts.AsNoTracking()
            .Include(a => a.User)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        var result = new List<AdminAffiliateAccountDto>();
        foreach (var a in accounts)
        {
            var clicks = await _db.AffiliateClicks.AsNoTracking().CountAsync(c => c.AffiliateAccountId == a.Id);
            var commissions = await _db.AffiliateCommissions.AsNoTracking().Where(c => c.AffiliateAccountId == a.Id).ToListAsync();
            result.Add(new AdminAffiliateAccountDto(
                a.Id, a.UserId, $"{a.User.FirstName} {a.User.LastName}".Trim(), a.Code, a.CommissionRate, a.Status,
                clicks, commissions.Count(c => c.Status != "reversed"),
                commissions.Where(c => c.Status is "confirmed" or "paid").Sum(c => c.CommissionAmount),
                a.CreatedAt, a.LastRateUpdateAt));
        }
        return result;
    }

    public async Task<bool> SetAccountStatusAsync(int accountId, string status)
    {
        if (status is not ("active" or "suspended")) return false;
        var account = await _db.AffiliateAccounts.FirstOrDefaultAsync(a => a.Id == accountId);
        if (account == null) return false;
        account.Status = status;
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Recalcule le taux de commission de chaque affilié actif. Tente d'abord
    /// WinAI (FastAPI, /api/affiliate/commission-rate) pour une analyse
    /// approfondie du profil ; si le service est indisponible ou ne répond
    /// pas encore (endpoint pas déployé côté Python), retombe sur
    /// <see cref="ComputeHeuristicRate"/> — jamais bloquant, jamais au-dessus
    /// du plafond admin.
    /// </summary>
    public async Task RecalculateAllRatesAsync(CancellationToken ct = default)
    {
        var settings = await GetOrCreateSettingsEntityAsync();
        var accounts = await _db.AffiliateAccounts.Where(a => a.Status == "active").ToListAsync(ct);

        foreach (var account in accounts)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var signals = await BuildSignalsAsync(account, settings, ct);
                var aiResponse = await _fastApi.PostAsync<AffiliateRateResponse>("/api/affiliate/commission-rate", signals);

                var newRate = aiResponse != null
                    ? Math.Clamp(aiResponse.RecommendedRatePercent, 0, settings.CommissionRateCapPercent)
                    : ComputeHeuristicRate(signals);

                account.CommissionRate = newRate;
                account.LastRateUpdateAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Recalcul du taux d'affiliation échoué pour le compte {AccountId}, taux conservé", account.Id);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<AffiliateRateSignals> BuildSignalsAsync(AffiliateAccount account, AffiliateSettings settings, CancellationToken ct)
    {
        var totalClicks = await _db.AffiliateClicks.AsNoTracking().CountAsync(c => c.AffiliateAccountId == account.Id, ct);
        var commissions = await _db.AffiliateCommissions.AsNoTracking()
            .Where(c => c.AffiliateAccountId == account.Id)
            .ToListAsync(ct);

        var totalConversions = commissions.Count(c => c.Status != "reversed");
        var reversedCount = commissions.Count(c => c.Status == "reversed");
        var refundRate = commissions.Count == 0 ? 0m : Math.Round(100m * reversedCount / commissions.Count, 2);
        var conversionRate = totalClicks == 0 ? 0m : Math.Round(100m * totalConversions / totalClicks, 2);
        var totalRevenue = commissions.Where(c => c.Status != "reversed").Sum(c => c.OrderAmount);
        var tenureDays = Math.Max(0, (int)(DateTime.UtcNow - account.CreatedAt).TotalDays);

        return new AffiliateRateSignals(
            account.UserId, tenureDays, totalClicks, totalConversions, conversionRate,
            totalRevenue, refundRate, account.CommissionRate, settings.CommissionRateCapPercent);
    }

    /// <summary>
    /// Heuristique de repli v1 (WinAI indisponible) : part d'un taux de base
    /// modeste et le fait monter avec le volume de conversions et la qualité
    /// (faible taux de remboursement), jamais au-delà du plafond admin. Conçue
    /// pour être remplacée en douceur par l'analyse WinAI dès que l'endpoint
    /// FastAPI existe — même plafond, mêmes signaux d'entrée.
    /// </summary>
    public static decimal ComputeHeuristicRate(AffiliateRateSignals s)
    {
        var baseRate = 5m;
        var volumeBonus = Math.Min(6m, s.TotalConversions * 0.5m);
        var qualityBonus = s.ConversionRate >= 5m ? 2m : 0m;
        var refundPenalty = s.RefundRatePercent >= 20m ? 4m : s.RefundRatePercent >= 10m ? 2m : 0m;

        var rate = baseRate + volumeBonus + qualityBonus - refundPenalty;
        return Math.Clamp(Math.Round(rate, 2), 0, s.CapPercent);
    }
}
