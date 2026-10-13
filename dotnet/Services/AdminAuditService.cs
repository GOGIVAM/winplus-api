using System.Security.Claims;
using System.Text.Json;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Point d'écriture unique du journal d'audit de l'administration (lot 9.7a,
/// suivi 18.3). Chaque action sensible (rôle, suspension, suppression
/// définitive, crédit, retrait, remboursement, e-mail de masse, réglage,
/// commission, code promo, annonce, litige, signalement, vérification de
/// tuteur, consultation d'une conversation WinAI…) y laisse une ligne :
/// auteur, action, cible, résumé avant/après, motif, adresse IP, date.
///
/// <see cref="Add"/> ajoute la ligne au contexte sans l'enregistrer, pour
/// qu'elle parte dans le même <c>SaveChanges</c> que l'action elle-même
/// (tout ou rien). <see cref="LogAsync"/> l'enregistre tout de suite, pour
/// les actions déjà validées par ailleurs (service, transaction propre).
/// </summary>
public interface IAdminAuditService
{
    AdminAuditLog Add(ClaimsPrincipal? actor, string? ipAddress, string action, string? targetType, object? targetId,
        string? targetLabel = null, object? before = null, object? after = null, string? reason = null);

    Task<AdminAuditLog> LogAsync(ClaimsPrincipal? actor, string? ipAddress, string action, string? targetType, object? targetId,
        string? targetLabel = null, object? before = null, object? after = null, string? reason = null);
}

public class AdminAuditService : IAdminAuditService
{
    private const int MaxSnapshotLength = 4000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ApplicationDbContext _db;
    private readonly ILogger<AdminAuditService> _logger;

    public AdminAuditService(ApplicationDbContext db, ILogger<AdminAuditService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public AdminAuditLog Add(ClaimsPrincipal? actor, string? ipAddress, string action, string? targetType, object? targetId,
        string? targetLabel = null, object? before = null, object? after = null, string? reason = null)
    {
        int? actorId = null;
        if (actor?.Identity?.IsAuthenticated == true || actor?.Claims.Any() == true)
        {
            try { actorId = actor!.GetUserId(); } catch (UnauthorizedAccessException) { actorId = null; }
        }

        var email = actor == null ? null : actor.GetUserEmail();
        var row = new AdminAuditLog
        {
            ActorUserId = actorId,
            ActorEmail = Truncate(string.IsNullOrWhiteSpace(email) ? null : email, 256),
            Action = Truncate(action, 80)!,
            TargetType = Truncate(targetType, 50),
            TargetId = Truncate(targetId?.ToString(), 64),
            TargetLabel = Truncate(targetLabel, 300),
            Before = Snapshot(before),
            After = Snapshot(after),
            Reason = Truncate(string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), 1000),
            IpAddress = Truncate(ipAddress, 64),
            CreatedAt = DateTime.UtcNow,
        };
        _db.AdminAuditLogs.Add(row);
        return row;
    }

    public async Task<AdminAuditLog> LogAsync(ClaimsPrincipal? actor, string? ipAddress, string action, string? targetType, object? targetId,
        string? targetLabel = null, object? before = null, object? after = null, string? reason = null)
    {
        var row = Add(actor, ipAddress, action, targetType, targetId, targetLabel, before, after, reason);
        try
        {
            if (row.ActorUserId.HasValue && row.ActorEmail == null)
                row.ActorEmail = await _db.Users.AsNoTracking().IgnoreQueryFilters()
                    .Where(u => u.Id == row.ActorUserId.Value).Select(u => u.Email).FirstOrDefaultAsync();
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // L'action est déjà appliquée : un échec du journal ne la défait
            // pas, mais il est tracé en erreur pour être repris.
            _logger.LogError(ex, "Journal d'audit non écrit : {Action} sur {TargetType} {TargetId}", action, targetType, targetId);
            _db.Entry(row).State = EntityState.Detached;
        }
        return row;
    }

    private static string? Snapshot(object? value)
    {
        if (value == null) return null;
        var json = value as string ?? JsonSerializer.Serialize(value, JsonOptions);
        return Truncate(json, MaxSnapshotLength);
    }

    private static string? Truncate(string? value, int max) =>
        value == null ? null : value.Length <= max ? value : value[..max];
}

/// <summary>Raccourcis d'écriture du journal depuis un contrôleur (auteur et IP lus dans la requête).</summary>
public static class AdminAuditControllerExtensions
{
    public static string? ClientIp(this ControllerBase controller) =>
        controller.HttpContext?.Connection?.RemoteIpAddress?.ToString();

    public static AdminAuditLog AuditAdd(this ControllerBase controller, IAdminAuditService audit, string action, string? targetType,
        object? targetId, string? targetLabel = null, object? before = null, object? after = null, string? reason = null) =>
        audit.Add(controller.User, controller.ClientIp(), action, targetType, targetId, targetLabel, before, after, reason);

    public static Task<AdminAuditLog> AuditAsync(this ControllerBase controller, IAdminAuditService audit, string action, string? targetType,
        object? targetId, string? targetLabel = null, object? before = null, object? after = null, string? reason = null) =>
        audit.LogAsync(controller.User, controller.ClientIp(), action, targetType, targetId, targetLabel, before, after, reason);
}
