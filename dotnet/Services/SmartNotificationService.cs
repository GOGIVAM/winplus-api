using System.Net.Http.Json;
using System.Text.Json;
using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Personnalise les notifications ntfy via WinAI (Python/DeepSeek) avant envoi.
/// Si Python est indisponible ou dépasse le rate-limit, retombe sur le message générique.
/// </summary>
public interface ISmartNotificationService
{
    Task PublishSmartAsync(
        string topic,
        string fallbackTitle,
        string fallbackBody,
        string notificationType,
        int userId,
        Dictionary<string, object>? contextData = null,
        string priority = "default",
        string[]? tags = null,
        string type = "General");
}

public class SmartNotificationService : ISmartNotificationService
{
    private readonly INtfyService _ntfy;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SmartNotificationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceTokenProvider _serviceToken;
    private readonly ApplicationDbContext? _db;

    public SmartNotificationService(
        INtfyService ntfy,
        IHttpClientFactory httpClientFactory,
        ILogger<SmartNotificationService> logger,
        IConfiguration configuration,
        IServiceTokenProvider serviceToken,
        ApplicationDbContext? db = null)
    {
        _db = db;
        _ntfy = ntfy;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
        _serviceToken = serviceToken;
    }

    public async Task PublishSmartAsync(
        string topic,
        string fallbackTitle,
        string fallbackBody,
        string notificationType,
        int userId,
        Dictionary<string, object>? contextData = null,
        string priority = "default",
        string[]? tags = null,
        string type = "General")
    {
        string title = fallbackTitle;
        string body  = fallbackBody;

        try
        {
            var aiBaseUrl = _configuration["AIService:BaseUrl"] ?? "http://localhost:8000";
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            var payload = new
            {
                user_id           = userId,
                notification_type = notificationType,
                context_data      = contextData ?? new Dictionary<string, object>()
            };

            // Module 23 : l'appel partait sans Authorization (service appelé hors
            // requête HTTP), FastAPI répondait 401 à chaque fois.
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{aiBaseUrl}/api/ai/generate-notification")
            {
                Content = JsonContent.Create(payload),
            };
            request.Headers.TryAddWithoutValidation("Authorization", _serviceToken.CreateAuthorizationHeader(ServiceScopes.Notification));
            // Lot 10.6 : appel hors requête HTTP, la langue du destinataire est
            // lue ici pour que WinAI rédige la notification dans sa langue.
            var locale = await ResolveLocaleAsync(userId);
            if (locale != null) request.Headers.TryAddWithoutValidation(AiLocaleHeaderHandler.HeaderName, locale);
            var response = await httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("success", out var successProp) && successProp.GetBoolean())
                {
                    var aiTitle = root.TryGetProperty("title", out var t) ? t.GetString() : null;
                    var aiBody  = root.TryGetProperty("body",  out var b) ? b.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(aiTitle)) title = aiTitle;
                    if (!string.IsNullOrWhiteSpace(aiBody))  body  = aiBody;
                    _logger.LogInformation("[SmartNotif] AI-personalized notification for user {UserId}", userId);
                }
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                    _logger.LogError("[SmartNotif] Authentification service à service refusée par Python ({Status}) : {Body}", (int)response.StatusCode, errorBody);
                else
                    _logger.LogWarning("[SmartNotif] Python returned {Status} ({Body})  using fallback", (int)response.StatusCode, errorBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SmartNotif] AI call failed  using fallback for user {UserId}", userId);
        }

        await _ntfy.PublishAsync(topic, title, body, priority, tags, userId, type);
    }

    private async Task<string?> ResolveLocaleAsync(int userId)
    {
        if (_db == null) return null;
        try
        {
            var stored = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Locale).FirstOrDefaultAsync();
            return stored == null ? null : AiLocaleHeaderHandler.Normalize(stored);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SmartNotif] Langue de l'utilisateur {UserId} illisible, français par défaut", userId);
            return null;
        }
    }
}
