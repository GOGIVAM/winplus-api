namespace Backend.Services;

/// <summary>
/// Entretien de fond du lot 9.5, toutes les heures :
/// <list type="number">
///   <item>suppression des fichiers d'identité arrivés au terme de leur
///   conservation (30 jours après validation, 7 jours après refus ; la
///   décision et son auteur restent dans l'historique) ;</item>
///   <item>recalcul des tuteurs vérifiés par caution d'institution, pour
///   rattraper un rattachement supprimé par un autre chemin que l'écran de
///   rattachement (compte ou institution supprimés).</item>
/// </list>
/// </summary>
public class TutorVerificationMaintenanceService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TutorVerificationMaintenanceService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public TutorVerificationMaintenanceService(IServiceScopeFactory scopeFactory, ILogger<TutorVerificationMaintenanceService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TutorVerificationMaintenanceService démarré (intervalle : 1 heure)");
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunStepAsync("purge des fichiers d'identité", s => s.PurgeExpiredIdentityFilesAsync(DateTime.UtcNow));
            await RunStepAsync("recalcul des cautions", s => s.RecomputeInstitutionRoutesAsync());

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunStepAsync(string name, Func<ITutorVerificationService, Task<int>> step)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ITutorVerificationService>();
            var count = await step(service);
            if (count > 0) _logger.LogInformation("Vérification des tuteurs, {Step} : {Count}", name, count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vérification des tuteurs : échec de l'étape « {Step} »", name);
        }
    }
}
