using Microsoft.AspNetCore.Mvc;
using Backend.Extensions;
using Backend.Services.PaymentProviders;
using Backend.Models.DTOs;

namespace Backend.Controllers;

[ApiController]
[Route("api/payments")]
[Produces("application/json")]
public class PaymentProvidersController : ControllerBase
{
    private readonly MtnMomoService _mtn;
    private readonly OrangeMoneyService _orange;
    private readonly WaveService _wave;
    private readonly StripeService _stripe;
    private readonly PayPalService _paypal;
    private readonly ILogger<PaymentProvidersController> _logger;

    public PaymentProvidersController(
        MtnMomoService mtn,
        OrangeMoneyService orange,
        WaveService wave,
        StripeService stripe,
        PayPalService paypal,
        ILogger<PaymentProvidersController> logger)
    {
        _mtn    = mtn;
        _orange = orange;
        _wave   = wave;
        _stripe = stripe;
        _paypal = paypal;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // MTN MOBILE MONEY
    // ══════════════════════════════════════════════════════════════════════════

    [HttpPost("mtn/initiate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MtnInitiate([FromBody] MtnInitiateDto dto)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (string.IsNullOrWhiteSpace(dto.CustomerPhone))
                return BadRequest(new { success = false, message = "Numéro de téléphone requis." });

            if (dto.Amount <= 0)
                return BadRequest(new { success = false, message = "Montant invalide." });

            // ── CORRECTION CLÉ : Normaliser le numéro ──────────────────────
            // Le frontend envoie déjà "237650XXXXXX" (sans +), mais on sécurise
            var phone = NormalizePhone(dto.CustomerPhone);

            _logger.LogInformation("[MTN Controller] Initiation — Order: {OrderId}, Phone: {Phone}, Amount: {Amount} {Currency}",
                dto.OrderId, MaskPhone(phone), dto.Amount, dto.Currency ?? "XAF");

            var req = new MtnPaymentRequest
            {
                OrderId     = dto.OrderId,
                PhoneNumber = phone,          // ← numéro normalisé
                Amount      = dto.Amount,
                Currency    = dto.Currency ?? "XAF",
                Description = dto.Description ?? $"Commande Win+ #{dto.OrderId}",
            };

            var result = await _mtn.RequestToPayAsync(req);

            if (!result.Success)
            {
                _logger.LogWarning("[MTN Controller] Échec — Order: {OrderId}, Message: {Msg}",
                    dto.OrderId, result.Message);
                return BadRequest(new { success = false, message = result.Message });
            }

            _logger.LogInformation("[MTN Controller] ✅ Succès — Order: {OrderId}, ReferenceId: {RefId}",
                dto.OrderId, result.ReferenceId);

            return Ok(new
            {
                success     = true,
                referenceId = result.ReferenceId,
                statusCode  = result.StatusCode,
                message     = result.Message,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MTN Controller] Erreur inattendue — Order: {OrderId}", dto?.OrderId);
            return StatusCode(500, new { success = false, message = "Erreur serveur : " + ex.Message });
        }
    }

    [HttpGet("mtn/status/{referenceId}")]
    [ProducesResponseType(typeof(MtnPaymentStatus), StatusCodes.Status200OK)]
    public async Task<IActionResult> MtnStatus(string referenceId)
    {
        try
        {
            var status = await _mtn.GetPaymentStatusAsync(referenceId);
            _logger.LogDebug("[MTN Controller] Status {RefId} → {Status}", referenceId, status.Status);
            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MTN Controller] Erreur vérification statut {RefId}", referenceId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ORANGE MONEY
    // ══════════════════════════════════════════════════════════════════════════

    [HttpPost("orange/initiate")]
    [ProducesResponseType(typeof(OrangePaymentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> OrangeInitiate([FromBody] OrangePaymentRequest req)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _orange.InitiateWebPayAsync(req);
            if (!result.Success)
                return BadRequest(result);

            return Ok(new { result.PaymentUrl, result.PayToken });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Orange] Erreur initiation paiement");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("orange/notify")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult OrangeNotify([FromBody] object rawBody)
    {
        try
        {
            var json   = System.Text.Json.JsonSerializer.Serialize(rawBody);
            var result = _orange.ProcessCallback(json);
            _logger.LogInformation("[Orange] Callback: {Status} - Order: {OrderId}", result.Status, result.OrderId);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Orange] Erreur traitement callback");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("orange/status/{payToken}")]
    [ProducesResponseType(typeof(OrangePaymentStatus), StatusCodes.Status200OK)]
    public async Task<IActionResult> OrangeStatus(string payToken)
    {
        try
        {
            var status = await _orange.CheckTransactionStatusAsync(payToken);
            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Orange] Erreur vérification statut");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // WAVE
    // ══════════════════════════════════════════════════════════════════════════

    [HttpPost("wave/initiate")]
    [ProducesResponseType(typeof(WaveCheckoutResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> WaveInitiate([FromBody] WavePaymentRequest req)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _wave.CreateCheckoutSessionAsync(req);
            if (!result.Success)
                return BadRequest(result);

            return Ok(new { result.WaveLaunchUrl, result.CheckoutSessionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Wave] Erreur initiation paiement");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("wave/status/{sessionId}")]
    [ProducesResponseType(typeof(WaveSessionStatus), StatusCodes.Status200OK)]
    public async Task<IActionResult> WaveStatus(string sessionId)
    {
        try
        {
            var status = await _wave.GetSessionStatusAsync(sessionId);
            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Wave] Erreur vérification statut");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("wave/webhook")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult WaveWebhook()
    {
        try
        {
            var signature     = Request.Headers["Wave-Signature"].ToString();
            var body          = new System.IO.StreamReader(Request.Body).ReadToEndAsync().Result;
            var webhookSecret = "votre_webhook_secret_wave";

            var result = _wave.ProcessWebhook(body, signature, webhookSecret);
            if (!result.Valid)
                return Unauthorized();

            _logger.LogInformation("[Wave] Event: {Type}", result.EventType);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Wave] Erreur webhook");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("wave/success")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> WaveSuccess([FromQuery] string session_id)
    {
        try
        {
            var status = await _wave.GetSessionStatusAsync(session_id);
            return status.Status == "succeeded"
                ? Redirect($"/commande/merci?ref={status.ClientReference}")
                : Redirect("/commande/erreur");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Wave] Erreur success redirect");
            return Redirect("/commande/erreur");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // STRIPE
    // ══════════════════════════════════════════════════════════════════════════

    [HttpPost("stripe/initiate")]
    [ProducesResponseType(typeof(StripeCheckoutResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StripeInitiate([FromBody] StripePaymentRequest req)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _stripe.CreateCheckoutSessionAsync(req);
            if (!result.Success)
                return BadRequest(result);

            return Ok(new { checkoutUrl = result.CheckoutUrl, result.SessionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Stripe] Erreur initiation paiement");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("stripe/status/{sessionId}")]
    [ProducesResponseType(typeof(StripeSessionStatus), StatusCodes.Status200OK)]
    public async Task<IActionResult> StripeStatus(string sessionId)
    {
        try
        {
            var status = await _stripe.GetCheckoutSessionAsync(sessionId);
            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Stripe] Erreur vérification statut");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("stripe/webhook")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> StripeWebhook()
    {
        try
        {
            var signature = Request.Headers["Stripe-Signature"].ToString();
            using var reader = new System.IO.StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            var result = _stripe.ProcessWebhook(body, signature);
            if (!result.Valid)
                return Unauthorized();

            switch (result.EventType)
            {
                case "checkout.session.completed":
                    _logger.LogInformation("[Stripe] Paiement réussi - Order: {OrderId}", result.OrderId);
                    break;
                case "payment_intent.payment_failed":
                    _logger.LogWarning("[Stripe] Paiement échoué - ID: {Id}", result.ObjectId);
                    break;
            }
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Stripe] Erreur webhook");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // PAYPAL
    // ══════════════════════════════════════════════════════════════════════════

    [HttpPost("paypal/initiate")]
    [ProducesResponseType(typeof(PayPalOrderResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PayPalInitiate([FromBody] PayPalPaymentRequest req)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _paypal.CreateOrderAsync(req);
            if (!result.Success)
                return BadRequest(result);

            return Ok(new { result.ApproveUrl, result.PayPalOrderId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PayPal] Erreur création commande");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("paypal/capture")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> PayPalCapture([FromQuery] string token)
    {
        try
        {
            var result = await _paypal.CaptureOrderAsync(token);
            if (!result.Success)
                return Redirect("/commande/erreur");

            _logger.LogInformation("[PayPal] Capturé: {Amount}", result.CapturedAmount);
            return Redirect($"/commande/merci?paypal_order={result.PayPalOrderId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PayPal] Erreur capture");
            return Redirect("/commande/erreur");
        }
    }

    [HttpGet("paypal/cancel")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult PayPalCancel() => Redirect("/commande/annulee");

    // ══════════════════════════════════════════════════════════════════════════
    // UTILITAIRES PRIVÉS
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Normalise un numéro de téléphone en MSISDN pur (sans +, espaces, tirets).
    /// Ex: "+237 650-12-34-56" → "237650123456"
    ///     "650123456"         → "650123456"  (le service ajoutera le dialCode si besoin)
    /// </summary>
    private static string NormalizePhone(string phone)
        => phone.TrimStart('+')
                .Replace(" ", "")
                .Replace("-", "")
                .Replace(".", "")
                .Replace("(", "")
                .Replace(")", "");

    private static string MaskPhone(string phone)
        => phone.Length > 6 ? phone[..3] + "***" + phone[^3..] : "***";
}