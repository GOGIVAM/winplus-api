using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.Entities;

namespace Backend.Services;

/// <summary>
/// Module 33 (lot 7)  déclenchement hybride de l'album de fin d'année
/// (décision §5.5.P). AdminAlbumsController permettait déjà une génération
/// manuelle (1 parent test en aperçu, ou tous les parents en diffusion
/// immédiate), mais l'ensemble reposait sur la mémoire de l'administrateur
/// pour être lancé.
///
/// Ce service planifié, sur le patron de WeeklyParentReportService, vérifie
/// périodiquement si une AlbumSchedule configurée par un administrateur
/// (date de fin d'année scolaire, jamais codée en dur) a atteint sa date de
/// déclenchement. Le cas échéant, il génère l'aperçu (IsPreviewPending=true)
/// pour tous les parents éligibles  jamais la diffusion réelle, qui reste
/// une action manuelle explicite d'un administrateur (AdminAlbumsController.Dispatch).
/// </summary>
public sealed class YearlyAlbumSchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<YearlyAlbumSchedulerService> _logger;
    private readonly INtfyService _ntfy;

    // Vérification toutes les 6h : la précision à la minute n'a aucun sens
    // pour un événement annuel, et ça borne le travail si plusieurs
    // instances tournent (le garde-fou réel reste l'unicité par SchoolYear
    // + la transition de Status, revérifiée avant écriture).
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(6);

    public YearlyAlbumSchedulerService(IServiceScopeFactory scopeFactory, ILogger<YearlyAlbumSchedulerService> logger, INtfyService ntfy)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _ntfy = ntfy;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("YearlyAlbumSchedulerService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "YearlyAlbumSchedulerService run failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var albumService = scope.ServiceProvider.GetRequiredService<IYearlyAlbumService>();

        var due = await db.AlbumSchedules
            .Where(s => s.Status == "Pending" && s.TriggerDate <= DateTime.UtcNow)
            .ToListAsync(ct);

        foreach (var schedule in due)
        {
            if (ct.IsCancellationRequested) break;

            // Reconfirme l'état juste avant d'écrire : deux exécutions qui se
            // recouperaient (plusieurs instances, ou double appel après un
            // redémarrage rapide) ne doivent générer qu'un seul aperçu.
            var fresh = await db.AlbumSchedules.FirstOrDefaultAsync(s => s.Id == schedule.Id && s.Status == "Pending", ct);
            if (fresh == null) continue;

            _logger.LogInformation("Génération automatique de l'aperçu d'album pour l'année scolaire {SchoolYear}.", fresh.SchoolYear);

            var eligibleParentIds = await db.ParentStudentLinks.AsNoTracking()
                .Where(l => l.Status == "accepted")
                .Select(l => l.ParentId)
                .Distinct()
                .OrderBy(id => id)
                .ToListAsync(ct);

            var successCount = 0;
            var failureCount = 0;

            foreach (var parentId in eligibleParentIds)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    var result = await albumService.GenerateAlbumForParentAsync(
                        parentId, fresh.SchoolYear, persist: true, ct, markPreviewPending: true);
                    if (result.Children.Any(c => c.Success)) successCount++;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    _logger.LogWarning(ex, "Échec de génération de l'aperçu d'album pour le parent {ParentId}.", parentId);
                }
            }

            fresh.Status = "PreviewGenerated";
            fresh.GeneratedAt = DateTime.UtcNow;
            fresh.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            // Notification admin persistée (Module 22, via le canal existant
            // PublishAdminAsync) : sans cela, l'alerte se perdrait si aucun
            // administrateur n'était connecté au moment de la génération
            // ce qui ramènerait au problème de départ.
            await _ntfy.PublishAdminAsync(
                "Album de fin d'année : aperçu prêt",
                $"L'aperçu de l'album {fresh.SchoolYear} a été généré pour {successCount} parent(s)" +
                    (failureCount > 0 ? $" ({failureCount} échec(s))" : "") +
                    ". Ouvrez l'écran Album pour le consulter et confirmer la diffusion.",
                relatedEntityType: "AlbumSchedule",
                relatedEntityId: fresh.Id);

            _logger.LogInformation(
                "Aperçu d'album {SchoolYear} généré : {Success}/{Total} parent(s), {Failures} échec(s).",
                fresh.SchoolYear, successCount, eligibleParentIds.Count, failureCount);
        }
    }
}
