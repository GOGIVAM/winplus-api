using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public class NotchPayConfig
{
    public string PublicKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.notchpay.co";
    public string CallbackUrl { get; set; } = string.Empty;
    public string Currency { get; set; } = "XAF";
}

public class NotchPayInitiateRequest
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "XAF";
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
}

public class NotchPayInitiateResponse
{
    public string? Status { get; set; }
    public string? Message { get; set; }
    public NotchPayTransaction? Transaction { get; set; }
    public string? AuthorizationUrl { get; set; }
}

public class NotchPayTransaction
{
    public string? Reference { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Operator { get; set; }
    public string? Phone { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
}

public interface INotchPayService
{
    Task<NotchPayInitiateResponse> InitiatePaymentAsync(string phone, decimal amount, int orderId, string description, string customerEmail, string channel);
    Task<NotchPayTransaction> GetTransactionStatusAsync(string reference);
    bool VerifyWebhookSignature(string payload, string signature);
}

public class NotchPayService : INotchPayService
{
    private readonly HttpClient _httpClient;
    private readonly NotchPayConfig _config;
    private readonly INtfyService _ntfy;
    private readonly ILogger<NotchPayService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public NotchPayService(IHttpClientFactory httpClientFactory, NotchPayConfig config, INtfyService ntfy, ILogger<NotchPayService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("NotchPayClient");
        _config = config;
        _ntfy = ntfy;
        _logger = logger;
    }

    public async Task<NotchPayInitiateResponse> InitiatePaymentAsync(string phone, decimal amount, int orderId, string description, string customerEmail, string channel)
    {
        var reference = $"WP-{orderId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        // NotchPay requires E.164 phone format (+237XXXXXXXXX)
        var e164Phone = phone.StartsWith('+') ? phone : $"+{phone}";

        var payload = new
        {
            amount    = (int)Math.Round(amount),
            currency  = _config.Currency,
            email     = customerEmail,
            phone     = e164Phone,
            channel   = channel,
            reference = reference,
            description = description,
            callback  = _config.CallbackUrl
        };

        return await ExecuteWithRetryAsync(async () =>
        {
            var json = JsonSerializer.Serialize(payload);
            _logger.LogInformation("NotchPay initiate → channel={Channel} phone={Phone} amount={Amount} json={Json}",
                channel, e164Phone, (int)Math.Round(amount), json);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/payments", content);
            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("NotchPay initiate ← status={Status} body={Body}", response.StatusCode, responseBody);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("NotchPay initiate failed: {Status} {Body}", response.StatusCode, responseBody);
                // 4xx = permanent client/auth error, don't retry
                if ((int)response.StatusCode < 500)
                    throw new InvalidOperationException($"NotchPay rejected request: {response.StatusCode} — {responseBody}");
                throw new HttpRequestException($"NotchPay error: {response.StatusCode}");
            }

            return JsonSerializer.Deserialize<NotchPayInitiateResponse>(responseBody, _jsonOptions)
                ?? throw new InvalidOperationException("Invalid NotchPay response");
        });
    }

    public async Task<NotchPayTransaction> GetTransactionStatusAsync(string reference)
    {
        var response = await _httpClient.GetAsync($"/payments/{Uri.EscapeDataString(reference)}");
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("NotchPay status check failed: {Status} {Body}", response.StatusCode, responseBody);
            if ((int)response.StatusCode < 500)
                throw new InvalidOperationException($"NotchPay rejected status request: {response.StatusCode} — {responseBody}");
            throw new HttpRequestException($"NotchPay error: {response.StatusCode}");
        }

        var result = JsonSerializer.Deserialize<NotchPayInitiateResponse>(responseBody, _jsonOptions);
        return result?.Transaction ?? throw new InvalidOperationException("Transaction not found in response");
    }

    public bool VerifyWebhookSignature(string payload, string signature)
    {
        if (string.IsNullOrEmpty(_config.WebhookSecret)) return false;

        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        // Tentative 1 : clé complète hsk.xxx
        if (TryHmac(_config.WebhookSecret, payloadBytes, signature)) return true;

        // Tentative 2 : clé sans le préfixe "hsk." (certaines implémentations le strippent)
        var dotIdx = _config.WebhookSecret.IndexOf('.');
        var stripped = dotIdx >= 0 ? _config.WebhookSecret[(dotIdx + 1)..] : _config.WebhookSecret;
        if (stripped != _config.WebhookSecret && TryHmac(stripped, payloadBytes, signature)) return true;

        // Tentative 3 : SecretKey (sk.xxx) comme clé de signature
        if (!string.IsNullOrEmpty(_config.SecretKey) && TryHmac(_config.SecretKey, payloadBytes, signature)) return true;

        using var hmacLog = new HMACSHA256(Encoding.UTF8.GetBytes(_config.WebhookSecret));
        var expectedHex = Convert.ToHexString(hmacLog.ComputeHash(payloadBytes)).ToLowerInvariant();
        _logger.LogWarning(
            "NotchPay webhook signature mismatch — receivedLen={RcvLen} received60={Rcv60} expectedHex60={Exp60}",
            signature?.Length,
            signature?.Length > 60 ? signature[..60] : signature,
            expectedHex.Length > 60 ? expectedHex[..60] : expectedHex);

        return false;
    }

    private static bool TryHmac(string key, byte[] payloadBytes, string signature)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(payloadBytes);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        var b64 = Convert.ToBase64String(hash);
        return string.Equals(hex, signature?.ToLowerInvariant(), StringComparison.Ordinal)
            || string.Equals(b64, signature, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action)
    {
        var delays = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) };

        for (int attempt = 0; attempt <= delays.Length; attempt++)
        {
            try
            {
                return await action();
            }
            catch (HttpRequestException ex) when (attempt < delays.Length)
            {
                _logger.LogWarning("NotchPay attempt {Attempt} failed: {Message}. Retrying in {Delay}s",
                    attempt + 1, ex.Message, delays[attempt].TotalSeconds);
                await Task.Delay(delays[attempt]);
            }
        }

        _ = _ntfy.PublishAdminAsync(
            "Service NotchPay indisponible",
            "NotchPay est inaccessible après 3 tentatives. Vérifier la connectivité API.",
            "urgent",
            new[] { "rotating_light", "credit_card" });

        throw new InvalidOperationException("NotchPay service unavailable after 3 retries");
    }
}
