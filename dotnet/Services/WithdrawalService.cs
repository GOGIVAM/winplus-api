using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>Demande de retrait refusée, avec un message exploitable par l'utilisateur.</summary>
public sealed class WithdrawalRejectedException : Exception
{
    /// <summary>Code HTTP à renvoyer (400 règle, 402 solde, 403 compte, 404 introuvable, 409 état).</summary>
    public int HttpStatus { get; }
    public string Code { get; }
    public decimal? AvailableXaf { get; init; }

    public WithdrawalRejectedException(int httpStatus, string code, string message) : base(message)
    {
        HttpStatus = httpStatus;
        Code = code;
    }
}

public sealed record WithdrawalRequestResult(Withdrawal Withdrawal, bool Duplicate);

/// <summary>
/// Retrait Mobile Money automatisé (lot 2, Module 2 ; décisions §6.1, §9,
/// §15 et §18 du suivi).
///
/// <list type="number">
///   <item>Contrôles serveur : montant entier ≥ 500 XAF, aucun plafond, aucun
///   frais, opérateur et numéro valides, compte actif.</item>
///   <item>Sous le verrou du portefeuille : solde lu dans le journal, demande
///   créée et écriture <c>WithdrawalRequested</c> (débit) posée dans la même
///   transaction. Les fonds sont réservés avant tout appel au fournisseur.</item>
///   <item>Transfert NotchPay créé avec la référence <c>WDR-{id}</c>.</item>
///   <item>Issue suivie par notification serveur (chemin rapide) et par
///   consultation périodique (filet de sécurité), toujours en relisant le
///   statut réel chez NotchPay.</item>
///   <item>Succès : écriture <c>WithdrawalProcessed</c> (montant nul, marque le
///   virement effectif). Échec ou annulation : contre-passation qui restitue
///   exactement le montant réservé.</item>
/// </list>
/// Toute transition d'état d'un retrait se fait sous le verrou du portefeuille
/// de son propriétaire : une notification reçue deux fois, ou en même temps
/// que la consultation périodique, ne produit qu'une seule issue.
/// </summary>
public interface IWithdrawalService
{
    Task<WithdrawalRequestResult> RequestAsync(int userId, string? operatorCode, string? phone, decimal amount, string? clientRequestId);

    /// <summary>Relit le statut réel chez NotchPay et applique l'issue. Sans effet sur une demande déjà terminée (hors détection d'anomalie).</summary>
    Task<Withdrawal?> SyncAsync(int withdrawalId);

    Task SyncByReferenceAsync(string reference);

    /// <summary>Consultation périodique de tous les retraits automatisés en cours.</summary>
    Task<int> SyncInFlightAsync(CancellationToken ct = default);

    /// <summary>Module 4 : rejoue le transfert d'une demande en échec (nouvelle demande, nouvelle réservation de fonds).</summary>
    Task<Withdrawal> RetryAsync(int withdrawalId, int adminUserId, string reason);

    /// <summary>Module 4 : clôt définitivement une demande en échec, en garantissant la restitution des fonds.</summary>
    Task<Withdrawal> CloseAsync(int withdrawalId, int adminUserId, string reason);

    /// <summary>
    /// Demande manuelle antérieure à l'automatisation (sans référence de
    /// transfert) : constat du virement manuel déjà fait, ou échec avec
    /// restitution. Refusé pour toute demande automatisée.
    /// </summary>
    Task<Withdrawal> SettleLegacyManualAsync(int withdrawalId, int adminUserId, bool success, string? note);
}

public class WithdrawalService : IWithdrawalService
{
    /// <summary>Minimum de retrait (décision §9 du suivi). Aucun plafond maximum (§18).</summary>
    public const decimal MinimumXaf = 500m;

    /// <summary>Délai au-delà duquel un transfert inconnu de NotchPay est considéré comme jamais créé.</summary>
    public static readonly TimeSpan NeverCreatedAfter = TimeSpan.FromMinutes(15);

    /// <summary>Fenêtre de détection d'une double soumission sans identifiant client (anciens clients).</summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(60);

    private static readonly string[] Operators = { "mtn", "orange" };

    private readonly ApplicationDbContext _db;
    private readonly IWalletService _wallet;
    private readonly INotchPayService _notchPay;
    private readonly INtfyService _ntfy;
    private readonly ILogger<WithdrawalService> _logger;

    public WithdrawalService(ApplicationDbContext db, IWalletService wallet, INotchPayService notchPay, INtfyService ntfy, ILogger<WithdrawalService> logger)
    {
        _db = db;
        _wallet = wallet;
        _notchPay = notchPay;
        _ntfy = ntfy;
        _logger = logger;
    }

    public static string ReferenceFor(int withdrawalId) => $"WDR-{withdrawalId}";

    /// <summary>9 chiffres locaux → préfixe 237 ; 237 + 9 chiffres accepté ; tout le reste refusé.</summary>
    public static string? NormalizePhone(string? phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00237")) digits = digits[2..];
        if (digits.Length == 9 && digits[0] == '6') return "237" + digits;
        if (digits.Length == 12 && digits.StartsWith("2376")) return digits;
        return null;
    }

    // ── Demande ─────────────────────────────────────────────────────────

    public async Task<WithdrawalRequestResult> RequestAsync(int userId, string? operatorCode, string? phone, decimal amount, string? clientRequestId)
    {
        var result = await CreateReservedAsync(userId, operatorCode, phone, amount, clientRequestId, retryOf: null, actionBy: null, note: null);
        if (!result.Duplicate)
            await StartTransferAsync(result.Withdrawal.Id);
        var fresh = await _db.Withdrawals.AsNoTracking().FirstAsync(w => w.Id == result.Withdrawal.Id);
        return result with { Withdrawal = fresh };
    }

    private async Task<WithdrawalRequestResult> CreateReservedAsync(
        int userId, string? operatorCode, string? phone, decimal amount, string? clientRequestId,
        int? retryOf, int? actionBy, string? note)
    {
        var op = (operatorCode ?? string.Empty).Trim().ToLowerInvariant();
        if (!Operators.Contains(op))
            throw new WithdrawalRejectedException(400, "invalid_operator", "Opérateur invalide : choisis MTN MoMo ou Orange Money.");

        var normalizedPhone = NormalizePhone(phone)
            ?? throw new WithdrawalRejectedException(400, "invalid_phone", "Numéro Mobile Money invalide : 9 chiffres commençant par 6 (ex. 6XXXXXXXX).");

        if (amount != decimal.Truncate(amount))
            throw new WithdrawalRejectedException(400, "invalid_amount", "Le montant doit être un nombre entier de FCFA.");
        if (amount < MinimumXaf)
            throw new WithdrawalRejectedException(400, "below_minimum", $"Le montant minimum de retrait est de {MinimumXaf:0} FCFA.");

        var user = await _db.Users.AsNoTracking().IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.IsDeleted })
            .FirstOrDefaultAsync();
        if (user == null || !user.IsActive || user.IsDeleted)
            throw new WithdrawalRejectedException(403, "account_inactive", "Ce compte ne peut pas effectuer de retrait (compte suspendu ou supprimé).");

        var requestId = string.IsNullOrWhiteSpace(clientRequestId) ? null : clientRequestId.Trim();
        if (requestId != null && requestId.Length > 64) requestId = requestId[..64];

        return await _wallet.RunLockedAsync(userId, async () =>
        {
            // Double soumission : même identifiant client, ou (anciens clients
            // sans identifiant) même demande en cours dans la minute.
            var duplicate = await FindDuplicateAsync(userId, requestId, op, normalizedPhone, amount);
            if (duplicate != null) return new WithdrawalRequestResult(duplicate, true);

            var available = await _wallet.GetAvailableAsync(userId);
            if (available < amount)
                throw new WithdrawalRejectedException(402, "insufficient_balance",
                    $"Solde insuffisant : {Math.Max(0, available):0} FCFA disponibles, {amount:0} FCFA demandés.")
                { AvailableXaf = Math.Max(0, available) };

            var withdrawal = new Withdrawal
            {
                UserId = userId,
                Operator = op,
                Phone = normalizedPhone,
                AmountXaf = amount,
                Status = WithdrawalStatus.Pending,
                ClientRequestId = requestId,
                RetryOfWithdrawalId = retryOf,
                ActionByUserId = actionBy,
                AdminNote = note,
                RequestedAt = DateTime.UtcNow,
            };
            _db.Withdrawals.Add(withdrawal);
            await _db.SaveChangesAsync();

            withdrawal.TransferReference = ReferenceFor(withdrawal.Id);
            await _db.SaveChangesAsync();

            await _wallet.DebitAsync(new WalletEntry(userId, WalletEntryTypes.WithdrawalRequested, amount,
                WalletService.WithdrawalRequestedKey(withdrawal.Id), WithdrawalLabels.Requested(withdrawal),
                "Withdrawal", withdrawal.Id, CreatedByUserId: actionBy));

            return new WithdrawalRequestResult(withdrawal, false);
        });
    }

    private async Task<Withdrawal?> FindDuplicateAsync(int userId, string? requestId, string op, string phone, decimal amount)
    {
        if (requestId != null)
            return await _db.Withdrawals.AsNoTracking().FirstOrDefaultAsync(w => w.UserId == userId && w.ClientRequestId == requestId);

        var since = DateTime.UtcNow - DuplicateWindow;
        return await _db.Withdrawals.AsNoTracking()
            .Where(w => w.UserId == userId && w.ClientRequestId == null && w.RetryOfWithdrawalId == null
                     && w.Operator == op && w.Phone == phone && w.AmountXaf == amount
                     && w.RequestedAt >= since
                     && (w.Status == WithdrawalStatus.Pending || w.Status == WithdrawalStatus.Processing))
            .OrderByDescending(w => w.RequestedAt)
            .FirstOrDefaultAsync();
    }

    /// <summary>Appel au fournisseur, hors verrou et hors transaction : les fonds sont déjà réservés.</summary>
    private async Task StartTransferAsync(int withdrawalId)
    {
        var w = await _db.Withdrawals.AsNoTracking().FirstAsync(x => x.Id == withdrawalId);
        var beneficiary = await _db.Users.AsNoTracking().IgnoreQueryFilters()
            .Where(u => u.Id == w.UserId)
            .Select(u => new { u.FirstName, u.LastName, u.Email })
            .FirstOrDefaultAsync();
        var name = $"{beneficiary?.FirstName} {beneficiary?.LastName}".Trim();

        try
        {
            var transfer = await _notchPay.CreateTransferAsync(new NotchPayTransferRequest
            {
                AmountXaf = w.AmountXaf,
                Channel = w.Operator == "orange" ? "cm.orange" : "cm.mtn",
                BeneficiaryName = string.IsNullOrWhiteSpace(name) ? (beneficiary?.Email ?? $"Utilisateur {w.UserId}") : name,
                BeneficiaryEmail = beneficiary?.Email,
                BeneficiaryPhone = w.Phone,
                Reference = w.TransferReference!,
                Description = $"Retrait WinPlus #{w.Id}",
            });
            await ApplyProviderStateAsync(withdrawalId, transfer);
        }
        catch (NotchPayTransferRejectedException ex)
        {
            // Refus explicite avant création : rien n'a été viré, les fonds
            // réservés sont restitués immédiatement.
            _logger.LogWarning("Transfert du retrait {Id} refusé par NotchPay : {Message}", withdrawalId, ex.Message);
            await FailAsync(withdrawalId, WithdrawalStatus.Failed, $"Refusé par NotchPay : {ex.Message}");
        }
        catch (Exception ex)
        {
            // Réponse perdue (délai, coupure, 5xx) : le transfert a peut-être
            // été créé. Aucun second envoi ; la réconciliation par référence
            // tranchera (WithdrawalTransferSyncService).
            _logger.LogError(ex, "Issue inconnue de la création du transfert du retrait {Id} : réconciliation par référence à venir", withdrawalId);
        }
    }

    // ── Suivi de l'issue ────────────────────────────────────────────────

    public async Task<Withdrawal?> SyncAsync(int withdrawalId)
    {
        var w = await _db.Withdrawals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == withdrawalId);
        if (w == null || w.TransferReference == null) return w;

        NotchPayTransfer? transfer;
        try
        {
            transfer = await _notchPay.GetTransferAsync(w.TransferReference);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Statut du transfert {Ref} indisponible", w.TransferReference);
            return w;
        }

        if (transfer == null)
        {
            // Seule une demande jamais acceptée par NotchPay (« pending », sans
            // identifiant de transfert) peut être conclue « jamais créée » ; une
            // demande « processing » a été acceptée : elle attend son issue.
            if (w.Status == WithdrawalStatus.Pending && w.ProviderTransferId == null
                && DateTime.UtcNow - w.RequestedAt > NeverCreatedAfter)
            {
                await FailAsync(w.Id, WithdrawalStatus.Failed, "Transfert jamais créé chez NotchPay.");
            }
            else
            {
                await TouchAsync(w.Id);
            }
        }
        else
        {
            await ApplyProviderStateAsync(w.Id, transfer);
        }

        return await _db.Withdrawals.AsNoTracking().FirstAsync(x => x.Id == withdrawalId);
    }

    public async Task SyncByReferenceAsync(string reference)
    {
        var id = await _db.Withdrawals.AsNoTracking()
            .Where(w => w.TransferReference == reference || w.ProviderTransferId == reference)
            .Select(w => (int?)w.Id).FirstOrDefaultAsync();
        if (id == null)
        {
            _logger.LogWarning("Notification de transfert NotchPay pour une référence inconnue : {Ref}", reference);
            return;
        }
        await SyncAsync(id.Value);
    }

    public async Task<int> SyncInFlightAsync(CancellationToken ct = default)
    {
        var ids = await _db.Withdrawals.AsNoTracking()
            .Where(w => w.TransferReference != null
                     && (w.Status == WithdrawalStatus.Pending || w.Status == WithdrawalStatus.Processing))
            .OrderBy(w => w.LastSyncedAt ?? w.RequestedAt)
            .Select(w => w.Id).Take(200).ToListAsync(ct);
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            await SyncAsync(id);
        }
        return ids.Count;
    }

    public static string? MapProviderStatus(string? status) => (status ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "complete" or "completed" or "success" or "successful" => WithdrawalStatus.Completed,
        "failed" or "failure" or "rejected" or "expired" => WithdrawalStatus.Failed,
        "canceled" or "cancelled" => WithdrawalStatus.Cancelled,
        "pending" or "processing" or "created" or "initiated" => WithdrawalStatus.Processing,
        _ => null,
    };

    private async Task ApplyProviderStateAsync(int withdrawalId, NotchPayTransfer transfer)
    {
        var target = MapProviderStatus(transfer.Status);
        var userId = await _db.Withdrawals.AsNoTracking().Where(w => w.Id == withdrawalId).Select(w => w.UserId).FirstAsync();

        if (target is WithdrawalStatus.Failed or WithdrawalStatus.Cancelled)
        {
            var reason = transfer.FailureReason ?? transfer.FailureMessage
                ?? (target == WithdrawalStatus.Cancelled ? "Transfert annulé." : "Transfert échoué chez NotchPay.");
            await FailAsync(withdrawalId, target, reason, transfer.Id);
            return;
        }

        var notify = false;
        Withdrawal? done = null;
        await _wallet.RunLockedAsync(userId, async () =>
        {
            var w = await _db.Withdrawals.FirstAsync(x => x.Id == withdrawalId);
            w.LastSyncedAt = DateTime.UtcNow;
            if (transfer.Id != null) w.ProviderTransferId ??= transfer.Id;

            if (target == WithdrawalStatus.Completed)
            {
                if (WithdrawalStatus.IsInFlight(w.Status))
                {
                    w.Status = WithdrawalStatus.Completed;
                    w.ProcessedAt = transfer.CompletedAt ?? DateTime.UtcNow;
                    w.FailureReason = null;
                    await _db.SaveChangesAsync();
                    await _wallet.PostAsync(new WalletEntry(w.UserId, WalletEntryTypes.WithdrawalProcessed, 0m,
                        WalletService.WithdrawalProcessedKey(w.Id), WithdrawalLabels.Processed(w), "Withdrawal", w.Id,
                        OccurredAt: w.ProcessedAt));
                    notify = true;
                    done = w;
                }
                else if (w.Status is WithdrawalStatus.Failed or WithdrawalStatus.Cancelled && w.Anomaly == null)
                {
                    // Fonds déjà restitués, mais le virement a finalement abouti :
                    // signalé, jamais corrigé en silence.
                    w.Anomaly = $"Transfert abouti chez NotchPay après restitution des fonds ({w.AmountXaf:0} XAF versés deux fois potentiellement).";
                    await _db.SaveChangesAsync();
                    _logger.LogError("Anomalie retrait {Id} : {Anomaly}", w.Id, w.Anomaly);
                    await SafeAdminAsync("Anomalie sur un retrait", $"Retrait #{w.Id} : {w.Anomaly} Vérification manuelle requise.", w.Id);
                }
                else
                {
                    await _db.SaveChangesAsync();
                }
            }
            else
            {
                if (w.Status == WithdrawalStatus.Pending && target == WithdrawalStatus.Processing)
                    w.Status = WithdrawalStatus.Processing;
                await _db.SaveChangesAsync();
            }
            return true;
        });

        if (notify && done != null)
            await SafeUserAsync(done.UserId, "Retrait effectué",
                $"Ton retrait de {done.AmountXaf:0} FCFA a été envoyé vers {WithdrawalLabels.MaskPhone(done.Phone)}.", done.Id);
    }

    /// <summary>Échec ou annulation : statut terminal et contre-passation de la réservation, une seule fois.</summary>
    private async Task FailAsync(int withdrawalId, string status, string reason, string? providerId = null)
    {
        var userId = await _db.Withdrawals.AsNoTracking().Where(w => w.Id == withdrawalId).Select(w => w.UserId).FirstAsync();
        Withdrawal? failed = null;

        await _wallet.RunLockedAsync(userId, async () =>
        {
            var w = await _db.Withdrawals.FirstAsync(x => x.Id == withdrawalId);
            w.LastSyncedAt = DateTime.UtcNow;
            if (providerId != null) w.ProviderTransferId ??= providerId;
            if (!WithdrawalStatus.IsInFlight(w.Status))
            {
                await _db.SaveChangesAsync();
                return false;
            }

            w.Status = status;
            w.FailureReason = reason.Length > 300 ? reason[..300] : reason;
            w.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await RestoreFundsAsync(w, $"Retrait #{w.Id} {(status == WithdrawalStatus.Cancelled ? "annulé" : "échoué")} : fonds restitués");
            failed = w;
            return true;
        });

        if (failed != null)
        {
            await SafeUserAsync(failed.UserId, "Retrait échoué",
                $"Ton retrait de {failed.AmountXaf:0} FCFA n'a pas abouti ({failed.FailureReason}). Le montant a été remis sur ton solde.", failed.Id);
            await SafeAdminAsync("Retrait échoué",
                $"Retrait #{failed.Id} de {failed.AmountXaf:0} FCFA (utilisateur #{failed.UserId}) : {failed.FailureReason}. Fonds restitués automatiquement.", failed.Id);
        }
    }

    private async Task RestoreFundsAsync(Withdrawal w, string label)
    {
        var requested = await _wallet.FindByKeyAsync(WalletService.WithdrawalRequestedKey(w.Id));
        if (requested != null)
            await _wallet.ReverseAsync(requested.Id, label, w.ClosedByUserId ?? w.ActionByUserId);
    }

    private async Task TouchAsync(int withdrawalId)
    {
        var w = await _db.Withdrawals.FirstAsync(x => x.Id == withdrawalId);
        w.LastSyncedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    // ── Actions de secours (Module 4) ───────────────────────────────────

    public async Task<Withdrawal> RetryAsync(int withdrawalId, int adminUserId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new WithdrawalRejectedException(400, "reason_required", "Le motif est obligatoire.");

        // Statut revérifié chez le fournisseur au moment d'agir.
        var w = await SyncAsync(withdrawalId)
            ?? throw new WithdrawalRejectedException(404, "not_found", "Retrait introuvable.");
        EnsureRescuable(w);

        // Rejeu déjà fait (double clic) : renvoyé tel quel, aucun second transfert.
        var existing = await _db.Withdrawals.AsNoTracking().FirstOrDefaultAsync(x => x.RetryOfWithdrawalId == withdrawalId);
        if (existing != null) return existing;

        var result = await CreateReservedAsync(w.UserId, w.Operator, w.Phone, w.AmountXaf, $"retry-of-{w.Id}",
            retryOf: w.Id, actionBy: adminUserId, note: Trim300(reason));
        if (!result.Duplicate)
            await StartTransferAsync(result.Withdrawal.Id);

        _logger.LogInformation("Retrait {Id} rejoué par l'administrateur {AdminId} (nouvelle demande {NewId}) : {Reason}",
            withdrawalId, adminUserId, result.Withdrawal.Id, reason);
        return await _db.Withdrawals.AsNoTracking().FirstAsync(x => x.Id == result.Withdrawal.Id);
    }

    public async Task<Withdrawal> CloseAsync(int withdrawalId, int adminUserId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new WithdrawalRejectedException(400, "reason_required", "Le motif est obligatoire.");

        var current = await SyncAsync(withdrawalId)
            ?? throw new WithdrawalRejectedException(404, "not_found", "Retrait introuvable.");
        EnsureRescuable(current);

        await _wallet.RunLockedAsync(current.UserId, async () =>
        {
            var w = await _db.Withdrawals.FirstAsync(x => x.Id == withdrawalId);
            EnsureRescuable(w);
            w.ClosedAt = DateTime.UtcNow;
            w.ClosedByUserId = adminUserId;
            w.AdminNote = Trim300(reason);
            await _db.SaveChangesAsync();
            // Restitution garantie (déjà faite à l'échec dans le cas normal :
            // la contre-passation est idempotente).
            await RestoreFundsAsync(w, $"Retrait #{w.Id} clôturé par l'administration : fonds restitués");
            return true;
        });

        _logger.LogInformation("Retrait {Id} clôturé par l'administrateur {AdminId} : {Reason}", withdrawalId, adminUserId, reason);
        return await _db.Withdrawals.AsNoTracking().FirstAsync(x => x.Id == withdrawalId);
    }

    private static void EnsureRescuable(Withdrawal w)
    {
        if (w.Status is not (WithdrawalStatus.Failed or WithdrawalStatus.Cancelled))
            throw new WithdrawalRejectedException(409, "not_failed", "Seule une demande en échec peut faire l'objet d'une action de secours.");
        if (w.ClosedAt != null)
            throw new WithdrawalRejectedException(409, "closed", "Cette demande est déjà clôturée.");
        if (w.Anomaly != null)
            throw new WithdrawalRejectedException(409, "anomaly", "Anomalie en cours sur cette demande : vérification manuelle requise avant toute action.");
    }

    public async Task<Withdrawal> SettleLegacyManualAsync(int withdrawalId, int adminUserId, bool success, string? note)
    {
        var current = await _db.Withdrawals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == withdrawalId)
            ?? throw new WithdrawalRejectedException(404, "not_found", "Retrait introuvable.");
        if (current.TransferReference != null)
            throw new WithdrawalRejectedException(409, "automated",
                "Ce retrait est traité automatiquement par NotchPay : son statut suit le transfert réel et ne se valide pas à la main.");
        if (current.Status != WithdrawalStatus.Pending)
            throw new WithdrawalRejectedException(409, "already_processed", "Ce retrait a déjà été traité.");

        await _wallet.RunLockedAsync(current.UserId, async () =>
        {
            var w = await _db.Withdrawals.FirstAsync(x => x.Id == withdrawalId);
            if (w.Status != WithdrawalStatus.Pending) return false;
            w.Status = success ? WithdrawalStatus.Completed : WithdrawalStatus.Failed;
            w.AdminNote = Trim300(note);
            w.ActionByUserId = adminUserId;
            w.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            if (success)
                await _wallet.PostAsync(new WalletEntry(w.UserId, WalletEntryTypes.WithdrawalProcessed, 0m,
                    WalletService.WithdrawalProcessedKey(w.Id), WithdrawalLabels.Processed(w), "Withdrawal", w.Id,
                    CreatedByUserId: adminUserId));
            else
                await RestoreFundsAsync(w, $"Retrait manuel #{w.Id} non effectué : fonds restitués");
            return true;
        });

        var done = await _db.Withdrawals.AsNoTracking().FirstAsync(x => x.Id == withdrawalId);
        await SafeUserAsync(done.UserId, success ? "Retrait effectué" : "Retrait échoué",
            success
                ? $"Ton retrait de {done.AmountXaf:0} FCFA a été envoyé vers {WithdrawalLabels.MaskPhone(done.Phone)}."
                : $"Ton retrait de {done.AmountXaf:0} FCFA n'a pas abouti. Le montant a été remis sur ton solde.", done.Id);
        return done;
    }

    // ── Notifications ───────────────────────────────────────────────────

    private async Task SafeUserAsync(int userId, string title, string message, int withdrawalId)
    {
        try
        {
            await _ntfy.PublishAsync($"winplus-user-{userId}", title, message, priority: "high",
                userId: userId, type: "Withdrawal", relatedEntityType: "Withdrawal", relatedEntityId: withdrawalId);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Notification du retrait {Id} non envoyée", withdrawalId); }
    }

    private async Task SafeAdminAsync(string title, string message, int withdrawalId)
    {
        try
        {
            await _ntfy.PublishAdminAsync(title, message, tags: new[] { "moneybag" },
                type: "Withdrawal", relatedEntityType: "Withdrawal", relatedEntityId: withdrawalId);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Alerte administrateur du retrait {Id} non envoyée", withdrawalId); }
    }

    private static string? Trim300(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > 300 ? value.Trim()[..300] : value.Trim();
}

/// <summary>Consultation périodique des retraits en cours (filet de sécurité des notifications NotchPay).</summary>
public sealed class WithdrawalTransferSyncService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WithdrawalTransferSyncService> _logger;

    public WithdrawalTransferSyncService(IServiceScopeFactory scopeFactory, ILogger<WithdrawalTransferSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IWithdrawalService>();
                await service.SyncInFlightAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Erreur lors du suivi des transferts de retrait"); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
