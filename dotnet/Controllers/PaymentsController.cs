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
    private readonly ITutorBookingService _tutorBookingService;
    private readonly INotchPayService _notchPay;
    private readonly ILogger<PaymentsController> _logger;
    private readonly IMemoryCache _cache;
    private readonly IWithdrawalService _withdrawals;

    public PaymentsController(
        IPaymentService paymentService,
        ITutorBookingService tutorBookingService,
        INotchPayService notchPay,
        ILogger<PaymentsController> logger,
        IMemoryCache cache,
        IWithdrawalService withdrawals)
    {
        _withdrawals = withdrawals;
        _paymentService = paymentService;
        _tutorBookingService = tutorBookingService;
        _notchPay = notchPay;
        _logger = logger;
        _cache = cache;
    }

    private const int PaymentRateLimit = 5;
    private static readonly TimeSpan PaymentRateWindow = TimeSpan.FromMinutes(10);

    private bool IsPaymentRateLimited(string key)
    {
        key = $"payment_rate:{key}";
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

    /// <summary>POST /api/payments/initiate  Initier un paiement NotchPay (max 5 / 10 min)</summary>
    [HttpPost("initiate")]
    // Décision 9.2 du suivi : un compte est obligatoire pour acheter. L'accès
    // anonyme ne servait qu'au parcours de commande invité, supprimé ; les
    // commandes invité historiques ne sont pas migrées.
    [Authorize]
    public async Task<IActionResult> Initiate([FromBody] InitiatePaymentRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var isAuth = User.Identity?.IsAuthenticated == true;
            int? userId = isAuth ? User.GetUserId() : null;

            // Rate limiting : par userId si connecté, par IP sinon
            var rateLimitKey = userId.HasValue
                ? userId.Value.ToString()
                : (HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

            if (IsPaymentRateLimited(rateLimitKey))
            {
                _logger.LogWarning("Rate limit dépassé pour les paiements  key={Key}", rateLimitKey);
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
        catch (UnauthorizedAccessException)
        {
            // Décision 10.5 : commande d'un autre compte.
            return StatusCode(403, new { error = "Accès refusé." });
        }
        catch (OrderNotPayableException ex)
        {
            // Décision 10.5 : état de commande incompatible avec un nouveau
            // paiement (remboursement en cours, commande déjà réglée). Motif
            // renvoyé tel quel, lu par le web dans `error` sur un 400.
            return BadRequest(new { error = ex.Message, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "NotchPay a refusé la demande de paiement");
            return BadRequest(new { error = "payment_rejected", message = ex.Message });
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

    /// <summary>POST /api/payments/webhook/notchpay  Webhook NotchPay (signature HMAC-SHA256)</summary>
    [HttpPost("webhook/notchpay")]
    [AllowAnonymous]
    public async Task<IActionResult> NotchPayWebhook()
    {
        Request.EnableBuffering();
        string payload;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
            payload = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        var signature = Request.Headers["x-notch-signature"].FirstOrDefault()
            ?? Request.Headers["X-Notch-Signature"].FirstOrDefault()
            ?? Request.Headers["X-Notchpay-Signature"].FirstOrDefault()
            ?? Request.Headers["x-notchpay-signature"].FirstOrDefault();

        _logger.LogInformation(
            "Webhook NotchPay reçu  signature={Sig} payloadLen={Len} hasHeaders={Headers}",
            signature ?? "(aucune)",
            payload.Length,
            string.Join(", ", Request.Headers.Keys));

        if (string.IsNullOrEmpty(signature) || !_notchPay.VerifyWebhookSignature(payload, signature))
        {
            _logger.LogWarning("Webhook NotchPay: signature invalide  signature={Sig}", signature ?? "(aucune)");
            return Unauthorized(new { error = "Signature invalide" });
        }

        // Lot 2, Module 2 : notifications de transfert sortant (retraits).
        // Même vérification de signature que l'encaissement (ci-dessus). Le
        // contenu de la notification sert seulement à retrouver le retrait :
        // l'issue appliquée est toujours le statut relu chez NotchPay, ce qui
        // rend le traitement indépendant du format exact de la notification
        // et idempotent (une notification reçue deux fois ne change rien).
        var transferReference = TryReadTransferReference(payload);
        if (transferReference != null)
        {
            try
            {
                await _withdrawals.SyncByReferenceAsync(transferReference);
                return Ok(new { received = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur traitement notification de transfert {Ref}", transferReference);
                return StatusCode(500, new { error = "Erreur traitement webhook" });
            }
        }

        NotchPayWebhookPayload? webhookData;
        try
        {
            webhookData = JsonSerializer.Deserialize<NotchPayWebhookPayload>(payload,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Webhook NotchPay: payload JSON invalide");
            return BadRequest(new { error = "Payload invalide" });
        }

        if (webhookData?.Transaction == null)
            return BadRequest(new { error = "Transaction manquante dans le payload" });

        _logger.LogInformation(
            "Webhook NotchPay event={Event} ref={Ref} status={Status} operator={Op} amount={Amount}",
            webhookData.Event, webhookData.Transaction.Reference,
            webhookData.Transaction.Status, webhookData.Transaction.Operator,
            webhookData.Transaction.Amount);

        var eventId = webhookData.Transaction.Reference
            ?? $"notchpay-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        try
        {
            // Les réservations Répétiteur (Module 6) portent leur propre paiement,
            // hors du module Order/Payment : référence préfixée "TBK-", distincte
            // du "WP-" utilisé par le checkout catalogue.
            var handledAsBooking = await _tutorBookingService.TryHandleNotchPayWebhookAsync(
                eventId, webhookData.Event ?? "unknown", webhookData.Transaction);
            if (!handledAsBooking)
            {
                await _paymentService.HandleNotchPayWebhookAsync(
                    eventId, webhookData.Event ?? "unknown", webhookData.Transaction);
            }
            return Ok(new { received = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur traitement webhook NotchPay {EventId}", eventId);
            return StatusCode(500, new { error = "Erreur traitement webhook" });
        }
    }

    /// <summary>
    /// Référence d'une notification de transfert (<c>transfer.*</c>), ou null
    /// pour toute autre notification. Tolère les deux formes observées
    /// (<c>event</c>/<c>type</c>, objet sous <c>data</c>/<c>transfer</c>/<c>transaction</c>).
    /// </summary>
    private static string? TryReadTransferReference(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string? eventName = null;
            foreach (var name in new[] { "event", "type" })
                if (root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String) { eventName = e.GetString(); break; }
            if (eventName == null || !eventName.StartsWith("transfer.", StringComparison.OrdinalIgnoreCase)) return null;

            foreach (var container in new[] { "data", "transfer", "transaction" })
            {
                if (!root.TryGetProperty(container, out var obj) || obj.ValueKind != JsonValueKind.Object) continue;
                foreach (var key in new[] { "reference", "id" })
                    if (obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                        return v.GetString();
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>GET /api/payments/{id}/status  Statut d'un paiement (polling depuis le frontend)</summary>
    [HttpGet("{id:int}/status")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStatus(int id)
    {
        try
        {
            int? userId = null;
            var isAdmin = false;
            if (User.Identity?.IsAuthenticated == true)
            {
                try { userId = User.GetUserId(); isAdmin = User.IsInRole("admin"); }
                catch { /* claims manquants  traiter comme anonyme */ }
            }
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

    /// <summary>GET /api/payments/history  Historique des paiements de l'utilisateur connecté</summary>
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

    /// <summary>POST /api/payments/{id}/retry  Réessayer un paiement échoué</summary>
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

    /// <summary>GET /api/admin/payments  Liste tous les paiements (admin)</summary>
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

    /// <summary>POST /api/payments/confirm  Alias de /initiate pour compatibilité frontend legacy.</summary>
    [HttpPost("confirm")]
    public async Task<IActionResult> ConfirmPayment([FromBody] InitiatePaymentRequest request)
    {
        try
        {
            return await Initiate(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur confirmation paiement");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>POST /api/payments/{id}/verify  Vérifie le statut d'un paiement.</summary>
    [HttpPost("{id:int}/verify")]
    [Authorize]
    public async Task<IActionResult> VerifyPayment(int id)
    {
        try
        {
            var userId = User.GetUserId();
            var result = await _paymentService.GetPaymentStatusAsync(id, userId, false);
            return Ok(new { data = result, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur vérification paiement {PaymentId}", id);
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>GET /api/admin/payments/user/{userId}  Paiements d'un utilisateur spécifique (admin)</summary>
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
