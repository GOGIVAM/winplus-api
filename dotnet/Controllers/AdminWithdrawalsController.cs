using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Supervision des retraits (lot 2, Module 4, décision §16 du suivi :
/// supervision seule).
///
/// GET  /api/admin/withdrawals                 liste paginée, filtre par statut, recherche par professeur, tri par date
/// POST /api/admin/withdrawals/{id}/retry      action de secours : rejouer le transfert d'une demande en échec
/// POST /api/admin/withdrawals/{id}/close      action de secours : clôturer une demande en échec (fonds restitués)
///
/// Aucune validation de paiement, aucune action sur une demande en cours,
/// aucune modification de montant : le statut suit le transfert NotchPay réel.
/// Les actions de secours revérifient le statut chez NotchPay au moment d'agir
/// et sont tracées (administrateur, motif).
/// </summary>
[ApiController]
[Route("api/admin/withdrawals")]
[Authorize(Policy = "AdminOnly")]
public class AdminWithdrawalsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IWithdrawalService _withdrawals;
    private readonly ILogger<AdminWithdrawalsController> _logger;

    public AdminWithdrawalsController(ApplicationDbContext db, IWithdrawalService withdrawals, ILogger<AdminWithdrawalsController> logger)
    {
        _db = db;
        _withdrawals = withdrawals;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] string sort = "desc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Comptes supprimés inclus : leurs demandes passées restent visibles.
        var users = _db.Users.AsNoTracking().IgnoreQueryFilters();
        var query = from w in _db.Withdrawals.AsNoTracking()
                    join u in users on w.UserId equals u.Id into uj
                    from u in uj.DefaultIfEmpty()
                    select new { W = w, U = u };

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var s = status.Trim().ToLowerInvariant();
            query = s == "in_progress"
                ? query.Where(x => x.W.Status == WithdrawalStatus.Pending || x.W.Status == WithdrawalStatus.Processing)
                : s == "failed"
                    ? query.Where(x => x.W.Status == WithdrawalStatus.Failed || x.W.Status == WithdrawalStatus.Cancelled)
                    : query.Where(x => x.W.Status == s);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            var isId = int.TryParse(term, out var idTerm);
            query = query.Where(x => (isId && (x.W.Id == idTerm || x.W.UserId == idTerm))
                || (x.U != null && (x.U.Email.ToLower().Contains(term)
                    || ((x.U.FirstName ?? "") + " " + (x.U.LastName ?? "")).ToLower().Contains(term))));
        }

        var total = await query.CountAsync();
        var ordered = sort == "asc"
            ? query.OrderBy(x => x.W.RequestedAt).ThenBy(x => x.W.Id)
            : query.OrderByDescending(x => x.W.RequestedAt).ThenByDescending(x => x.W.Id);

        var rows = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var counts = await _db.Withdrawals.AsNoTracking()
            .GroupBy(w => w.Status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync();

        var items = rows.Select(x => new
        {
            withdrawal = AdminWalletController.AdminWithdrawalDto(x.W),
            teacher = x.U == null ? null : new
            {
                id = x.U.Id,
                name = $"{x.U.FirstName} {x.U.LastName}".Trim(),
                email = x.U.Email,
                isDeleted = x.U.IsDeleted,
                isActive = x.U.IsActive,
            },
            // Actions de secours possibles : seulement sur un échec non clôturé, sans anomalie.
            canRescue = (x.W.Status == WithdrawalStatus.Failed || x.W.Status == WithdrawalStatus.Cancelled)
                     && x.W.ClosedAt == null && x.W.Anomaly == null,
        });

        return Ok(new { data = new { items, total, page, pageSize, counts }, success = true });
    }

    public record RescueRequest(string Reason);

    [HttpPost("{id:int}/retry")]
    public Task<IActionResult> Retry(int id, [FromBody] RescueRequest request) =>
        RescueAsync(id, request, retry: true);

    [HttpPost("{id:int}/close")]
    public Task<IActionResult> Close(int id, [FromBody] RescueRequest request) =>
        RescueAsync(id, request, retry: false);

    private async Task<IActionResult> RescueAsync(int id, RescueRequest request, bool retry)
    {
        var adminId = User.GetUserId();
        try
        {
            var result = retry
                ? await _withdrawals.RetryAsync(id, adminId, request?.Reason ?? string.Empty)
                : await _withdrawals.CloseAsync(id, adminId, request?.Reason ?? string.Empty);
            _logger.LogInformation("Action de secours {Action} sur le retrait {Id} par l'administrateur {AdminId}",
                retry ? "rejeu" : "clôture", id, adminId);
            return Ok(new { success = true, data = AdminWalletController.AdminWithdrawalDto(result) });
        }
        catch (WithdrawalRejectedException ex)
        {
            return StatusCode(ex.HttpStatus, new { success = false, code = ex.Code, error = ex.Message, balanceXaf = ex.AvailableXaf });
        }
        catch (Exception ex) when (DbConcurrency.IsSerializationFailure(ex))
        {
            return StatusCode(409, new { success = false, error = "Opération concurrente en cours sur ce portefeuille. Réessaie." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'action de secours sur le retrait {Id}", id);
            return StatusCode(500, new { success = false, error = "Erreur serveur" });
        }
    }
}
