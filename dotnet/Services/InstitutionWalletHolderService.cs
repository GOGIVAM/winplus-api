using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>Action sur le portefeuille institution refusée, avec un message exploitable.</summary>
public sealed class InstitutionWalletException : Exception
{
    /// <summary>Code HTTP à renvoyer (400 règle, 404 introuvable, 409 état).</summary>
    public int HttpStatus { get; }
    public string Code { get; }

    public InstitutionWalletException(int httpStatus, string code, string message) : base(message)
    {
        HttpStatus = httpStatus;
        Code = code;
    }
}

/// <summary>
/// Suppression d'un compte refusée parce qu'il porte le portefeuille de son
/// institution (suivi 17.14) : un administrateur doit d'abord changer de titulaire.
/// </summary>
public sealed class WalletHolderDeletionBlockedException : InvalidOperationException
{
    public WalletHolderDeletionBlockedException(string message) : base(message) { }
}

/// <summary>Éligibilité au retrait vers Mobile Money, décidée par le type du portefeuille.</summary>
/// <param name="WalletType">teacher | student | parent | institution | user.</param>
public sealed record WithdrawalEligibility(bool CanWithdraw, string WalletType, string? Reason);

/// <summary>
/// Règle de retrait (suivi 17.14, cohérente avec 13.6) : seul le portefeuille
/// d'un tuteur ou d'un professeur peut être retiré vers Mobile Money. Les
/// portefeuilles élève, parent et institution sont des portefeuilles de
/// dépense, alimentés par recharge ou par remboursement.
///
/// Le type du portefeuille ne se déduit pas du seul rôle du compte : un compte
/// titulaire du portefeuille d'une institution, ou dont le registre contient
/// des écritures de portefeuille parent ou institution, est un portefeuille de
/// dépense quel que soit son rôle actuel (un changement de rôle ne rend pas
/// retirable de l'argent rechargé pour être dépensé).
/// </summary>
public static class WalletWithdrawalPolicy
{
    public const string TeacherWallet = "teacher";
    public const string StudentWallet = "student";
    public const string ParentWallet = WalletOwnerTypes.Parent;
    public const string InstitutionWallet = WalletOwnerTypes.Institution;
    public const string OtherWallet = WalletOwnerTypes.User;

    public static async Task<WithdrawalEligibility> CheckAsync(ApplicationDbContext db, int userId)
    {
        var role = (await db.Users.AsNoTracking().IgnoreQueryFilters()
            .Where(u => u.Id == userId).Select(u => u.Role).FirstOrDefaultAsync() ?? string.Empty).Trim().ToLowerInvariant();

        var isHolder = await db.Institutions.AsNoTracking().IgnoreQueryFilters().AnyAsync(i => i.WalletOwnerUserId == userId);
        var spendingTypes = await db.WalletTransactions.AsNoTracking()
            .Where(t => t.OwnerId == userId && (t.OwnerType == WalletOwnerTypes.Institution || t.OwnerType == WalletOwnerTypes.Parent))
            .Select(t => t.OwnerType).Distinct().ToListAsync();

        var type = isHolder || role == "institution" || spendingTypes.Contains(WalletOwnerTypes.Institution) ? InstitutionWallet
            : role == "parent" || spendingTypes.Contains(WalletOwnerTypes.Parent) ? ParentWallet
            : role == "teacher" ? TeacherWallet
            : role == "student" ? StudentWallet
            : OtherWallet;

        return type switch
        {
            TeacherWallet => new WithdrawalEligibility(true, type, null),
            InstitutionWallet => new WithdrawalEligibility(false, type,
                "Le portefeuille d'une institution ne peut pas être retiré vers Mobile Money : il se recharge et sert à payer les séances de ses élèves."),
            ParentWallet => new WithdrawalEligibility(false, type,
                "Le portefeuille d'un parent ne peut pas être retiré vers Mobile Money : il sert à payer les contenus et les séances de tes enfants, et reçoit les remboursements."),
            StudentWallet => new WithdrawalEligibility(false, type,
                "Le portefeuille d'un élève ne peut pas être retiré vers Mobile Money : il sert à payer tes contenus et tes séances, et reçoit tes remboursements."),
            _ => new WithdrawalEligibility(false, type,
                "Seuls les tuteurs et les professeurs peuvent retirer leur solde vers Mobile Money."),
        };
    }
}

public sealed record InstitutionWalletHolderChangeResult(InstitutionWalletHolderChange? Change, int HolderUserId, bool AlreadyDone);

/// <summary>
/// Changement de titulaire du portefeuille institution par un administrateur
/// WinPlus (suivi 17.14).
/// <list type="bullet">
///   <item>le nouveau titulaire est un compte institution actif de la même institution ;</item>
///   <item>le solde disponible est transféré en entier par deux écritures du
///   registre (débit de l'ancien titulaire, crédit du nouveau), dans une seule
///   transaction, sous le verrou des deux portefeuilles ;</item>
///   <item>chaque changement est tracé (<see cref="InstitutionWalletHolderChange"/>) ;</item>
///   <item>idempotent : même identifiant de soumission, ou nouveau titulaire déjà
///   en place, ne produit ni second transfert ni seconde trace ;</item>
///   <item>ce qui reviendrait encore à l'ancien titulaire (recharge en attente,
///   remboursement à venir d'une séance payée par l'institution) est réorienté
///   vers le nouveau.</item>
/// </list>
/// </summary>
public interface IInstitutionWalletHolderService
{
    Task<InstitutionWalletHolderChangeResult> ChangeHolderAsync(int institutionId, int newHolderUserId, int adminUserId, string? reason, string? clientRequestId);
}

public class InstitutionWalletHolderService : IInstitutionWalletHolderService
{
    public const string SourceType = "InstitutionWalletHolderChange";

    private readonly ApplicationDbContext _db;
    private readonly IWalletService _wallet;
    private readonly INtfyService _ntfy;
    private readonly ILogger<InstitutionWalletHolderService> _logger;

    public InstitutionWalletHolderService(ApplicationDbContext db, IWalletService wallet, INtfyService ntfy, ILogger<InstitutionWalletHolderService> logger)
    {
        _db = db;
        _wallet = wallet;
        _ntfy = ntfy;
        _logger = logger;
    }

    public static string TransferOutKey(int changeId) => $"institution_holder_change:{changeId}:out";
    public static string TransferInKey(int changeId) => $"institution_holder_change:{changeId}:in";

    public async Task<InstitutionWalletHolderChangeResult> ChangeHolderAsync(int institutionId, int newHolderUserId, int adminUserId, string? reason, string? clientRequestId)
    {
        var motive = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (motive == null)
            throw new InstitutionWalletException(400, "reason_required", "Le motif du changement de titulaire est obligatoire.");
        if (motive.Length > 300) motive = motive[..300];
        var requestId = string.IsNullOrWhiteSpace(clientRequestId) ? null : clientRequestId.Trim();
        if (requestId is { Length: > 64 }) requestId = requestId[..64];

        // Rejeu d'une même soumission (double clic, réseau) : la trace existante.
        if (requestId != null)
        {
            var replay = await _db.InstitutionWalletHolderChanges.AsNoTracking()
                .FirstOrDefaultAsync(c => c.InstitutionId == institutionId && c.ClientRequestId == requestId);
            if (replay != null) return new InstitutionWalletHolderChangeResult(replay, replay.NewHolderUserId, true);
        }

        var institution = await _db.Institutions.AsNoTracking().FirstOrDefaultAsync(i => i.Id == institutionId && !i.IsDeleted)
            ?? throw new InstitutionWalletException(404, "not_found", "Institution introuvable.");

        var candidate = await _db.Users.AsNoTracking()
            .Where(u => u.Id == newHolderUserId)
            .Select(u => new { u.Id, u.Role, u.InstitutionId, u.IsActive, u.IsDeleted })
            .FirstOrDefaultAsync();
        if (candidate == null || candidate.InstitutionId != institutionId || !candidate.IsActive || candidate.IsDeleted
            || !string.Equals(candidate.Role, "institution", StringComparison.OrdinalIgnoreCase))
            throw new InstitutionWalletException(400, "invalid_holder",
                "Le nouveau titulaire doit être un compte institution actif de la même institution.");

        var oldHolder = institution.WalletOwnerUserId;
        if (oldHolder == newHolderUserId)
            return new InstitutionWalletHolderChangeResult(null, newHolderUserId, true);

        InstitutionWalletHolderChange? change;
        if (oldHolder is not int previous)
        {
            // Portefeuille jamais ouvert : aucun solde à transférer, seul le titulaire est fixé.
            await EnsureNoOwnBalanceAsync(newHolderUserId);
            change = await RecordAndSwitchAsync(institutionId, null, newHolderUserId, 0m, adminUserId, motive, requestId);
        }
        else
        {
            var first = Math.Min(previous, newHolderUserId);
            var second = Math.Max(previous, newHolderUserId);
            change = await _wallet.RunLockedAsync(first, () => _wallet.RunLockedAsync(second, async () =>
            {
                // Relu sous verrou : la même demande a pu aboutir entre-temps
                // (rejeu concurrent), ou un autre administrateur a pu agir.
                if (requestId != null && await _db.InstitutionWalletHolderChanges.AsNoTracking()
                        .AnyAsync(c => c.InstitutionId == institutionId && c.ClientRequestId == requestId))
                    return (InstitutionWalletHolderChange?)null;
                var current = await _db.Institutions.AsNoTracking().Where(i => i.Id == institutionId)
                    .Select(i => i.WalletOwnerUserId).FirstOrDefaultAsync();
                if (current == newHolderUserId) return null;
                if (current != previous)
                    throw new InstitutionWalletException(409, "holder_changed",
                        "Le titulaire de ce portefeuille vient d'être modifié par ailleurs. Recharge la page puis recommence.");

                await EnsureNoOwnBalanceAsync(newHolderUserId);
                var available = await _wallet.GetAvailableAsync(previous);
                if (available < 0)
                    throw new InstitutionWalletException(409, "negative_balance",
                        $"Le portefeuille de l'institution est en solde négatif ({available:0} FCFA) : anomalie à examiner avant tout changement de titulaire.");

                var recorded = await RecordAndSwitchAsync(institutionId, previous, newHolderUserId, available, adminUserId, motive, requestId);

                if (available > 0)
                {
                    var label = $"Changement de titulaire du portefeuille de l'institution « {institution.Name} »";
                    await _wallet.DebitAsync(new WalletEntry(previous, WalletEntryTypes.InstitutionHolderTransferOut, available,
                        TransferOutKey(recorded.Id), $"{label} : solde transféré", SourceType, recorded.Id,
                        CreatedByUserId: adminUserId, OwnerType: WalletOwnerTypes.Institution));
                    await _wallet.PostAsync(new WalletEntry(newHolderUserId, WalletEntryTypes.InstitutionHolderTransferIn, available,
                        TransferInKey(recorded.Id), $"{label} : solde reçu", SourceType, recorded.Id,
                        CreatedByUserId: adminUserId, OwnerType: WalletOwnerTypes.Institution));
                }

                // Ce qui reviendrait encore à l'ancien titulaire suit le portefeuille.
                foreach (var topUp in await _db.WalletTopUps.Where(t => t.UserId == previous && t.Status == "pending").ToListAsync())
                    topUp.UserId = newHolderUserId;
                foreach (var booking in await _db.TutorBookings
                             .Where(b => b.PayerInstitutionId == institutionId && b.PayerUserId == previous && b.RefundedAt == null)
                             .ToListAsync())
                    booking.PayerUserId = newHolderUserId;
                await _db.SaveChangesAsync();
                return (InstitutionWalletHolderChange?)recorded;
            }));
        }

        if (change == null)
        {
            // Rejeu concurrent : renvoie la trace qui a abouti, sans rien refaire.
            var done = requestId == null ? null : await _db.InstitutionWalletHolderChanges.AsNoTracking()
                .FirstOrDefaultAsync(c => c.InstitutionId == institutionId && c.ClientRequestId == requestId);
            return new InstitutionWalletHolderChangeResult(done, newHolderUserId, true);
        }

        _logger.LogWarning("Portefeuille de l'institution {InstitutionId} : titulaire {Old} → {New} par l'administrateur {Admin}, {Amount} XAF transférés ({Reason})",
            institutionId, oldHolder, newHolderUserId, adminUserId, change.AmountXaf, motive);
        await NotifyAsync(institution.Name, oldHolder, newHolderUserId, change.AmountXaf);
        return new InstitutionWalletHolderChangeResult(change, newHolderUserId, false);
    }

    /// <summary>
    /// Le nouveau titulaire ne doit pas avoir de solde propre : ses écritures
    /// et celles de l'institution se mélangeraient dans un même portefeuille.
    /// </summary>
    private async Task EnsureNoOwnBalanceAsync(int newHolderUserId)
    {
        var own = await _wallet.GetAvailableAsync(newHolderUserId);
        if (own != 0)
            throw new InstitutionWalletException(409, "holder_has_balance",
                $"Ce compte a déjà un solde propre de {own:0} FCFA : il ne peut pas recevoir le portefeuille de l'institution sans mélanger deux soldes.");
    }

    private async Task<InstitutionWalletHolderChange> RecordAndSwitchAsync(int institutionId, int? oldHolder, int newHolder, decimal amount,
        int adminUserId, string reason, string? requestId)
    {
        var change = new InstitutionWalletHolderChange
        {
            InstitutionId = institutionId,
            OldHolderUserId = oldHolder,
            NewHolderUserId = newHolder,
            AmountXaf = amount,
            ChangedByUserId = adminUserId,
            Reason = reason,
            ClientRequestId = requestId,
            CreatedAt = DateTime.UtcNow,
        };
        _db.InstitutionWalletHolderChanges.Add(change);
        var institution = await _db.Institutions.FirstAsync(i => i.Id == institutionId);
        institution.WalletOwnerUserId = newHolder;
        institution.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return change;
    }

    private async Task NotifyAsync(string institutionName, int? oldHolder, int newHolder, decimal amount)
    {
        var message = $"Le portefeuille de l'institution « {institutionName} » est désormais porté par un autre compte titulaire"
                      + (amount > 0 ? $" ({amount:0} FCFA transférés)." : ".");
        foreach (var id in new[] { oldHolder, (int?)newHolder }.Where(i => i != null).Select(i => i!.Value).Distinct())
        {
            try
            {
                await _ntfy.PublishAsync($"winplus-user-{id}", "Titulaire du portefeuille modifié", message,
                    userId: id, type: "Wallet", relatedEntityType: "Institution");
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Notification du changement de titulaire non envoyée au compte {UserId}", id); }
        }
    }

    // ── Suppression du compte titulaire (17.14, décision 13.4) ──────────

    /// <summary>
    /// Refuse la suppression (douce ou définitive) d'un compte qui porte le
    /// portefeuille d'une institution : l'argent de l'institution resterait
    /// sans titulaire actif.
    /// </summary>
    public static async Task EnsureNotWalletHolderAsync(ApplicationDbContext db, int userId)
    {
        var name = await db.Institutions.AsNoTracking()
            .Where(i => i.WalletOwnerUserId == userId && !i.IsDeleted)
            .Select(i => i.Name).FirstOrDefaultAsync();
        if (name != null)
            throw new WalletHolderDeletionBlockedException(
                $"Ce compte porte le portefeuille de l'institution « {name} » : un administrateur WinPlus doit d'abord en changer le titulaire avant de pouvoir le supprimer.");
    }
}
