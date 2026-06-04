using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Backend.Models.DTOs;
using Backend.Services;
using Backend.Extensions;

namespace Backend.Controllers;

[ApiController]
[Route("api/payments")]
[Produces("application/json")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly INotchPayService _notchPay;
    private readonly ILogger<PaymentsController> _logger;
    private readonly IMemoryCache _cache;

    public PaymentsController(
        IPaymentService paymentService,
        INotchPayService notchPay,
        ILogger<PaymentsController> logger,
        IMemoryCache cache)
    {
        _paymentService = paymentService;
        _notchPay = notchPay;
        _logger = logger;
        _cache = cache;
    }

    private const int PaymentRateLimit = 5;
    private static readonly TimeSpan PaymentRateWindow = TimeSpan.FromMinutes(10);

    private bool IsPaymentRateLimited(int userId)
    {
        var key = $"payment_rate:{userId}";
        var now = DateTime.UtcNow;
        var cutoff = now - PaymentRateWindow;

        var timestamps = _cache.GetOrCreate(key, entry =>
        {
            entry.SlidingExpiration = PaymentRateWindow;
            return new List<DateTime>();
        })!;

        lock (timestamps)
        {
            timestamps.RemoveAll(t => t < cutoff);
            if (timestamps.Count >= PaymentRateLimit)
                return true;
            timestamps.Add(now);
            _cache.Set(key, timestamps, PaymentRateWindow);
        }
        return false;
    }

    /// <summary>POST /api/payments/initiate — Initier un paiement NotchPay (max 5 / 10 min / utilisateur)</summary>
    [HttpPost("initiate")]
    [Authorize]
    public async Task<IActionResult> Initiate([FromBody] InitiatePaymentRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var userId = User.GetUserId();

            if (IsPaymentRateLimited(userId))
            {
                _logger.LogWarning("Rate limit dépassé pour les paiements — userId={UserId}", userId);
                return StatusCode(429, new
                {
                    error = "too_many_requests",
                    message = "Trop de tentatives de paiement. Veuillez réessayer dans 10 minutes.",
                    retryAfterMinutes = 10
                });
            }

            var result = await _paymentService.InitiateNotchPayAsync(userId, request);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { error = "operator_unavailable", message = "Service de paiement temporairement indisponible. Réessayez dans quelques instants." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur initiation paiement");
            return StatusCode(500, new { error = "Erreur lors de l'initiation du paiement" });
        }
    }

    /// <summary>POST /api/payments/webhook/notchpay — Webhook NotchPay (signature HMAC-SHA256)</summary>
    [HttpPost("webhook/notchpay")]
    [AllowAnonymous]
    public async Task<IActionResult> NotchPayWebhook()
    {
        string payload;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            payload = await reader.ReadToEndAsync();

        var signature = Request.Headers["X-Notchpay-Signature"].FirstOrDefault()
            ?? Request.Headers["x-notchpay-signature"].FirstOrDefault();

        if (string.IsNullOrEmpty(signature) || !_notchPay.VerifyWebhookSignature(payload, signature))
        {
            _logger.LogWarning("Webhook NotchPay: signature invalide");
            return Unauthorized(new { error = "Signature invalide" });
        }

        NotchPayWebhookPayload? webhookData;
        try
        {
            webhookData = JsonSerializer.Deserialize<NotchPayWebhookPayload>(payload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Webhook NotchPay: payload JSON invalide");
            return BadRequest(new { error = "Payload invalide" });
        }

        if (webhookData?.Transaction == null)
            return BadRequest(new { error = "Transaction manquante dans le payload" });

        var eventId = webhookData.Transaction.Reference
            ?? $"notchpay-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        try
        {
            await _paymentService.HandleNotchPayWebhookAsync(
                eventId, webhookData.Event ?? "unknown", webhookData.Transaction);
            return Ok(new { received = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur traitement webhook NotchPay {EventId}", eventId);
            return StatusCode(500, new { error = "Erreur traitement webhook" });
        }
    }

    /// <summary>GET /api/payments/{id}/status — Statut d'un paiement (avec sync NotchPay)</summary>
    [HttpGet("{id:int}/status")]
    [Authorize]
    public async Task<IActionResult> GetStatus(int id)
    {
        try
        {
            var userId = User.GetUserId();
            var isAdmin = User.IsInRole("admin");
            var result = await _paymentService.GetPaymentStatusAsync(id, userId, isAdmin);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur récupération statut paiement {Id}", id);
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>GET /api/payments/history — Historique des paiements de l'utilisateur connecté</summary>
    [HttpGet("history")]
    [Authorize]
    public async Task<IActionResult> GetHistory([FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        try
        {
            var userId = User.GetUserId();
            var result = await _paymentService.GetUserPaymentHistoryAsync(userId, page, limit);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur récupération historique paiements");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>POST /api/payments/{id}/retry — Réessayer un paiement échoué</summary>
    [HttpPost("{id:int}/retry")]
    [Authorize]
    public async Task<IActionResult> Retry(int id)
    {
        try
        {
            var userId = User.GetUserId();
            var result = await _paymentService.RetryPaymentAsync(id, userId);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { error = "operator_unavailable", message = "Service de paiement indisponible" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur réessai paiement {Id}", id);
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>GET /api/admin/payments — Liste tous les paiements (admin)</summary>
    [HttpGet("/api/admin/payments")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetAllPayments(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        [FromQuery] string? status = null)
    {
        try
        {
            var result = await _paymentService.GetAllPaymentsAsync(page, limit, status);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur récupération paiements admin");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>GET /api/admin/payments/user/{userId} — Paiements d'un utilisateur spécifique (admin)</summary>
    [HttpGet("/api/admin/payments/user/{userId:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetPaymentsByUser(int userId, [FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        try
        {
            var result = await _paymentService.GetPaymentsByUserAsync(userId, page, limit);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur récupération paiements utilisateur {UserId}", userId);
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }
}
