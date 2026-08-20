using System.Net.Http.Json;
using System.Text.Json;

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

    public SmartNotificationService(
        INtfyService ntfy,
        IHttpClientFactory httpClientFactory,
        ILogger<SmartNotificationService> logger,
        IConfiguration configuration)
    {
        _ntfy = ntfy;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
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

            var response = await httpClient.PostAsJsonAsync(
                $"{aiBaseUrl}/api/ai/generate-notification", payload);

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
                _logger.LogWarning("[SmartNotif] Python returned {Status}  using fallback", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SmartNotif] AI call failed  using fallback for user {UserId}", userId);
        }

        await _ntfy.PublishAsync(topic, title, body, priority, tags, userId, type);
    }
}
