using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.Entities;

namespace Backend.Services;

/// <summary>
/// Partie 14.1 (décisions produit du 2026-10-08)  Le conseiller parent devient
/// proactif par ALERTES CIBLÉES SUR SEUILS (pas un résumé quotidien systématique,
/// pas un statu quo purement réactif).
///
/// Tourne une fois par jour (comme CourseInactivityAlertService, même cadence) :
/// pour chaque lien parent-enfant accepté, demande à WinAI (Python,
/// services/parent_child_context.py  detect_threshold_alerts, base de
/// contexte partagée du Module 13) si un seuil est franchi, puis :
///  - persiste chaque alerte dans ParentAlerts (si elle n'y est pas déjà, via
///    la clé de déduplication DedupKey  non-répétition exigée par la
///    décision : une notification par franchissement, jamais une par
///    exécution périodique) ;
///  - déclenche la notification via NtfyService.PublishAsync, le système de
///    notifications déjà fiabilisé au lot 1 (Module 22 : préférences
///    utilisateur consultées, persistance in-app + push), réutilisé tel quel
///    plutôt qu'un canal parallèle.
///
/// Module 36 : appel à Python authentifié par jeton technique de périmètre
/// dédié (ServiceScopes.ParentAdvisorThresholds), comme les autres tâches de
/// fond .NET → FastAPI.
/// </summary>
public sealed class ParentAdvisorThresholdService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    // L'instance redémarre chaque matin et FastAPI met ~30 s à charger ses
    // modèles : sans attente, le premier (et souvent seul) passage du jour
    // tombait systématiquement sur une connexion refusée.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ParentAdvisorThresholdService> _logger;

    public ParentAdvisorThresholdService(IServiceScopeFactory scopeFactory, ILogger<ParentAdvisorThresholdService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ParentAdvisorThresholdService started.");
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var succeeded = false;
            try { succeeded = await RunAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "ParentAdvisorThresholdService run failed."); }

            try { await Task.Delay(succeeded ? Interval : RetryDelay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private sealed record LinkDto(int ParentId, int ChildId);

    // Noms explicites (JsonPropertyName) : la réponse JSON de FastAPI/Pydantic
    // est en snake_case, System.Text.Json ne la fait PAS correspondre
    // automatiquement aux propriétés PascalCase .NET.
    private sealed class ThresholdAlertDto
    {
        [JsonPropertyName("parent_id")] public int ParentId { get; set; }
        [JsonPropertyName("child_id")] public int ChildId { get; set; }
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("severity")] public string Severity { get; set; } = "Low";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
        [JsonPropertyName("detected_at")] public string DetectedAt { get; set; } = "";
        [JsonPropertyName("dedup_key")] public string DedupKey { get; set; } = "";
    }

    private sealed class ThresholdScanResponseDto
    {
        [JsonPropertyName("alerts")] public List<ThresholdAlertDto> Alerts { get; set; } = new();
    }

    // Lot 10.6 : titres dans les deux langues, choisis selon la langue du parent.
    private static readonly Dictionary<string, LocalizedText> AlertTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BaisseScoreSeuil"] = new("Baisse de score observée", "Drop in scores observed"),
        ["DevoirRetardProlonge"] = new("Devoir en retard", "Overdue homework"),
        ["ExamenImminentSansRevision"] = new("Examen proche  pas de révision récente", "Exam coming up, no recent revision"),
    };

    /// <returns>false si WinAI n'a pas pu être interrogé (nouvel essai plus tôt).</returns>
    private async Task<bool> RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();
        var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var serviceToken = scope.ServiceProvider.GetRequiredService<IServiceTokenProvider>();

        var links = await db.ParentStudentLinks.AsNoTracking()
            .Where(l => l.Status == "accepted")
            .Select(l => new LinkDto(l.ParentId, l.StudentId))
            .Distinct()
            .ToListAsync(ct);

        // Lot 9.7b : les alertes immédiates sont un droit du plan parent
        // (parent.realtime_alerts, Famille et Famille+) et ne portent que sur
        // les enfants couverts par ce plan (children.max). Sans ce droit, le
        // parent garde la synthèse hebdomadaire (WeeklyParentReportService).
        // Traitement déclenché par le système : non imputé au budget WinAI.
        var entitlements = scope.ServiceProvider.GetRequiredService<Backend.Services.Entitlements.IEntitlementService>();
        var eligible = new List<LinkDto>();
        foreach (var parentId in links.Select(l => l.ParentId).Distinct())
        {
            var ent = await entitlements.GetAsync(parentId, ct);
            if (!ent.Has(Backend.Services.Entitlements.EntitlementCatalog.ParentRealtimeAlerts)) continue;
            var covered = await Backend.Services.Entitlements.EntitlementService.CoveredChildrenAsync(
                db, parentId, ent.Int(Backend.Services.Entitlements.EntitlementCatalog.ChildrenMax), ct);
            eligible.AddRange(links.Where(l => l.ParentId == parentId && covered.Contains(l.ChildId)));
        }
        links = eligible;

        if (links.Count == 0) return true;

        // Un seul appel pour tous les couples parent-enfant du jour : évite N
        // appels HTTP (un par enfant), comme CourseInactivityAlertService le
        // fait déjà par formation plutôt que par élève individuel.
        ThresholdScanResponseDto? response;
        try
        {
            var client = httpFactory.CreateClient("FastApiClient");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/parent-advisor/threshold-alerts")
            {
                Content = JsonContent.Create(new { links = links.Select(l => new { parent_id = l.ParentId, child_id = l.ChildId }) }),
            };
            req.Headers.TryAddWithoutValidation("Authorization", serviceToken.CreateAuthorizationHeader(ServiceScopes.ParentAdvisorThresholds));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(60));
            var res = await client.SendAsync(req, cts.Token);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(cts.Token);
                if (res.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                    _logger.LogError("threshold-alerts : authentification service à service refusée par Python ({Status}) : {Body}", (int)res.StatusCode, body);
                else
                    _logger.LogWarning("threshold-alerts : Python a répondu {Status} : {Body}", (int)res.StatusCode, body);
                return false;
            }
            response = await res.Content.ReadFromJsonAsync<ThresholdScanResponseDto>(cancellationToken: cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "threshold-alerts : appel WinAI impossible.");
            return false;
        }

        if (response == null || response.Alerts.Count == 0) return true;

        // Dédoublonne contre les alertes déjà persistées (même parent/enfant/type/DedupKey).
        var existingKeys = (await db.ParentAlerts.AsNoTracking()
                .Where(a => a.DedupKey != null)
                .Select(a => new { a.ParentId, a.ChildId, a.Type, a.DedupKey })
                .ToListAsync(ct))
            .Select(a => (a.ParentId, a.ChildId, a.Type, a.DedupKey))
            .ToHashSet();

        var newCount = 0;
        foreach (var alert in response.Alerts)
        {
            if (existingKeys.Contains((alert.ParentId, alert.ChildId, alert.Type, alert.DedupKey)))
                continue; // déjà notifié pour ce même évènement  non-répétition exigée par 14.1.

            if (!DateTime.TryParse(alert.DetectedAt, null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var detectedAt))
                detectedAt = DateTime.UtcNow;

            db.ParentAlerts.Add(new ParentAlert
            {
                ParentId = alert.ParentId,
                ChildId = alert.ChildId,
                Type = alert.Type,
                Severity = alert.Severity,
                Content = alert.Content,
                DedupKey = alert.DedupKey,
                DetectedAt = detectedAt,
            });
            // Persisté AVANT la notification (et non en fin de boucle) : en cas
            // d'interruption du service entre deux alertes, celles déjà notifiées
            // ont déjà leur DedupKey en base et ne seront jamais renvoyées au
            // prochain run  exigence de non-répétition de la décision 14.1.
            await db.SaveChangesAsync(ct);
            existingKeys.Add((alert.ParentId, alert.ChildId, alert.Type, alert.DedupKey));
            newCount++;

            // Lot 1, Module 22 : canal de notification déjà fiabilisé (préférences
            // utilisateur, persistance in-app + push), réutilisé tel quel.
            var title = AlertTitles.TryGetValue(alert.Type, out var t) ? t : new LocalizedText("Signal WinAI", "WinAI alert");
            // Le texte de l'alerte est rédigé par WinAI : transmis tel quel.
            await ntfy.PublishLocalizedAsync(db,
                $"winplus-user-{alert.ParentId}",
                title,
                new LocalizedText(alert.Content, alert.Content),
                priority: alert.Severity == "High" ? "high" : "default",
                userId: alert.ParentId,
                type: "ParentAdvisor",
                relatedEntityType: "Child",
                relatedEntityId: alert.ChildId);
        }

        if (newCount > 0)
            _logger.LogInformation("ParentAdvisorThresholdService : {New} nouvelle(s) alerte(s) sur {Total} détectée(s).", newCount, response.Alerts.Count);
        return true;
    }
}
