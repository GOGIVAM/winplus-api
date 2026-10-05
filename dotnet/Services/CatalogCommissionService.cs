using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.Entities;

namespace Backend.Services;

/// <summary>
/// Module 7 : grille de commission catalogue. La part retenue par la
/// plateforme varie linéairement entre MinRatePercent (score WinAI 0) et
/// MaxRatePercent (score WinAI 100), elle-même toujours contenue dans
/// l'intervalle 10%-60% imposé par le product owner (§6.2 du suivi), quel
/// que soit ce qu'un administrateur tente de régler en dehors.
/// </summary>
public interface ICatalogCommissionService
{
    Task<CatalogCommissionSettings> GetSettingsAsync();

    /// <summary>Met à jour la grille (administrateur). Bornée en dur à [10, 60].</summary>
    Task<CatalogCommissionSettings> UpdateSettingsAsync(decimal minPercent, decimal maxPercent, int adminUserId);

    /// <summary>
    /// Traduit un score WinAI (0-100, ou null si l'évaluation a échoué) en
    /// part plateforme (0.10 à 0.60). Un score null retombe sur la borne
    /// basse de la grille  valeur de repli prudente, explicite, annoncée
    /// (cas limite du Module 7 : l'évaluation WinAI échoue ou expire).
    /// </summary>
    Task<decimal> RateForScoreAsync(decimal? score0To100);
}

public class CatalogCommissionService : ICatalogCommissionService
{
    public const decimal AbsoluteMinPercent = 10m;
    public const decimal AbsoluteMaxPercent = 60m;

    private readonly ApplicationDbContext _db;

    public CatalogCommissionService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<CatalogCommissionSettings> GetSettingsAsync()
    {
        var settings = await _db.CatalogCommissionSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null)
        {
            settings = new CatalogCommissionSettings { Id = 1, MinRatePercent = AbsoluteMinPercent, MaxRatePercent = AbsoluteMaxPercent };
            _db.CatalogCommissionSettings.Add(settings);
            await _db.SaveChangesAsync();
        }
        return settings;
    }

    public async Task<CatalogCommissionSettings> UpdateSettingsAsync(decimal minPercent, decimal maxPercent, int adminUserId)
    {
        var clampedMin = Math.Clamp(minPercent, AbsoluteMinPercent, AbsoluteMaxPercent);
        var clampedMax = Math.Clamp(maxPercent, AbsoluteMinPercent, AbsoluteMaxPercent);
        if (clampedMin > clampedMax) (clampedMin, clampedMax) = (clampedMax, clampedMin);

        var settings = await GetSettingsAsync();
        settings.MinRatePercent = clampedMin;
        settings.MaxRatePercent = clampedMax;
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedByUserId = adminUserId;
        await _db.SaveChangesAsync();
        return settings;
    }

    public async Task<decimal> RateForScoreAsync(decimal? score0To100)
    {
        var settings = await GetSettingsAsync();
        if (score0To100 is null)
            return settings.MinRatePercent / 100m;

        var clampedScore = Math.Clamp(score0To100.Value, 0m, 100m);
        var rate = settings.MinRatePercent + (settings.MaxRatePercent - settings.MinRatePercent) * (clampedScore / 100m);
        return Math.Clamp(rate, AbsoluteMinPercent, AbsoluteMaxPercent) / 100m;
    }
}
