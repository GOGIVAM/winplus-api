using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

public record CreateWithdrawalRequest(string Operator, string Phone, decimal Amount);

/// <summary>
/// Retrait Mobile Money du solde WinPlus (prompt_prof.md Module 7).
/// Le frontend (RevenueOverview.tsx) appelait déjà ces routes, mais aucun
/// contrôleur n'existait — le bouton "Retirer" ne faisait donc jamais rien de
/// réel (404 silencieux traité comme un succès optimiste côté UI). Corrigé
/// ici. Comme le reste du projet (voir TutorBookingService), aucun virement
/// Mobile Money automatisé n'existe : la demande réserve le montant sur le
/// solde et notifie l'admin pour le virement manuel.
/// </summary>
[ApiController]
[Route("api/withdrawals")]
[Authorize]
public class WithdrawalsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ITeacherService _teacherService;
    private readonly INtfyService _ntfy;
    private readonly ILogger<WithdrawalsController> _logger;

    private static readonly string[] ValidOperators = { "mtn", "orange" };

    public WithdrawalsController(ApplicationDbContext db, ITeacherService teacherService, INtfyService ntfy, ILogger<WithdrawalsController> logger)
    {
        _db = db;
        _teacherService = teacherService;
        _ntfy = ntfy;
        _logger = logger;
    }

    /// <summary>Demande un retrait — réserve immédiatement le montant sur le solde.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWithdrawalRequest req)
    {
        try
        {
            var userId = User.GetUserId();
            var op = ValidOperators.Contains(req.Operator) ? req.Operator : "mtn";

            if (req.Amount <= 0)
                return BadRequest(new { error = "Montant invalide." });
            if (string.IsNullOrWhiteSpace(req.Phone))
                return BadRequest(new { error = "Numéro de téléphone requis." });

            var balance = await _teacherService.GetSpendableBalanceAsync(userId);
            if (req.Amount > balance)
                return BadRequest(new { error = $"Solde insuffisant. Solde disponible : {balance:0} XAF." });

            var withdrawal = new Withdrawal
            {
                UserId = userId,
                Operator = op,
                Phone = req.Phone,
                AmountXaf = req.Amount,
                Status = "pending",
            };
            _db.Withdrawals.Add(withdrawal);
            await _db.SaveChangesAsync();

            await _ntfy.PublishAdminAsync("Retrait à traiter manuellement",
                $"Retrait #{withdrawal.Id} : {req.Amount:0} XAF vers {req.Phone} ({op.ToUpperInvariant()} MoMo) pour l'utilisateur #{userId}. " +
                "Effectuer le virement Mobile Money puis marquer le retrait comme traité.",
                tags: new[] { "moneybag" });

            return Ok(new { id = withdrawal.Id, status = withdrawal.Status });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating withdrawal");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>Statut d'un retrait (polling côté front).</summary>
    [HttpGet("status/{id:int}")]
    public async Task<IActionResult> GetStatus(int id)
    {
        var userId = User.GetUserId();
        var withdrawal = await _db.Withdrawals.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);
        if (withdrawal == null) return NotFound(new { error = "Retrait introuvable." });

        return Ok(new { id = withdrawal.Id, status = withdrawal.Status, amount = withdrawal.AmountXaf, requestedAt = withdrawal.RequestedAt });
    }

    /// <summary>Historique de mes retraits.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine()
    {
        var userId = User.GetUserId();
        var withdrawals = await _db.Withdrawals.AsNoTracking()
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.RequestedAt)
            .Select(w => new { w.Id, w.Operator, w.Phone, w.AmountXaf, w.Status, w.RequestedAt, w.ProcessedAt })
            .ToListAsync();
        return Ok(withdrawals);
    }

    public record MarkProcessedRequest(bool Success, string? Note);

    /// <summary>Admin : marque un retrait traité (viré) ou échoué (fonds relibérés).</summary>
    [HttpPost("{id:int}/mark-processed")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> MarkProcessed(int id, [FromBody] MarkProcessedRequest req)
    {
        var withdrawal = await _db.Withdrawals.FirstOrDefaultAsync(w => w.Id == id);
        if (withdrawal == null) return NotFound(new { error = "Retrait introuvable." });
        if (withdrawal.Status != "pending")
            return BadRequest(new { error = "Ce retrait a déjà été traité." });

        withdrawal.Status = req.Success ? "completed" : "failed";
        withdrawal.AdminNote = req.Note;
        withdrawal.ProcessedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _ntfy.PublishAsync($"winplus-user-{withdrawal.UserId}",
            req.Success ? "Retrait effectué" : "Retrait échoué",
            req.Success
                ? $"Ton retrait de {withdrawal.AmountXaf:0} XAF a été envoyé vers {withdrawal.Phone}."
                : $"Ton retrait de {withdrawal.AmountXaf:0} XAF a échoué. Le montant reste disponible sur ton solde. {req.Note}",
            userId: withdrawal.UserId, type: "Withdrawal");

        return Ok(new { success = true });
    }
}
