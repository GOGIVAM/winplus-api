using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Administration du journal de portefeuille (lot 2).
///
/// Module 1 :
///   POST /api/admin/wallet/backfill          reprise de l'historique (idempotente) + preuve d'égalité
///   GET  /api/admin/wallet/backfill/compare  comparaison seule, sans écriture
///
/// Module 3 (décision §12 : vue de trésorerie + crédit manuel, rien d'autre) :
///   GET  /api/admin/wallet/treasury          trésorerie : encaissé, part plateforme, dû aux professeurs
///   GET  /api/admin/wallet/users             portefeuilles (recherche par nom ou e-mail)
///   GET  /api/admin/wallet/users/{userId}    portefeuille d'un utilisateur : solde, écritures, retraits
///   POST /api/admin/wallet/credits           crédit manuel motivé vers un professeur ou un parent
///
/// Il n'existe volontairement aucune action de débit manuel d'un portefeuille
/// utilisateur, ni d'approvisionnement de la trésorerie (gérée hors
/// application chez NotchPay).
/// </summary>
[ApiController]
[Route("api/admin/wallet")]
[Authorize(Policy = "AdminOnly")]
public partial class AdminWalletController : ControllerBase
{
    private readonly IWalletBackfillService _backfill;
    private readonly IWalletService _wallet;
    private readonly ILogger<AdminWalletController> _logger;
    private readonly ApplicationDbContext _db;
    private readonly INtfyService _ntfy;

    public AdminWalletController(IWalletBackfillService backfill, IWalletService wallet, ILogger<AdminWalletController> logger,
        ApplicationDbContext db, INtfyService ntfy)
    {
        _db = db;
        _ntfy = ntfy;
        _backfill = backfill;
        _wallet = wallet;
        _logger = logger;
    }

    /// <summary>
    /// Rejoue les événements passés en écritures (mêmes clés que les écritures
    /// en direct : relancer est sans effet de bord), puis compare chaque solde
    /// au calcul historique. <c>equal = false</c> liste les écarts : la bascule
    /// n'est pas validée tant qu'il en reste.
    /// </summary>
    [HttpPost("backfill")]
    public async Task<IActionResult> Backfill(CancellationToken ct)
    {
        try
        {
            var report = await _backfill.RunAsync(ct);
            _logger.LogInformation("Reprise du journal lancée par l'administrateur {AdminId} : égalité = {Equal}", User.GetUserId(), report.Equal);
            return Ok(new { data = report, equal = report.Equal, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la reprise du journal de portefeuille");
            return StatusCode(500, new { success = false, error = "Erreur serveur" });
        }
    }

    [HttpGet("backfill/compare")]
    public async Task<IActionResult> Compare(CancellationToken ct)
    {
        var report = await _backfill.CompareAsync(ct);
        return Ok(new { data = report, equal = report.Equal, success = true });
    }

    // ── Module 3 : trésorerie ───────────────────────────────────────────

    /// <summary>
    /// Vue de trésorerie, lecture seule. Les montants dus et la part plateforme
    /// viennent du journal ; l'encaissé vient des paiements NotchPay confirmés
    /// (l'argent entrant n'est pas un solde de portefeuille).
    /// </summary>
    [HttpGet("treasury")]
    public async Task<IActionResult> Treasury()
    {
        var entries = _db.WalletTransactions.AsNoTracking();
        var individual = entries.Where(t => t.OwnerType != WalletOwnerTypes.Platform);
        var platform = entries.Where(t => t.OwnerType == WalletOwnerTypes.Platform);

        var collectedPayments = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == "completed").SumAsync(p => (decimal?)p.Amount) ?? 0m;
        var collectedTutoring = await _db.TutorBookings.AsNoTracking()
            .Where(b => b.PaymentStatus == "completed").SumAsync(b => (decimal?)b.PriceXaf) ?? 0m;

        var platformConfirmed = await platform.Where(t => t.Status == WalletEntryStatus.Confirmed).SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var platformPending = await platform.Where(t => t.Status == WalletEntryStatus.Pending).SumAsync(t => (decimal?)t.Amount) ?? 0m;

        var dueAvailable = await individual.Where(t => t.Status == WalletEntryStatus.Confirmed).SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var duePending = await individual.Where(t => t.Status == WalletEntryStatus.Pending).SumAsync(t => (decimal?)t.Amount) ?? 0m;

        var inFlightRows = _db.Withdrawals.AsNoTracking()
            .Where(w => w.Status == WithdrawalStatus.Pending || w.Status == WithdrawalStatus.Processing);
        var inFlightCount = await inFlightRows.CountAsync();
        var inFlightAmount = await inFlightRows.SumAsync(w => (decimal?)w.AmountXaf) ?? 0m;
        var paidOut = await _db.Withdrawals.AsNoTracking()
            .Where(w => w.Status == WithdrawalStatus.Completed).SumAsync(w => (decimal?)w.AmountXaf) ?? 0m;
        var failedOpen = await _db.Withdrawals.AsNoTracking()
            .CountAsync(w => (w.Status == WithdrawalStatus.Failed || w.Status == WithdrawalStatus.Cancelled) && w.ClosedAt == null);

        var recharges = await individual.Where(t => t.EntryType == WalletEntryTypes.Recharge && t.Status == WalletEntryStatus.Confirmed).SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var adminCredits = await individual.Where(t => t.EntryType == WalletEntryTypes.AdminCredit && t.Status == WalletEntryStatus.Confirmed).SumAsync(t => (decimal?)t.Amount) ?? 0m;

        // Portefeuilles en solde négatif : anomalies à examiner, jamais des soldes normaux.
        var negativeOwners = await individual.Where(t => t.Status == WalletEntryStatus.Confirmed)
            .GroupBy(t => t.OwnerId).Where(g => g.Sum(t => t.Amount) < 0).CountAsync();

        return Ok(new
        {
            data = new
            {
                collectedXaf = collectedPayments + collectedTutoring,
                collectedPaymentsXaf = collectedPayments,
                collectedTutoringXaf = collectedTutoring,
                platformCommissionXaf = platformConfirmed,
                platformCommissionPendingXaf = platformPending,
                // Dû aux utilisateurs : soldes disponibles cumulés, plus les
                // retraits en cours (déjà déduits de ces soldes).
                dueAvailableXaf = dueAvailable,
                duePendingXaf = duePending,
                withdrawalsInProgressXaf = inFlightAmount,
                withdrawalsInProgressCount = inFlightCount,
                dueTotalXaf = dueAvailable + inFlightAmount,
                paidOutXaf = paidOut,
                failedWithdrawalsOpen = failedOpen,
                rechargesXaf = recharges,
                adminCreditsXaf = adminCredits,
                negativeWallets = negativeOwners,
                generatedAt = DateTime.UtcNow,
            },
            success = true,
        });
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Users.AsNoTracking().Where(u => u.Role == "teacher" || u.Role == "parent");
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(u => u.Email.ToLower().Contains(term)
                || ((u.FirstName ?? "") + " " + (u.LastName ?? "")).ToLower().Contains(term));
        }

        var total = await query.CountAsync();
        var users = await query.OrderBy(u => u.LastName).ThenBy(u => u.FirstName).ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.Role, u.IsActive })
            .ToListAsync();

        var ids = users.Select(u => u.Id).ToList();
        var balances = await _db.WalletTransactions.AsNoTracking()
            .Where(t => t.OwnerId != null && ids.Contains(t.OwnerId.Value) && t.OwnerType != WalletOwnerTypes.Platform && t.Status == WalletEntryStatus.Confirmed)
            .GroupBy(t => t.OwnerId!.Value)
            .Select(g => new { UserId = g.Key, Amount = g.Sum(t => t.Amount) })
            .ToDictionaryAsync(x => x.UserId, x => x.Amount);

        var items = users.Select(u => new
        {
            id = u.Id,
            name = $"{u.FirstName} {u.LastName}".Trim(),
            email = u.Email,
            role = u.Role,
            isActive = u.IsActive,
            availableXaf = balances.TryGetValue(u.Id, out var b) ? b : 0m,
        });
        return Ok(new { data = new { items, total, page, pageSize }, success = true });
    }

    [HttpGet("users/{userId:int}")]
    public async Task<IActionResult> UserWallet(int userId, [FromQuery] string? source = "all", [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var user = await _db.Users.AsNoTracking().IgnoreQueryFilters().Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.Role, u.IsActive, u.IsDeleted })
            .FirstOrDefaultAsync();
        if (user == null) return NotFound(new { success = false, error = "Utilisateur introuvable." });

        var balance = await _wallet.GetBalanceAsync(userId);
        var (items, total) = await _wallet.GetHistoryAsync(userId, source, page, pageSize);
        var withdrawals = await _db.Withdrawals.AsNoTracking().Where(w => w.UserId == userId)
            .OrderByDescending(w => w.RequestedAt).Take(50).ToListAsync();

        return Ok(new
        {
            data = new
            {
                user = new { id = user.Id, name = $"{user.FirstName} {user.LastName}".Trim(), email = user.Email, role = user.Role, isActive = user.IsActive, isDeleted = user.IsDeleted },
                balance = new { availableXaf = balance.AvailableXaf, pendingXaf = balance.PendingXaf, engagedXaf = balance.EngagedXaf, isAnomaly = balance.IsAnomaly },
                entries = new { items, total, page = Math.Max(1, page), pageSize = Math.Clamp(pageSize, 1, 200) },
                withdrawals = withdrawals.Select(AdminWithdrawalDto),
            },
            success = true,
        });
    }

    public record AdminCreditRequest(int UserId, decimal AmountXaf, string Reason, string IdempotencyKey);

    /// <summary>
    /// Crédit manuel (geste commercial, correction d'erreur). Rôle
    /// administrateur vérifié par la politique du contrôleur ; bénéficiaire
    /// existant, actif, professeur ou parent ; montant entier strictement
    /// positif ; motif obligatoire ; clé d'idempotence fournie par le
    /// formulaire (un double clic ne crédite qu'une fois). L'écriture porte
    /// l'administrateur qui l'a déclenchée.
    /// </summary>
    [HttpPost("credits")]
    public async Task<IActionResult> Credit([FromBody] AdminCreditRequest request)
    {
        var adminId = User.GetUserId();
        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length < 3)
            return BadRequest(new { success = false, error = "Le motif du crédit est obligatoire." });
        if (reason.Length > 250) reason = reason[..250];
        if (request.AmountXaf <= 0 || request.AmountXaf != decimal.Truncate(request.AmountXaf))
            return BadRequest(new { success = false, error = "Le montant doit être un nombre entier de FCFA strictement positif." });
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 64)
            return BadRequest(new { success = false, error = "Clé d'idempotence manquante ou invalide." });

        var beneficiary = await _db.Users.AsNoTracking().Where(u => u.Id == request.UserId)
            .Select(u => new { u.Id, u.Role, u.IsActive, u.IsDeleted }).FirstOrDefaultAsync();
        if (beneficiary == null || beneficiary.IsDeleted)
            return NotFound(new { success = false, error = "Bénéficiaire introuvable." });
        if (!beneficiary.IsActive)
            return BadRequest(new { success = false, error = "Ce compte est suspendu : aucun crédit ne peut lui être attribué." });
        var role = (beneficiary.Role ?? string.Empty).ToLowerInvariant();
        if (role is not ("teacher" or "parent"))
            return BadRequest(new { success = false, error = "Seuls les comptes professeur et parent disposent d'un portefeuille créditable." });

        var key = $"admin_credit:{adminId}:{request.IdempotencyKey.Trim()}";
        var existing = await _wallet.FindByKeyAsync(key);
        if (existing != null && (existing.OwnerId != beneficiary.Id || existing.Amount != request.AmountXaf))
            return Conflict(new { success = false, error = "Cette clé d'idempotence a déjà servi pour un autre crédit." });

        var entry = existing ?? await _wallet.PostAsync(new WalletEntry(beneficiary.Id, WalletEntryTypes.AdminCredit, request.AmountXaf, key,
            $"Crédit administrateur : {reason}", "AdminCredit", null, CreatedByUserId: adminId));

        if (existing == null)
        {
            _logger.LogInformation("Crédit manuel de {Amount} XAF vers l'utilisateur {UserId} par l'administrateur {AdminId} : {Reason}",
                request.AmountXaf, beneficiary.Id, adminId, reason);
            try
            {
                await _ntfy.PublishAsync($"winplus-user-{beneficiary.Id}", "Crédit sur ton portefeuille",
                    $"{request.AmountXaf:0} FCFA ont été crédités sur ton portefeuille WinPlus. Motif : {reason}",
                    userId: beneficiary.Id, type: "Wallet");
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Notification de crédit manuel non envoyée"); }
        }

        return Ok(new
        {
            success = true,
            duplicate = existing != null,
            data = new
            {
                id = entry.Id,
                userId = entry.OwnerId,
                amountXaf = entry.Amount,
                description = entry.Description,
                createdByUserId = entry.CreatedByUserId,
                createdAt = entry.CreatedAt,
                availableXaf = await _wallet.GetAvailableAsync(beneficiary.Id),
            },
        });
    }

    /// <summary>Forme administrateur d'un retrait : numéro masqué, motif d'échec, actions de secours tracées.</summary>
    public static object AdminWithdrawalDto(Withdrawal w) => new
    {
        id = w.Id,
        userId = w.UserId,
        status = w.Status,
        amountXaf = w.AmountXaf,
        @operator = w.Operator,
        phoneMasked = WithdrawalLabels.MaskPhone(w.Phone),
        requestedAt = w.RequestedAt,
        processedAt = w.ProcessedAt,
        failureReason = w.FailureReason,
        transferReference = w.TransferReference,
        isLegacyManual = w.TransferReference == null,
        retryOfWithdrawalId = w.RetryOfWithdrawalId,
        actionByUserId = w.ActionByUserId,
        closedAt = w.ClosedAt,
        closedByUserId = w.ClosedByUserId,
        adminNote = w.AdminNote,
        anomaly = w.Anomaly,
        lastSyncedAt = w.LastSyncedAt,
    };
}
