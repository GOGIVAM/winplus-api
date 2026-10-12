namespace Backend.Services;

/// <summary>
/// Cycle de vie des engagements mensuels (lot 9.4, suivi 17.7), toutes les
/// 15 minutes comme <see cref="TutorBookingLifecycleService"/> :
/// <list type="number">
///   <item>paiements Mobile Money d'engagement non confirmés après 2 h :
///   créneaux libérés (un encaissement tardif est remboursé au portefeuille) ;</item>
///   <item>demandes sans réponse du tuteur dans son délai : engagement expiré,
///   chaque séance remboursée intégralement (frais retenus sur le tuteur) ;</item>
///   <item>engagements dont la période est passée : « completed » ;</item>
///   <item>rappel au payeur 7 jours avant la période suivante (une
///   notification avant chaque échéance) ;</item>
///   <item>renouvellement automatique 3 jours avant la période suivante, pour
///   les engagements payés par solde dont l'option est activée.</item>
/// </list>
/// La libération de l'argent reste séance par séance, par
/// TutorBookingLifecycleService (24 h après chaque séance effectuée sans
/// litige). Aucune dépendance au service Python : pas de délai de démarrage
/// (celui de CourseInactivityAlertService/ParentAdvisorThresholdService ne
/// concerne que les tâches qui appellent FastAPI).
/// </summary>
public class TutorEngagementLifecycleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TutorEngagementLifecycleService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    public TutorEngagementLifecycleService(IServiceScopeFactory scopeFactory, ILogger<TutorEngagementLifecycleService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TutorEngagementLifecycleService démarré (intervalle : 15 minutes)");
        while (!stoppingToken.IsCancellationRequested)
        {
            // Chaque étape dans son propre scope : l'échec de l'une n'empêche pas les autres.
            await RunStepAsync("paiements abandonnés", s => s.CancelAbandonedPaymentsAsync(DateTime.UtcNow));
            await RunStepAsync("demandes expirées", s => s.ExpireStaleRequestsAsync(DateTime.UtcNow));
            await RunStepAsync("engagements terminés", s => s.CompleteFinishedAsync(DateTime.UtcNow));
            await RunStepAsync("rappels de renouvellement", s => s.SendRenewalRemindersAsync(DateTime.UtcNow));
            await RunStepAsync("renouvellements automatiques", s => s.RunAutoRenewalsAsync(DateTime.UtcNow));

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunStepAsync(string name, Func<ITutorEngagementService, Task<int>> step)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ITutorEngagementService>();
            var count = await step(service);
            if (count > 0) _logger.LogInformation("Engagements mensuels, {Step} : {Count}", name, count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Engagements mensuels : échec de l'étape « {Step} »", name);
        }
    }
}
