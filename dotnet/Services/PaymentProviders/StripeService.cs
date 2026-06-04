using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Backend.Services.PaymentProviders;

// ============================================================
// STRIPE PAYMENT SERVICE (Carte Bancaire)
// API: Stripe REST API v2
// Docs: https://stripe.com/docs/api
// ============================================================

public class StripeConfig
{
    public string SecretKey { get; set; } = "";
    public string PublishableKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string Currency { get; set; } = "xaf";
    public string SuccessUrl { get; set; } = "https://votresite.com/paiement/succes?session_id={CHECKOUT_SESSION_ID}";
    public string CancelUrl { get; set; } = "https://votresite.com/paiement/annule";
}

public class StripeService
{
    private readonly HttpClient _http;
    private readonly StripeConfig _config;
    private readonly ILogger<StripeService> _logger;

    public StripeService(HttpClient http, StripeConfig config, ILogger<StripeService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
        _http.BaseAddress = new Uri("https://api.stripe.com/v1/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", config.SecretKey);
    }

    public async Task<StripeCheckoutResult> CreateCheckoutSessionAsync(StripePaymentRequest payment)
    {
        try
        {
            var formData = new Dictionary<string, string>
            {
                ["mode"] = "payment",
                ["success_url"] = _config.SuccessUrl,
                ["cancel_url"] = _config.CancelUrl,
                ["client_reference_id"] = payment.OrderId,
                ["customer_email"] = payment.CustomerEmail ?? "",
                ["line_items[0][price_data][currency]"] = _config.Currency,
                ["line_items[0][price_data][product_data][name]"] = payment.ProductName,
                ["line_items[0][price_data][unit_amount]"] = ((int)(payment.Amount * 100)).ToString(),
                ["line_items[0][quantity]"] = "1",
                ["metadata[order_id]"] = payment.OrderId,
            };

            if (payment.Metadata != null)
            {
                foreach (var (key, value) in payment.Metadata)
                    formData[$"metadata[{key}]"] = value;
            }

            var response = await _http.PostAsync("checkout/sessions",
                new FormUrlEncodedContent(formData));

            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Stripe] Erreur lors de la création: {Response}", json);
                return new StripeCheckoutResult { Success = false, Message = json };
            }

            var data = JsonSerializer.Deserialize<JsonElement>(json);

            _logger.LogInformation("[Stripe] Checkout session créée - Order: {OrderId}", payment.OrderId);

            return new StripeCheckoutResult
            {
                Success = true,
                SessionId = data.GetProperty("id").GetString()!,
                CheckoutUrl = data.GetProperty("url").GetString()!,
                PaymentIntentId = data.TryGetProperty("payment_intent", out var pi) ? pi.GetString() : null,
                Message = "Session créée. Rediriger vers CheckoutUrl."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Stripe] Erreur lors de la création du checkout");
            return new StripeCheckoutResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public async Task<StripeSessionStatus> GetCheckoutSessionAsync(string sessionId)
    {
        try
        {
            var response = await _http.GetAsync($"checkout/sessions/{sessionId}");
            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<JsonElement>(json);

            return new StripeSessionStatus
            {
                SessionId = sessionId,
                PaymentStatus = data.GetProperty("payment_status").GetString()!,
                Status = data.GetProperty("status").GetString()!,
                AmountTotal = data.TryGetProperty("amount_total", out var a) ? a.GetInt64() : 0,
                Currency = data.TryGetProperty("currency", out var c) ? c.GetString() : null,
                CustomerEmail = data.TryGetProperty("customer_email", out var e) ? e.GetString() : null,
                ClientReference = data.TryGetProperty("client_reference_id", out var r) ? r.GetString() : null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Stripe] Erreur lors de la récupération de session");
            return new StripeSessionStatus
            {
                SessionId = sessionId,
                Status = "error"
            };
        }
    }

    public async Task<StripePaymentIntentResult> CreatePaymentIntentAsync(StripePaymentRequest payment)
    {
        try
        {
            var formData = new Dictionary<string, string>
            {
                ["amount"] = ((int)(payment.Amount * 100)).ToString(),
                ["currency"] = _config.Currency,
                ["description"] = payment.ProductName,
                ["metadata[order_id]"] = payment.OrderId,
                ["automatic_payment_methods[enabled]"] = "true",
            };

            var response = await _http.PostAsync("payment_intents",
                new FormUrlEncodedContent(formData));

            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Stripe] Erreur PaymentIntent: {Response}", json);
                return new StripePaymentIntentResult { Success = false, Message = json };
            }

            var data = JsonSerializer.Deserialize<JsonElement>(json);

            return new StripePaymentIntentResult
            {
                Success = true,
                PaymentIntentId = data.GetProperty("id").GetString()!,
                ClientSecret = data.GetProperty("client_secret").GetString()!,
                Status = data.GetProperty("status").GetString()!,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Stripe] Erreur lors de la création du PaymentIntent");
            return new StripePaymentIntentResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public async Task<StripeRefundResult> RefundAsync(string paymentIntentId, decimal? amount = null)
    {
        try
        {
            var formData = new Dictionary<string, string>
            {
                ["payment_intent"] = paymentIntentId
            };

            if (amount.HasValue)
                formData["amount"] = ((int)(amount.Value * 100)).ToString();

            var response = await _http.PostAsync("refunds",
                new FormUrlEncodedContent(formData));

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<JsonElement>(json);

            return new StripeRefundResult
            {
                Success = response.IsSuccessStatusCode,
                RefundId = response.IsSuccessStatusCode
                    ? data.GetProperty("id").GetString()!
                    : null,
                Status = response.IsSuccessStatusCode
                    ? data.GetProperty("status").GetString()!
                    : "error",
                Message = json
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Stripe] Erreur lors du remboursement");
            return new StripeRefundResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public StripeWebhookResult ProcessWebhook(string rawBody, string stripeSignature)
    {
        if (!VerifyStripeSignature(rawBody, stripeSignature, _config.WebhookSecret))
        {
            _logger.LogWarning("[Stripe] Signature webhook invalide");
            return new StripeWebhookResult { Valid = false, Message = "Signature invalide" };
        }

        try
        {
            var data = JsonSerializer.Deserialize<JsonElement>(rawBody);
            var eventType = data.GetProperty("type").GetString()!;
            var eventData = data.GetProperty("data").GetProperty("object");

            return new StripeWebhookResult
            {
                Valid = true,
                EventType = eventType,
                ObjectId = eventData.TryGetProperty("id", out var id) ? id.GetString() : null,
                PaymentStatus = eventData.TryGetProperty("payment_status", out var ps)
                    ? ps.GetString() : null,
                OrderId = eventData.TryGetProperty("client_reference_id", out var r)
                    ? r.GetString() : null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Stripe] Erreur lors du traitement du webhook");
            return new StripeWebhookResult { Valid = false, Message = ex.Message };
        }
    }

    private static bool VerifyStripeSignature(string payload, string signature, string secret)
    {
        var parts = signature.Split(',');
        var timestamp = parts.FirstOrDefault(p => p.StartsWith("t="))?.Substring(2);
        var v1 = parts.FirstOrDefault(p => p.StartsWith("v1="))?.Substring(3);

        if (timestamp == null || v1 == null) return false;

        var signedPayload = $"{timestamp}.{payload}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var expected = Convert.ToHexString(hash).ToLower();

        return expected == v1.ToLower();
    }
}

public class StripePaymentRequest
{
    public string OrderId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal Amount { get; set; }
    public string? CustomerEmail { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public class StripeCheckoutResult
{
    public bool Success { get; set; }
    public string? SessionId { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? PaymentIntentId { get; set; }
    public string Message { get; set; } = "";
}

public class StripeSessionStatus
{
    public string SessionId { get; set; } = "";
    public string PaymentStatus { get; set; } = "";
    public string Status { get; set; } = "";
    public long AmountTotal { get; set; }
    public string? Currency { get; set; }
    public string? CustomerEmail { get; set; }
    public string? ClientReference { get; set; }
}

public class StripePaymentIntentResult
{
    public bool Success { get; set; }
    public string? PaymentIntentId { get; set; }
    public string? ClientSecret { get; set; }
    public string Status { get; set; } = "";
    public string? Message { get; set; }
}

public class StripeRefundResult
{
    public bool Success { get; set; }
    public string? RefundId { get; set; }
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
}

public class StripeWebhookResult
{
    public bool Valid { get; set; }
    public string? EventType { get; set; }
    public string? ObjectId { get; set; }
    public string? PaymentStatus { get; set; }
    public string? OrderId { get; set; }
    public string? Message { get; set; }
}
