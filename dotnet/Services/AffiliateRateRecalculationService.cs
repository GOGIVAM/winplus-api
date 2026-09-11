namespace Backend.Services;

/// <summary>
/// Recalcule le 1er de chaque mois le taux de commission de chaque affilié
/// actif (WinAI si disponible, heuristique locale en repli — voir
/// AffiliateService.RecalculateAllRatesAsync). Même mécanique de
/// planification que TutorCoachingReportService/MonthlyInstitutionReportService.
/// </summary>
public sealed class AffiliateRateRecalculationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AffiliateRateRecalculationService> _logger;

    public AffiliateRateRecalculationService(IServiceScopeFactory scopeFactory, ILogger<AffiliateRateRecalculationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AffiliateRateRecalculationService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilFirstOfMonth();
            _logger.LogInformation("Next affiliate rate recalculation in {Hours}h {Minutes}m.", (int)delay.TotalHours, delay.Minutes);

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var affiliate = scope.ServiceProvider.GetRequiredService<IAffiliateService>();
                await affiliate.RecalculateAllRatesAsync(stoppingToken);
                _logger.LogInformation("Affiliate rate recalculation completed.");
            }
            catch (Exception ex) { _logger.LogError(ex, "AffiliateRateRecalculationService run failed."); }
        }
    }

    private static TimeSpan ComputeDelayUntilFirstOfMonth()
    {
        var now = DateTime.UtcNow;
        var thisMonthFirst = new DateTime(now.Year, now.Month, 1, 9, 0, 0, DateTimeKind.Utc);
        var next = thisMonthFirst > now ? thisMonthFirst : thisMonthFirst.AddMonths(1);
        return next - now;
    }
}
