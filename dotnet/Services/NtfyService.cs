using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public interface INtfyService
{
    Task PublishAsync(string topic, string title, string message,
        string priority = "default",
        string[]? tags = null,
        int? userId = null,
        string type = "General",
        string? relatedEntityType = null,
        int? relatedEntityId = null);

    /// <summary>
    /// Alerte administrateur : publiée sur le topic ntfy admin ET persistée
    /// dans la liste in-app de chaque administrateur actif (Module 22), pour
    /// qu'un administrateur non abonné au topic ne perde pas l'information
    /// (demandes de retrait en particulier). Jamais filtrée par les préférences.
    /// </summary>
    Task PublishAdminAsync(string title, string message,
        string priority = "urgent",
        string[]? tags = null,
        string type = "Admin",
        string? relatedEntityType = null,
        int? relatedEntityId = null);
}

/// <summary>
/// Canal de notification ntfy + liste in-app.
///
/// Module 22 :
///  - la persistance in-app se fait dans un contexte de données DÉDIÉ (scope
///    propre). Auparavant, SaveChangesAsync était appelé sur le contexte
///    « scopé » partagé avec l'appelant : toute modification non encore
///    enregistrée par un contrôleur était validée prématurément (confirmé par
///    NotificationIntegrationTests.FailedOperationAfterNotify_…). Chaque
///    appelant reste seul responsable de l'enregistrement de ses propres
///    modifications ;
///  - les préférences de l'utilisateur sont consultées avant chaque envoi,
///    via INotificationPreferenceService (point unique) ;
///  - PublishAdminAsync persiste désormais une trace par administrateur actif.
/// Un échec de notification ne fait jamais échouer l'opération métier.
/// </summary>
public class NtfyService : INtfyService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationPreferenceService _preferences;
    private readonly ILogger<NtfyService> _logger;
    private readonly string _baseUrl;
    private readonly string? _authToken;
    private readonly string _adminTopic;

    public NtfyService(
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory,
        INotificationPreferenceService preferences,
        ILogger<NtfyService> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _preferences = preferences;
        _logger = logger;
        _baseUrl = configuration["Ntfy:BaseUrl"] ?? "https://ntfy.sh";
        _authToken = configuration["Ntfy:AuthToken"];
        _adminTopic = configuration["Ntfy:AdminTopic"] ?? "winplus-admin";
    }

    public async Task PublishAsync(string topic, string title, string message,
        string priority = "default",
        string[]? tags = null,
        int? userId = null,
        string type = "General",
        string? relatedEntityType = null,
        int? relatedEntityId = null)
    {
        var category = NotificationPolicy.CategoryOf(type);

        // Sans destinataire identifié (topic seul), aucune préférence à lire.
        var settings = userId.HasValue && category != NotificationCategory.Transactional
            ? await _preferences.GetSettingsAsync(userId.Value)
            : null;

        if (settings == null || NotificationPreferenceService.Evaluate(settings, NotificationChannel.Push, category))
            await SendToNtfy(topic, title, message, priority, tags);
        else
            _logger.LogDebug("Notification {Type} non poussée à l'utilisateur {UserId} (préférences)", type, userId);

        if (userId.HasValue && (settings == null || NotificationPreferenceService.Evaluate(settings, NotificationChannel.InApp, category)))
        {
            await PersistAsync(new[] { userId.Value }, title, message, type, relatedEntityType, relatedEntityId);
        }
    }

    public async Task PublishAdminAsync(string title, string message,
        string priority = "urgent",
        string[]? tags = null,
        string type = "Admin",
        string? relatedEntityType = null,
        int? relatedEntityId = null)
    {
        await SendToNtfy(_adminTopic, title, message, priority, tags);

        List<int> adminIds;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            adminIds = await db.Users.AsNoTracking()
                .Where(u => u.Role.ToLower() == "admin" && u.IsActive && !u.IsDeleted)
                .Select(u => u.Id)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alerte administrateur non persistée (lecture des administrateurs impossible) : {Title}", title);
            return;
        }

        if (adminIds.Count == 0)
        {
            _logger.LogWarning("Alerte administrateur sans aucun compte administrateur actif pour la conserver : {Title}", title);
            return;
        }

        // Une ligne par administrateur (voulu) : chacun a sa propre liste et
        // son propre état « lu ».
        await PersistAsync(adminIds, title, message, type, relatedEntityType, relatedEntityId);
    }

    private async Task PersistAsync(IReadOnlyCollection<int> userIds, string title, string message,
        string type, string? relatedEntityType, int? relatedEntityId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTime.UtcNow;
            foreach (var id in userIds)
            {
                db.Notifications.Add(new Notification
                {
                    UserId = id,
                    Title = title,
                    Message = message,
                    Type = type,
                    RelatedEntityType = relatedEntityType,
                    RelatedEntityId = relatedEntityId,
                    IsRead = false,
                    CreatedAt = now,
                    User = null!
                });
            }
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist notification to DB for user(s) {UserIds}", string.Join(",", userIds));
        }
    }

    private async Task SendToNtfy(string topic, string title, string message,
        string priority, string[]? tags)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/{topic}")
            {
                Content = new StringContent(message)
            };
            request.Headers.Add("Title", Uri.EscapeDataString(title ?? string.Empty));
            request.Headers.Add("Priority", priority);

            if (!string.IsNullOrEmpty(_authToken))
                request.Headers.Add("Authorization", $"Bearer {_authToken}");

            if (tags?.Length > 0)
                request.Headers.Add("Tags", string.Join(",", tags));

            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
                _logger.LogInformation("Ntfy notification published to {Topic} ({Status})",
                    topic, (int)response.StatusCode);
            else
                _logger.LogWarning("Ntfy a refusé la notification pour {Topic} ({Status})",
                    topic, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish Ntfy notification to topic {Topic}", topic);
        }
    }
}
