using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

public record StartWalletRechargeRequest(decimal Amount, string Operator, string Phone, string? ClientRequestId = null);

/// <summary>
/// Portefeuille de l'utilisateur connecté (lot 2, Module 3).
///
/// GET  /api/wallet/me           solde unifié : disponible, en attente, engagé par un retrait en cours
/// GET  /api/wallet/me/entries   historique des écritures, filtrable par source, paginé
/// POST /api/wallet/recharge     recharge Mobile Money (parcours d'encaissement existant)
///
/// Toutes les valeurs viennent du journal de portefeuille : aucun calcul de
/// solde côté client (web ou mobile).
/// </summary>
[ApiController]
[Route("api/wallet")]
[Authorize]
public class WalletController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IWalletService _wallet;
    private readonly IWalletTopUpService _topUps;
    private readonly ILogger<WalletController> _logger;

    public WalletController(ApplicationDbContext db, IWalletService wallet, IWalletTopUpService topUps, ILogger<WalletController> logger)
    {
        _db = db;
        _wallet = wallet;
        _topUps = topUps;
        _logger = logger;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMine()
    {
        var userId = User.GetUserId();
        var balance = await _wallet.GetBalanceAsync(userId);
        var active = await _db.Withdrawals.AsNoTracking()
            .Where(w => w.UserId == userId && (w.Status == WithdrawalStatus.Pending || w.Status == WithdrawalStatus.Processing))
            .OrderByDescending(w => w.RequestedAt)
            .FirstOrDefaultAsync();

        return Ok(new
        {
            data = new
            {
                availableXaf = balance.AvailableXaf,
                pendingXaf = balance.PendingXaf,
                engagedXaf = balance.EngagedXaf,
                isAnomaly = balance.IsAnomaly,
                minWithdrawalXaf = WithdrawalService.MinimumXaf,
                minRechargeXaf = WalletTopUpService.MinimumXaf,
                activeWithdrawal = active == null ? null : WithdrawalsController.ToDto(active),
            },
            success = true,
        });
    }

    [HttpGet("me/entries")]
    public async Task<IActionResult> GetEntries([FromQuery] string? source = "all", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = User.GetUserId();
        var (items, total) = await _wallet.GetHistoryAsync(userId, source, page, pageSize);
        return Ok(new { data = new { items, total, page = Math.Max(1, page), pageSize = Math.Clamp(pageSize, 1, 200) }, success = true });
    }

    [HttpPost("recharge")]
    public async Task<IActionResult> Recharge([FromBody] StartWalletRechargeRequest request)
    {
        try
        {
            var result = await _topUps.StartAsync(User.GetUserId(), request.Amount, request.Operator, request.Phone, request.ClientRequestId);
            return Ok(new
            {
                data = new
                {
                    topUpId = result.TopUpId,
                    orderId = result.OrderId,
                    paymentId = result.PaymentId,
                    status = result.Status,
                    amountXaf = result.AmountXaf,
                    duplicate = result.Duplicate,
                },
                success = true,
            });
        }
        catch (WithdrawalRejectedException ex)
        {
            return StatusCode(ex.HttpStatus, new { success = false, code = ex.Code, error = ex.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { success = false, error = "Service de paiement temporairement indisponible. Réessaie dans quelques instants." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Recharge refusée par NotchPay");
            return BadRequest(new { success = false, error = "Le paiement Mobile Money a été refusé. Vérifie le numéro et réessaie." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la recharge du portefeuille");
            return StatusCode(500, new { success = false, error = "Erreur serveur" });
        }
    }
}
