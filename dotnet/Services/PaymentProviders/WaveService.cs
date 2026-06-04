using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Backend.Services.PaymentProviders;

// ============================================================
// WAVE MOBILE MONEY SERVICE
// API: Wave Checkout API
// Docs: https://docs.wave.com/
// ============================================================

public class WaveConfig
{
    public string BaseUrl { get; set; } = "https://api.wave.com/v1";
    public string ApiKey { get; set; } = "";
    public string Currency { get; set; } = "XOF";
    public string SuccessUrl { get; set; } = "https://votresite.com/api/payments/wave/success";
    public string ErrorUrl { get; set; } = "https://votresite.com/api/payments/wave/error";
}

public class WaveService
{
    private readonly HttpClient _http;
    private readonly WaveConfig _config;
    private readonly ILogger<WaveService> _logger;

    public WaveService(HttpClient http, WaveConfig config, ILogger<WaveService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", config.ApiKey);
    }

    public async Task<WaveCheckoutResult> CreateCheckoutSessionAsync(WavePaymentRequest payment)
    {
        try
        {
            var body = new
            {
                currency = _config.Currency,
                amount = payment.Amount.ToString("F0"),
                error_url = _config.ErrorUrl,
                success_url = _config.SuccessUrl,
                client_reference = payment.OrderId,
                restrict_mobile = false
            };

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_config.BaseUrl}/checkout/sessions");

            request.Content = new StringContent(JsonSerializer.Serialize(body),
                Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Wave] Erreur lors de la création de session: {Response}", json);
                return new WaveCheckoutResult
                {
                    Success = false,
                    Message = $"Erreur Wave: {json}"
                };
            }

            var data = JsonSerializer.Deserialize<JsonElement>(json);

            _logger.LogInformation("[Wave] Session créée - Order: {OrderId}", payment.OrderId);

            return new WaveCheckoutResult
            {
                Success = true,
                CheckoutSessionId = data.GetProperty("id").GetString()!,
                WaveLaunchUrl = data.GetProperty("wave_launch_url").GetString()!,
                ExpiresAt = data.TryGetProperty("when_expires", out var exp)
                    ? exp.GetString() : null,
                Message = "Session créée. Rediriger le client vers WaveLaunchUrl."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Wave] Erreur lors de la création de session");
            return new WaveCheckoutResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public async Task<WaveSessionStatus> GetSessionStatusAsync(string sessionId)
    {
        try
        {
            var response = await _http.GetAsync(
                $"{_config.BaseUrl}/checkout/sessions/{sessionId}");

            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return new WaveSessionStatus
                {
                    SessionId = sessionId,
                    Status = "ERROR",
                    Message = json
                };

            var data = JsonSerializer.Deserialize<JsonElement>(json);

            return new WaveSessionStatus
            {
                SessionId = sessionId,
                Status = data.GetProperty("payment_status").GetString()!,
                Amount = data.TryGetProperty("amount", out var a) ? a.GetString() : null,
                Currency = data.TryGetProperty("currency", out var c) ? c.GetString() : null,
                ClientReference = data.TryGetProperty("client_reference", out var r) ? r.GetString() : null,
                TransactionId = data.TryGetProperty("transaction_id", out var t) ? t.GetString() : null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Wave] Erreur lors de la vérification du statut");
            return new WaveSessionStatus
            {
                SessionId = sessionId,
                Status = "ERROR",
                Message = ex.Message
            };
        }
    }

    public WaveWebhookResult ProcessWebhook(string rawJson, string waveSignature, string webhookSecret)
    {
        if (!VerifySignature(rawJson, waveSignature, webhookSecret))
        {
            _logger.LogWarning("[Wave] Signature webhook invalide");
            return new WaveWebhookResult { Valid = false, Message = "Signature invalide" };
        }

        try
        {
            var data = JsonSerializer.Deserialize<JsonElement>(rawJson);

            return new WaveWebhookResult
            {
                Valid = true,
                EventType = data.TryGetProperty("type", out var t) ? t.GetString()! : "unknown",
                SessionId = data.TryGetProperty("data", out var d)
                            && d.TryGetProperty("id", out var id) ? id.GetString() : null,
                Status = data.TryGetProperty("data", out var d2)
                         && d2.TryGetProperty("payment_status", out var ps) ? ps.GetString() : null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Wave] Erreur lors du traitement du webhook");
            return new WaveWebhookResult { Valid = false, Message = ex.Message };
        }
    }

    private static bool VerifySignature(string payload, string signature, string secret)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expected = Convert.ToHexString(hash).ToLower();
        return expected == signature?.ToLower();
    }
}

public class WavePaymentRequest
{
    public string OrderId { get; set; } = "";
    public decimal Amount { get; set; }
    public string? CustomerPhone { get; set; }
}

public class WaveCheckoutResult
{
    public bool Success { get; set; }
    public string? CheckoutSessionId { get; set; }
    public string? WaveLaunchUrl { get; set; }
    public string? ExpiresAt { get; set; }
    public string Message { get; set; } = "";
}

public class WaveSessionStatus
{
    public string SessionId { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public string? ClientReference { get; set; }
    public string? TransactionId { get; set; }
    public string? Message { get; set; }
}

public class WaveWebhookResult
{
    public bool Valid { get; set; }
    public string? EventType { get; set; }
    public string? SessionId { get; set; }
    public string? Status { get; set; }
    public string? Message { get; set; }
}
