using Backend.Data;
using Backend.Models.Entities;

namespace Backend.Services;

public interface INtfyService
{
    Task PublishAsync(string topic, string title, string message,
        string priority = "default",
        string[]? tags = null,
        int? userId = null,
        string type = "General");

    Task PublishAdminAsync(string title, string message,
        string priority = "urgent",
        string[]? tags = null);
}

public class NtfyService : INtfyService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<NtfyService> _logger;
    private readonly string _baseUrl;
    private readonly string? _authToken;
    private readonly string _adminTopic;

    public NtfyService(
        IHttpClientFactory httpClientFactory,
        ApplicationDbContext db,
        ILogger<NtfyService> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _db = db;
        _logger = logger;
        _baseUrl = configuration["Ntfy:BaseUrl"] ?? "https://ntfy.sh";
        _authToken = configuration["Ntfy:AuthToken"];
        _adminTopic = configuration["Ntfy:AdminTopic"] ?? "winplus-admin";
    }

    public async Task PublishAsync(string topic, string title, string message,
        string priority = "default",
        string[]? tags = null,
        int? userId = null,
        string type = "General")
    {
        await SendToNtfy(topic, title, message, priority, tags);

        if (userId.HasValue)
        {
            try
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = userId.Value,
                    Title = title,
                    Message = message,
                    Type = type,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow,
                    User = null!
                });
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist notification to DB for user {UserId}", userId);
            }
        }
    }

    public async Task PublishAdminAsync(string title, string message,
        string priority = "urgent",
        string[]? tags = null)
    {
        await SendToNtfy(_adminTopic, title, message, priority, tags);
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
            request.Headers.Add("Title", title);
            request.Headers.Add("Priority", priority);

            if (!string.IsNullOrEmpty(_authToken))
                request.Headers.Add("Authorization", $"Bearer {_authToken}");

            if (tags?.Length > 0)
                request.Headers.Add("Tags", string.Join(",", tags));

            var response = await client.SendAsync(request);
            _logger.LogInformation("Ntfy notification published to {Topic} ({Status})",
                topic, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish Ntfy notification to topic {Topic}", topic);
        }
    }
}
