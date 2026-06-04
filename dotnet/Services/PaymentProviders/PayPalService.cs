using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Backend.Services.PaymentProviders;

// ============================================================
// PAYPAL PAYMENT SERVICE
// API: PayPal REST API v2 (Orders + Payments)
// Docs: https://developer.paypal.com/api/orders/v2/
// ============================================================

public class PayPalConfig
{
    public string BaseUrl { get; set; } = "https://api-m.sandbox.paypal.com";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public string ReturnUrl { get; set; } = "https://votresite.com/api/payments/paypal/capture";
    public string CancelUrl { get; set; } = "https://votresite.com/api/payments/paypal/cancel";
    public string WebhookId { get; set; } = "";
}

public class PayPalService
{
    private readonly HttpClient _http;
    private readonly PayPalConfig _config;
    private readonly ILogger<PayPalService> _logger;
    private string? _accessToken;
    private DateTime _tokenExpiry;

    public PayPalService(HttpClient http, PayPalConfig config, ILogger<PayPalService> logger)
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
                $"{_config.BaseUrl}/v1/oauth2/token");

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
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 30);

            _logger.LogInformation("[PayPal] Token obtenu avec succès");
            return _accessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PayPal] Erreur lors de l'obtention du token");
            throw;
        }
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(PayPalPaymentRequest payment)
    {
        try
        {
            var token = await GetAccessTokenAsync();

            var body = new
            {
                intent = "CAPTURE",
                purchase_units = new[]
                {
                    new
                    {
                        reference_id = payment.OrderId,
                        description = payment.Description,
                        amount = new
                        {
                            currency_code = _config.Currency,
                            value = payment.Amount.ToString("F2"),
                            breakdown = new
                            {
                                item_total = new
                                {
                                    currency_code = _config.Currency,
                                    value = payment.Amount.ToString("F2")
                                }
                            }
                        },
                        items = payment.Items?.Select(i => new
                        {
                            name = i.Name,
                            quantity = i.Quantity.ToString(),
                            unit_amount = new
                            {
                                currency_code = _config.Currency,
                                value = i.UnitPrice.ToString("F2")
                            }
                        }).ToArray()
                    }
                },
                application_context = new
                {
                    brand_name = "Réussir",
                    locale = "fr-FR",
                    landing_page = "BILLING",
                    user_action = "PAY_NOW",
                    return_url = _config.ReturnUrl,
                    cancel_url = _config.CancelUrl
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_config.BaseUrl}/v2/checkout/orders");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(JsonSerializer.Serialize(body),
                Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[PayPal] Erreur lors de la création: {Response}", json);
                return new PayPalOrderResult { Success = false, Message = json };
            }

            var data = JsonSerializer.Deserialize<JsonElement>(json);
            var orderId = data.GetProperty("id").GetString()!;

            string? approveUrl = null;
            foreach (var link in data.GetProperty("links").EnumerateArray())
            {
                if (link.GetProperty("rel").GetString() == "approve")
                {
                    approveUrl = link.GetProperty("href").GetString();
                    break;
                }
            }

            _logger.LogInformation("[PayPal] Commande créée - OrderId: {OrderId}", payment.OrderId);

            return new PayPalOrderResult
            {
                Success = true,
                PayPalOrderId = orderId,
                ApproveUrl = approveUrl,
                Status = data.GetProperty("status").GetString()!,
                Message = "Commande créée. Rediriger vers ApproveUrl."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PayPal] Erreur lors de la création de commande");
            return new PayPalOrderResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public async Task<PayPalCaptureResult> CaptureOrderAsync(string paypalOrderId)
    {
        try
        {
            var token = await GetAccessTokenAsync();

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_config.BaseUrl}/v2/checkout/orders/{paypalOrderId}/capture");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[PayPal] Erreur lors de la capture: {Response}", json);
                return new PayPalCaptureResult { Success = false, Message = json };
            }

            var data = JsonSerializer.Deserialize<JsonElement>(json);
            var status = data.GetProperty("status").GetString()!;

            string? captureId = null;
            decimal? capturedAmount = null;

            if (data.TryGetProperty("purchase_units", out var units))
            {
                var payments = units[0].GetProperty("payments");
                if (payments.TryGetProperty("captures", out var captures) &&
                    captures.GetArrayLength() > 0)
                {
                    captureId = captures[0].GetProperty("id").GetString();
                    var amountStr = captures[0].GetProperty("amount").GetProperty("value").GetString();
                    capturedAmount = decimal.TryParse(amountStr, out var a) ? a : null;
                }
            }

            _logger.LogInformation("[PayPal] Capture réussie - Status: {Status}", status);

            return new PayPalCaptureResult
            {
                Success = status == "COMPLETED",
                PayPalOrderId = paypalOrderId,
                CaptureId = captureId,
                Status = status,
                CapturedAmount = capturedAmount,
                Message = status == "COMPLETED" ? "Paiement capturé avec succès" : $"Statut: {status}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PayPal] Erreur lors de la capture");
            return new PayPalCaptureResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public async Task<PayPalOrderDetails> GetOrderDetailsAsync(string paypalOrderId)
    {
        try
        {
            var token = await GetAccessTokenAsync();

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_config.BaseUrl}/v2/checkout/orders/{paypalOrderId}");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<JsonElement>(json);

            return new PayPalOrderDetails
            {
                PayPalOrderId = paypalOrderId,
                Status = data.GetProperty("status").GetString()!,
                Amount = data.GetProperty("purchase_units")[0]
                            .GetProperty("amount")
                            .GetProperty("value")
                            .GetString()!,
                Currency = data.GetProperty("purchase_units")[0]
                              .GetProperty("amount")
                              .GetProperty("currency_code")
                              .GetString()!,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PayPal] Erreur lors de la récupération des détails");
            return new PayPalOrderDetails { PayPalOrderId = paypalOrderId };
        }
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount = null, string? reason = null)
    {
        try
        {
            var token = await GetAccessTokenAsync();

            object body;
            if (amount.HasValue)
            {
                body = new
                {
                    amount = new { value = amount.Value.ToString("F2"), currency_code = _config.Currency },
                    note_to_payer = reason ?? "Remboursement"
                };
            }
            else
            {
                body = new { note_to_payer = reason ?? "Remboursement intégral" };
            }

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_config.BaseUrl}/v2/payments/captures/{captureId}/refund");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(JsonSerializer.Serialize(body),
                Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<JsonElement>(json);

            return new PayPalRefundResult
            {
                Success = response.IsSuccessStatusCode,
                RefundId = response.IsSuccessStatusCode
                    ? data.GetProperty("id").GetString()!
                    : null,
                Status = response.IsSuccessStatusCode
                    ? data.GetProperty("status").GetString()!
                    : "ERROR",
                Message = json
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PayPal] Erreur lors du remboursement");
            return new PayPalRefundResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }
}

public class PayPalPaymentRequest
{
    public string OrderId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
    public List<PayPalItem>? Items { get; set; }
}

public class PayPalItem
{
    public string Name { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
}

public class PayPalOrderResult
{
    public bool Success { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? ApproveUrl { get; set; }
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
}

public class PayPalCaptureResult
{
    public bool Success { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? CaptureId { get; set; }
    public string Status { get; set; } = "";
    public decimal? CapturedAmount { get; set; }
    public string Message { get; set; } = "";
}

public class PayPalOrderDetails
{
    public string PayPalOrderId { get; set; } = "";
    public string Status { get; set; } = "";
    public string Amount { get; set; } = "";
    public string Currency { get; set; } = "";
}

public class PayPalRefundResult
{
    public bool Success { get; set; }
    public string? RefundId { get; set; }
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
}
