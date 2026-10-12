using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Services;

namespace Backend.Controllers;

/// <param name="NewHolderUserId">Compte institution actif de la même institution.</param>
/// <param name="Reason">Motif, obligatoire (tracé).</param>
/// <param name="ClientRequestId">Identifiant de la soumission : un rejeu ne transfère rien une seconde fois.</param>
public record ChangeInstitutionWalletHolderRequest(int NewHolderUserId, string? Reason, string? ClientRequestId = null);

/// <summary>
/// Titulaire du portefeuille institution, réservé aux administrateurs WinPlus
/// (suivi 17.14).
///
/// GET  /api/admin/institutions/wallets                  institutions et titulaire actuel (recherche par nom)
/// GET  /api/admin/institutions/{id}/wallet              titulaire, solde, comptes éligibles, historique des changements
/// POST /api/admin/institutions/{id}/wallet/holder       changement de titulaire avec transfert intégral du solde
/// </summary>
[ApiController]
[Route("api/admin/institutions")]
[Authorize(Policy = "AdminOnly")]
public class AdminInstitutionWalletController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IWalletService _wallet;
    private readonly IInstitutionWalletHolderService _holders;
    private readonly ILogger<AdminInstitutionWalletController> _logger;

    public AdminInstitutionWalletController(ApplicationDbContext db, IWalletService wallet, IInstitutionWalletHolderService holders,
        ILogger<AdminInstitutionWalletController> logger)
    {
        _db = db;
        _wallet = wallet;
        _holders = holders;
        _logger = logger;
    }

    private static string Name(string? first, string? last, string email) =>
        $"{first} {last}".Trim() is { Length: > 0 } n ? n : email;

    [HttpGet("wallets")]
    public async Task<IActionResult> List([FromQuery] string? search)
    {
        var query = _db.Institutions.AsNoTracking().Where(i => !i.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(i => i.Name.ToLower().Contains(term) || (i.Code != null && i.Code.ToLower().Contains(term)));
        }

        var rows = await query.OrderBy(i => i.Name).Take(100)
            .Select(i => new { i.Id, i.Name, i.City, i.WalletOwnerUserId })
            .ToListAsync();
        var holderIds = rows.Where(r => r.WalletOwnerUserId != null).Select(r => r.WalletOwnerUserId!.Value).ToList();
        var holders = await _db.Users.AsNoTracking().IgnoreQueryFilters().Where(u => holderIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email }).ToDictionaryAsync(u => u.Id);

        var items = new List<object>();
        foreach (var r in rows)
        {
            var holder = r.WalletOwnerUserId is int h && holders.TryGetValue(h, out var u) ? u : null;
            items.Add(new
            {
                institutionId = r.Id,
                name = r.Name,
                city = r.City,
                holderUserId = r.WalletOwnerUserId,
                holderName = holder == null ? null : Name(holder.FirstName, holder.LastName, holder.Email),
                holderEmail = holder?.Email,
                balanceXaf = r.WalletOwnerUserId is int id ? await _wallet.GetAvailableAsync(id) : 0m,
            });
        }
        return Ok(new { data = items, success = true });
    }

    [HttpGet("{institutionId:int}/wallet")]
    public async Task<IActionResult> Get(int institutionId)
    {
        var institution = await _db.Institutions.AsNoTracking().Where(i => i.Id == institutionId && !i.IsDeleted)
            .Select(i => new { i.Id, i.Name, i.City, i.WalletOwnerUserId }).FirstOrDefaultAsync();
        if (institution == null) return NotFound(new { success = false, error = "Institution introuvable." });

        var accounts = await _db.Users.AsNoTracking()
            .Where(u => u.InstitutionId == institutionId && u.Role == "institution")
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.IsActive, u.IsDeleted })
            .ToListAsync();

        var changes = await _db.InstitutionWalletHolderChanges.AsNoTracking()
            .Where(c => c.InstitutionId == institutionId)
            .OrderByDescending(c => c.CreatedAt).Take(50)
            .ToListAsync();
        var userIds = changes.SelectMany(c => new[] { c.OldHolderUserId ?? 0, c.NewHolderUserId, c.ChangedByUserId })
            .Where(i => i > 0).Distinct().ToList();
        var names = await _db.Users.AsNoTracking().IgnoreQueryFilters().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email }).ToDictionaryAsync(u => u.Id, u => Name(u.FirstName, u.LastName, u.Email));
        string? N(int? id) => id is int v && names.TryGetValue(v, out var n) ? n : null;

        var balance = institution.WalletOwnerUserId is int holder ? await _wallet.GetBalanceAsync(holder) : null;

        return Ok(new
        {
            data = new
            {
                institutionId = institution.Id,
                name = institution.Name,
                city = institution.City,
                holderUserId = institution.WalletOwnerUserId,
                availableXaf = balance?.AvailableXaf ?? 0m,
                pendingXaf = balance?.PendingXaf ?? 0m,
                accounts = accounts.Select(a => new
                {
                    userId = a.Id,
                    name = Name(a.FirstName, a.LastName, a.Email),
                    email = a.Email,
                    isHolder = a.Id == institution.WalletOwnerUserId,
                    // Éligible : compte institution actif, non supprimé, qui n'est pas déjà titulaire.
                    eligible = a.IsActive && !a.IsDeleted && a.Id != institution.WalletOwnerUserId,
                }),
                changes = changes.Select(c => new
                {
                    id = c.Id,
                    createdAt = c.CreatedAt,
                    oldHolderUserId = c.OldHolderUserId,
                    oldHolderName = N(c.OldHolderUserId),
                    newHolderUserId = c.NewHolderUserId,
                    newHolderName = N(c.NewHolderUserId),
                    amountXaf = c.AmountXaf,
                    changedByUserId = c.ChangedByUserId,
                    changedByName = N(c.ChangedByUserId),
                    reason = c.Reason,
                }),
            },
            success = true,
        });
    }

    [HttpPost("{institutionId:int}/wallet/holder")]
    public async Task<IActionResult> ChangeHolder(int institutionId, [FromBody] ChangeInstitutionWalletHolderRequest request)
    {
        try
        {
            var result = await _holders.ChangeHolderAsync(institutionId, request.NewHolderUserId, User.GetUserId(), request.Reason, request.ClientRequestId);
            return Ok(new
            {
                success = true,
                alreadyDone = result.AlreadyDone,
                data = new
                {
                    holderUserId = result.HolderUserId,
                    changeId = result.Change?.Id,
                    amountXaf = result.Change?.AmountXaf ?? 0m,
                    oldHolderUserId = result.Change?.OldHolderUserId,
                },
            });
        }
        catch (InstitutionWalletException ex)
        {
            return StatusCode(ex.HttpStatus, new { success = false, code = ex.Code, error = ex.Message });
        }
        catch (Exception ex) when (DbConcurrency.IsSerializationFailure(ex))
        {
            return StatusCode(409, new { success = false, code = "concurrent", error = "Une autre opération sur ce portefeuille est en cours. Réessaie dans un instant." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du changement de titulaire du portefeuille de l'institution {InstitutionId}", institutionId);
            return StatusCode(500, new { success = false, error = "Erreur serveur" });
        }
    }
}
