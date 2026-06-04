using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Backend.Services.PaymentProviders;

// ============================================================
// ORANGE MONEY SERVICE
// API: Orange Money API v2 (Pay-in / Merchant API)
// Docs: https://developer.orange.com/apis/om-webpay-cm
// ============================================================

public class OrangeMoneyConfig
{
    public string BaseUrl { get; set; } = "https://api.orange.com";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string MerchantKey { get; set; } = "";
    public string Currency { get; set; } = "XAF";
    public string ReturnUrl { get; set; } = "https://votresite.com/api/payments/orange/return";
    public string CancelUrl { get; set; } = "https://votresite.com/api/payments/orange/cancel";
    public string NotifUrl { get; set; } = "https://votresite.com/api/payments/orange/notify";
}

public class OrangeMoneyService
{
    private readonly HttpClient _http;
    private readonly OrangeMoneyConfig _config;
    private readonly ILogger<OrangeMoneyService> _logger;
    private string? _accessToken;
    private DateTime _tokenExpiry;

    public OrangeMoneyService(HttpClient http, OrangeMoneyConfig config, ILogger<OrangeMoneyService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync()
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
            return _accessToken;

        try
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_config.ClientId}:{_config.ClientSecret}"));

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_config.BaseUrl}/oauth/v3/token");

            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<JsonElement>(json);

            _accessToken = data.GetProperty("access_token").GetString()!;
            var expiresIn = data.GetProperty("expires_in").GetInt32();
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);

            _logger.LogInformation("[Orange] Token obtenu avec succès");
            return _accessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Orange] Erreur lors de l'obtention du token");
            throw;
        }
    }

    public async Task<OrangePaymentResult> InitiateWebPayAsync(OrangePaymentRequest payment)
    {
        try
        {
            var token = await GetAccessTokenAsync();

            var hashInput = $"{_config.MerchantKey}{payment.OrderId}{payment.Amount}{_config.Currency}";
            var hash = ComputeHash(hashInput);

            var body = new
            {
                merchant_key = _config.MerchantKey,
                currency = _config.Currency,
                order_id = payment.OrderId,
                amount = payment.Amount,
                return_url = _config.ReturnUrl,
                cancel_url = _config.CancelUrl,
                notif_url = _config.NotifUrl,
                lang = "fr",
                reference = payment.OrderId,
                hash
            };

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_config.BaseUrl}/orange-money-webpay/cm/v1/webpayment");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(JsonSerializer.Serialize(body),
                Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Orange] Erreur lors de l'initiation: {Response}", json);
                return new OrangePaymentResult { Success = false, Message = json };
            }

            var data = JsonSerializer.Deserialize<JsonElement>(json);

            _logger.LogInformation("[Orange] WebPay initié - Order: {OrderId}", payment.OrderId);

            return new OrangePaymentResult
            {
                Success = true,
                PaymentUrl = data.GetProperty("payment_url").GetString()!,
                PayToken = data.GetProperty("pay_token").GetString()!,
                NotifToken = data.TryGetProperty("notif_token", out var nt) ? nt.GetString() : null,
                Message = "Rediriger le client vers PaymentUrl"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Orange] Erreur lors de l'initiation du paiement");
            return new OrangePaymentResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public async Task<OrangePaymentStatus> CheckTransactionStatusAsync(string payToken)
    {
        try
        {
            var token = await GetAccessTokenAsync();

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_config.BaseUrl}/orange-money-webpay/cm/v1/transactionstatus/{payToken}");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<JsonElement>(json);

            return new OrangePaymentStatus
            {
                PayToken = payToken,
                Status = data.TryGetProperty("status", out var s) ? s.GetString()! : "UNKNOWN",
                Amount = data.TryGetProperty("amount", out var a) ? a.GetDecimal() : 0,
                TxnId = data.TryGetProperty("txnid", out var t) ? t.GetString() : null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Orange] Erreur lors de la vérification du statut");
            return new OrangePaymentStatus
            {
                PayToken = payToken,
                Status = "ERROR"
            };
        }
    }

    public OrangeCallbackResult ProcessCallback(string rawJson)
    {
        try
        {
            var data = JsonSerializer.Deserialize<JsonElement>(rawJson);
            return new OrangeCallbackResult
            {
                Success = true,
                Status = data.GetProperty("status").GetString()!,
                OrderId = data.GetProperty("order_id").GetString()!,
                TxnId = data.TryGetProperty("txnid", out var t) ? t.GetString() : null,
                Amount = data.TryGetProperty("amount", out var a) ? a.GetDecimal() : 0,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Orange] Erreur lors du traitement du callback");
            return new OrangeCallbackResult { Success = false, Message = ex.Message };
        }
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLower();
    }
}

public class OrangePaymentRequest
{
    public string OrderId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
}

public class OrangePaymentResult
{
    public bool Success { get; set; }
    public string? PaymentUrl { get; set; }
    public string? PayToken { get; set; }
    public string? NotifToken { get; set; }
    public string Message { get; set; } = "";
}

public class OrangePaymentStatus
{
    public string PayToken { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
    public string? TxnId { get; set; }
}

public class OrangeCallbackResult
{
    public bool Success { get; set; }
    public string Status { get; set; } = "";
    public string OrderId { get; set; } = "";
    public string? TxnId { get; set; }
    public decimal Amount { get; set; }
    public string? Message { get; set; }
}
