using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

/// <param name="Operator">mtn | orange.</param>
/// <param name="Phone">Numéro Mobile Money (6XXXXXXXX, 2376XXXXXXXX ou +2376XXXXXXXX).</param>
/// <param name="Amount">Montant entier en FCFA, au moins 500, sans plafond.</param>
/// <param name="ClientRequestId">Identifiant unique de la soumission, généré par le client : un rejeu (double clic, réseau) renvoie la même demande.</param>
public record CreateWithdrawalRequest(string Operator, string Phone, decimal Amount, string? ClientRequestId = null);

/// <summary>
/// Retrait Mobile Money du solde WinPlus.
///
/// Lot 2, Module 2 (décision §15 du suivi) : le virement est automatisé via
/// l'API de transfert NotchPay (<see cref="IWithdrawalService"/>). La demande
/// réserve les fonds dans le journal de portefeuille puis crée le transfert ;
/// le statut renvoyé est le statut réel du transfert, relu chez NotchPay à
/// chaque consultation tant qu'il est en cours. Aucun délai propre n'est
/// promis, aucun frais n'est prélevé, aucun plafond n'est appliqué.
/// </summary>
[ApiController]
[Route("api/withdrawals")]
// Module 20 : une demande de retrait exige une adresse vérifiée (seul flux qui
// fait sortir de l'argent réel de la plateforme).
[Authorize(Policy = "VerifiedEmailOnly")]
public class WithdrawalsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IWithdrawalService _withdrawals;
    private readonly ILogger<WithdrawalsController> _logger;

    public WithdrawalsController(ApplicationDbContext db, IWithdrawalService withdrawals, ILogger<WithdrawalsController> logger)
    {
        _db = db;
        _withdrawals = withdrawals;
        _logger = logger;
    }

    /// <summary>Forme publique d'un retrait : numéro masqué, motif d'échec exploitable.</summary>
    public static object ToDto(Withdrawal w) => new
    {
        id = w.Id,
        status = w.Status,
        amount = w.AmountXaf,
        amountXaf = w.AmountXaf,
        @operator = w.Operator,
        phoneMasked = WithdrawalLabels.MaskPhone(w.Phone),
        requestedAt = w.RequestedAt,
        processedAt = w.ProcessedAt,
        failureReason = w.FailureReason,
        // Rappel affiché au client : le traitement se poursuit même s'il
        // quitte l'écran.
        inProgress = WithdrawalStatus.IsInFlight(w.Status),
    };

    /// <summary>Règles de retrait appliquées par le serveur (affichées par les clients, jamais décidées par eux).</summary>
    [HttpGet("rules")]
    [Authorize]
    public IActionResult GetRules() => Ok(new
    {
        minAmountXaf = WithdrawalService.MinimumXaf,
        maxAmountXaf = (decimal?)null,
        feeXaf = 0,
        operators = new[] { "mtn", "orange" },
    });

    /// <summary>Demande un retrait : réserve les fonds puis déclenche le transfert NotchPay.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWithdrawalRequest req)
    {
        try
        {
            var userId = User.GetUserId();
            var result = await _withdrawals.RequestAsync(userId, req.Operator, req.Phone, req.Amount, req.ClientRequestId);
            return Ok(new { success = true, duplicate = result.Duplicate, data = ToDto(result.Withdrawal),
                // Compatibilité avec les clients qui lisent id/status à la racine.
                id = result.Withdrawal.Id, status = result.Withdrawal.Status });
        }
        catch (WithdrawalRejectedException ex)
        {
            return StatusCode(ex.HttpStatus, new { success = false, code = ex.Code, error = ex.Message, balanceXaf = ex.AvailableXaf });
        }
        catch (Exception ex) when (DbConcurrency.IsSerializationFailure(ex))
        {
            return StatusCode(409, new { success = false, code = "concurrent", error = "Une autre opération sur ton solde est en cours. Réessaie dans un instant." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating withdrawal");
            return StatusCode(500, new { success = false, error = "Erreur serveur" });
        }
    }

    /// <summary>Statut réel d'un retrait (relu chez NotchPay s'il est en cours).</summary>
    [HttpGet("status/{id:int}")]
    public async Task<IActionResult> GetStatus(int id)
    {
        var userId = User.GetUserId();
        var withdrawal = await _db.Withdrawals.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);
        if (withdrawal == null) return NotFound(new { error = "Retrait introuvable." });

        if (WithdrawalStatus.IsInFlight(withdrawal.Status) && withdrawal.TransferReference != null)
            withdrawal = await _withdrawals.SyncAsync(id) ?? withdrawal;

        return Ok(ToDto(withdrawal));
    }

    /// <summary>Retrait en cours de l'utilisateur, s'il y en a un (pour réafficher son état plutôt qu'un formulaire vierge).</summary>
    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var userId = User.GetUserId();
        var withdrawal = await _db.Withdrawals.AsNoTracking()
            .Where(w => w.UserId == userId && (w.Status == WithdrawalStatus.Pending || w.Status == WithdrawalStatus.Processing))
            .OrderByDescending(w => w.RequestedAt)
            .FirstOrDefaultAsync();
        return Ok(new { data = withdrawal == null ? null : ToDto(withdrawal), success = true });
    }

    /// <summary>Historique de mes retraits.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine()
    {
        var userId = User.GetUserId();
        var withdrawals = await _db.Withdrawals.AsNoTracking()
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.RequestedAt)
            .ToListAsync();
        return Ok(withdrawals.Select(ToDto));
    }

    public record MarkProcessedRequest(bool Success, string? Note);

    /// <summary>
    /// Admin : demandes manuelles antérieures à l'automatisation uniquement
    /// (sans transfert NotchPay). Une demande automatisée est refusée (409) :
    /// son statut suit le transfert réel, il n'existe pas de validation
    /// manuelle des retraits (supervision seule, décision §16).
    /// </summary>
    [HttpPost("{id:int}/mark-processed")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> MarkProcessed(int id, [FromBody] MarkProcessedRequest req)
    {
        try
        {
            var done = await _withdrawals.SettleLegacyManualAsync(id, User.GetUserId(), req.Success, req.Note);
            return Ok(new { success = true, data = ToDto(done) });
        }
        catch (WithdrawalRejectedException ex)
        {
            return StatusCode(ex.HttpStatus, new { success = false, code = ex.Code, error = ex.Message });
        }
    }
}
