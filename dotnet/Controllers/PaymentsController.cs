using Microsoft.AspNetCore.Mvc;
using Backend.Models.DTOs;
using Backend.Services;
using Backend.Extensions;

namespace Backend.Controllers;

/// <summary>
/// Controller pour gérer les paiements
/// </summary>
[ApiController]
[Route("api/payments")]
[Produces("application/json")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(IPaymentService paymentService, ILogger<PaymentsController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    /// <summary>
    /// Crée un nouveau paiement
    /// </summary>
    /// <param name="request">Détails du paiement</param>
    /// <returns>Les détails du paiement créé</returns>
    /// <response code="200">Paiement créé avec succès</response>
    /// <response code="400">Requête invalide</response>
    /// <response code="500">Erreur serveur</response>
    [HttpPost]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreatePayment([FromBody] CreatePaymentRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = User.GetUserId();

            var response = await _paymentService.CreatePaymentAsync(userId, request);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Argument invalide lors de la création du paiement");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la création du paiement");
            return StatusCode(500, new { error = "Erreur lors de la création du paiement" });
        }
    }

    /// <summary>
    /// Récupère les détails d'un paiement
    /// </summary>
    /// <param name="id">ID du paiement</param>
    /// <returns>Les détails du paiement</returns>
    /// <response code="200">Paiement trouvé</response>
    /// <response code="404">Paiement non trouvé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPayment(int id)
    {
        try
        {
            var response = await _paymentService.GetPaymentByIdAsync(id);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Paiement non trouvé: {PaymentId}", id);
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération du paiement");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>
    /// Confirme un paiement en attente
    /// </summary>
    /// <param name="id">ID du paiement</param>
    /// <param name="request">Données de confirmation</param>
    /// <returns>Les détails du paiement mis à jour</returns>
    /// <response code="200">Paiement confirmé avec succès</response>
    /// <response code="400">Requête invalide</response>
    /// <response code="404">Paiement non trouvé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpPost("{id}/confirm")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ConfirmPayment(int id, [FromBody] ConfirmPaymentRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var response = await _paymentService.ConfirmPaymentAsync(id, request);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Argument invalide");
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Opération invalide");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la confirmation du paiement");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>
    /// Effectue un remboursement de paiement
    /// </summary>
    /// <param name="id">ID du paiement</param>
    /// <param name="request">Détails du remboursement</param>
    /// <returns>Les détails du paiement mis à jour</returns>
    /// <response code="200">Remboursement effectué avec succès</response>
    /// <response code="400">Requête invalide</response>
    /// <response code="404">Paiement non trouvé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpPost("{id}/refund")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RefundPayment(int id, [FromBody] RefundPaymentRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var response = await _paymentService.RefundPaymentAsync(id, request);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Argument invalide");
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Opération invalide");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du remboursement");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>
    /// Réessaie un paiement échoué
    /// </summary>
    /// <param name="id">ID du paiement</param>
    /// <param name="request">Détails de réessai</param>
    /// <returns>Les détails du paiement mis à jour</returns>
    /// <response code="200">Réessai lancé avec succès</response>
    /// <response code="400">Requête invalide</response>
    /// <response code="404">Paiement non trouvé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpPost("{id}/retry")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RetryPayment(int id, [FromBody] RetryPaymentRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var response = await _paymentService.RetryPaymentAsync(id, request);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Argument invalide");
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Opération invalide");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du réessai du paiement");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>
    /// Annule un paiement
    /// </summary>
    /// <param name="id">ID du paiement</param>
    /// <returns>Résultat de l'annulation</returns>
    /// <response code="200">Paiement annulé avec succès</response>
    /// <response code="404">Paiement non trouvé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CancelPayment(int id)
    {
        try
        {
            var result = await _paymentService.CancelPaymentAsync(id);
            return Ok(new { success = result, message = "Paiement annulé avec succès" });
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Paiement non trouvé");
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Opération invalide");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'annulation du paiement");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }
}
