using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Services;
using BC = BCrypt.Net.BCrypt;

namespace Backend.Controllers;

// ═══════════════════════════════════════════════════════════════════════════
//  DTOs
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Ligne de la table « Gestion des utilisateurs » du dashboard admin.</summary>
public class AdminUserRowDto
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string Role { get; set; } = "student";
    /// <summary>active | suspended | deleted</summary>
    public string Status { get; set; } = "active";
    public bool IsEmailVerified { get; set; }

    // Présence — calculée côté serveur, jamais côté client
    public bool IsOnline { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int ActiveSessions { get; set; }

    // Abonnement / paiement
    /// <summary>paid | trial | unpaid | expired | refunded</summary>
    public string PaymentState { get; set; } = "unpaid";
    public string? PlanName { get; set; }
    public DateTime? SubscriptionEndsAt { get; set; }
    public decimal TotalPaid { get; set; }
    public int OrdersCount { get; set; }

    // Contexte de connexion
    public string? LastIp { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? Device { get; set; }

    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminPagedDto<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class AdminUserStatsDto
{
    public int Total { get; set; }
    public int Online { get; set; }
    public int Active { get; set; }
    public int Suspended { get; set; }
    public int Paying { get; set; }
    public int Unpaid { get; set; }
    public int NewToday { get; set; }
    public decimal Revenue { get; set; }
}

public class AdminUserSessionDto
{
    public int Id { get; set; }
    public string? Device { get; set; }
    public string? Browser { get; set; }
    public string? Ip { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public bool IsCurrent { get; set; }
}

public class AdminUserPaymentDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "XAF";
    public string? Method { get; set; }
    public string Status { get; set; } = "";
    public string? PlanName { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminUserActivityDto
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Label { get; set; } = "";
    public DateTime At { get; set; }
}

public class AdminCreateUserRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string Role { get; set; } = "student";
    public string? Password { get; set; }
    public bool SendInvite { get; set; } = true;
}

public class AdminUpdateUserRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Role { get; set; }
    /// <summary>active | suspended</summary>
    public string? Status { get; set; }
}

public class AdminSetRoleRequest
{
    public string Role { get; set; } = "student";
}

public class AdminSuspendRequest
{
    public string? Reason { get; set; }
}

public class AdminGrantSubscriptionRequest
{
    public int? PlanId { get; set; }
    public int Months { get; set; } = 1;
    public string? Note { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Controller
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Administration complète des comptes utilisateurs : CRUD, présence temps réel,
/// état de paiement, sessions, historique et octroi d'abonnement.
///
/// IMPORTANT — ce controller prend en charge TOUTES les routes « api/admin/users… ».
/// Les méthodes homonymes doivent être supprimées d'AdminController.cs
/// (voir README-admin-users.md), sinon ASP.NET lève une AmbiguousMatchException.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Produces("application/json")]
[Authorize(Policy = "AdminOnly")]
public class AdminUsersController : ControllerBase
{
    /// <summary>Une session est considérée active si vue il y a moins de 5 minutes.</summary>
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(5);

    private static readonly string[] AllowedRoles = { "student", "teacher", "parent", "admin" };

    private readonly ApplicationDbContext _db;
    private readonly IEmailService _email;
    private readonly ILogger<AdminUsersController> _logger;

    public AdminUsersController(
        ApplicationDbContext db,
        IEmailService email,
        ILogger<AdminUsersController> logger)
    {
        _db = db;
        _email = email;
        _logger = logger;
    }

    private int? CurrentAdminId()
    {
        var raw = User.FindFirst("sub")?.Value
               ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(raw, out var id) ? id : null;
    }

    // ── Projection ──────────────────────────────────────────────────────────

    /// <summary>
    /// Agrégats par utilisateur. Tout est calculé en base : présence, montant
    /// encaissé, abonnement courant. Aucune valeur simulée.
    /// </summary>
    private sealed class UserAggregate
    {
        public User User { get; init; } = null!;
        public UserSession? LastSession { get; init; }
        public int ActiveSessions { get; init; }
        public Subscription? Subscription { get; init; }
        public string? PlanName { get; init; }
        public decimal TotalPaid { get; init; }
        public int OrdersCount { get; init; }
    }

    private async Task<List<UserAggregate>> AggregateAsync(List<User> users, DateTime now)
    {
        var ids = users.Select(u => u.Id).ToList();
        var onlineSince = now - OnlineWindow;

        var sessions = await _db.UserSessions
            .Where(s => s.UserId != 0 && ids.Contains(s.UserId))
            .ToListAsync();

        var subs = await _db.Subscriptions
            .Where(s => ids.Contains(s.UserId) && !s.IsDeleted)
            .ToListAsync();

        var planIds = subs.Select(s => s.PricingPlanId).Distinct().ToList();
        var plans = await _db.PricingPlans
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, p => p.Name);

        var paid = await _db.Payments
            .Where(p => p.UserId != null && ids.Contains(p.UserId!.Value) && p.Status == "completed")
            .GroupBy(p => p.UserId!.Value)
            .Select(g => new { UserId = g.Key, Sum = g.Sum(x => x.Amount) })
            .ToListAsync();

        var orders = await _db.Orders
            .Where(o => o.UserId != null && ids.Contains(o.UserId!.Value) && !o.IsDeleted)
            .GroupBy(o => o.UserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync();

        return users.Select(u =>
        {
            var userSessions = sessions.Where(s => s.UserId == u.Id).ToList();
            var currentSub = subs
                .Where(s => s.UserId == u.Id)
                .OrderByDescending(s => s.EndDate ?? DateTime.MaxValue)
                .ThenByDescending(s => s.StartDate)
                .FirstOrDefault();

            return new UserAggregate
            {
                User = u,
                LastSession = userSessions
                    .OrderByDescending(s => s.LastActivityAt)
                    .FirstOrDefault(),
                ActiveSessions = userSessions.Count(s =>
                    s.IsActive && s.LastActivityAt >= onlineSince),
                Subscription = currentSub,
                PlanName = currentSub != null && plans.TryGetValue(currentSub.PricingPlanId, out var n) ? n : null,
                TotalPaid = paid.FirstOrDefault(p => p.UserId == u.Id)?.Sum ?? 0m,
                OrdersCount = orders.FirstOrDefault(o => o.UserId == u.Id)?.Count ?? 0,
            };
        }).ToList();
    }

    private static string ResolveStatus(User u) =>
        u.IsDeleted ? "deleted" : (u.IsActive ? "active" : "suspended");

    private static string ResolvePaymentState(UserAggregate a, DateTime now)
    {
        var sub = a.Subscription;
        if (sub != null)
        {
            var status = (sub.Status ?? "").ToLowerInvariant();
            if (status == "trial")     return "trial";
            if (status == "refunded")  return "refunded";
            var stillValid = sub.EndDate == null || sub.EndDate > now;
            if (status == "active" && stillValid) return "paid";
            if (status == "cancelled" && stillValid) return "paid"; // résilié mais couvert
            return "expired";
        }
        // Aucun abonnement : on se rabat sur l'encaissement réel.
        return a.TotalPaid > 0 ? "paid" : "unpaid";
    }

    private static (string? city, string? country) SplitLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return (null, null);
        var parts = location.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => (null, null),
            1 => (parts[0], null),
            _ => (parts[0], parts[^1]),
        };
    }

    private static AdminUserRowDto ToRow(UserAggregate a, DateTime now)
    {
        var u = a.User;
        var (city, country) = SplitLocation(a.LastSession?.Location);
        var name = $"{u.FirstName} {u.LastName}".Trim();

        return new AdminUserRowDto
        {
            Id = u.Id,
            Email = u.Email,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Name = string.IsNullOrWhiteSpace(name) ? u.Email : name,
            Phone = u.Phone,
            Role = (u.Role ?? "student").ToLowerInvariant(),
            Status = ResolveStatus(u),
            IsEmailVerified = u.IsEmailVerified,

            IsOnline = a.ActiveSessions > 0,
            LastSeenAt = a.LastSession?.LastActivityAt ?? u.LastLoginAt,
            LastLoginAt = u.LastLoginAt,
            ActiveSessions = a.ActiveSessions,

            PaymentState = ResolvePaymentState(a, now),
            PlanName = a.PlanName,
            SubscriptionEndsAt = a.Subscription?.EndDate,
            TotalPaid = a.TotalPaid,
            OrdersCount = a.OrdersCount,

            LastIp = a.LastSession?.IpAddress,
            City = city ?? u.City,
            Country = country,
            Device = a.LastSession?.DeviceName ?? a.LastSession?.DeviceType,

            AvatarUrl = u.AvatarUrl ?? u.ProfileImageUrl,
            CreatedAt = u.CreatedAt,
        };
    }

    // ── GET /api/admin/users ────────────────────────────────────────────────

    /// <summary>
    /// Liste paginée avec recherche et filtres rôle / statut / paiement / présence.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q = null,
        [FromQuery] string? role = null,
        [FromQuery] string? status = null,
        [FromQuery] string? payment = null,
        [FromQuery] bool? online = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? sort = null)
    {
        try
        {
            if (page < 1) page = 1;
            pageSize = Math.Clamp(pageSize, 1, 200);
            var now = DateTime.UtcNow;

            var query = _db.Users.AsNoTracking().AsQueryable();

            // Les comptes supprimés ne remontent que si on les demande.
            if (status == "deleted") query = query.Where(u => u.IsDeleted);
            else if (status == "active") query = query.Where(u => !u.IsDeleted && u.IsActive);
            else if (status == "suspended") query = query.Where(u => !u.IsDeleted && !u.IsActive);
            else query = query.Where(u => !u.IsDeleted);

            if (!string.IsNullOrWhiteSpace(role) && role != "all")
                query = query.Where(u => u.Role.ToLower() == role.ToLower());

            if (!string.IsNullOrWhiteSpace(q))
            {
                var s = q.Trim().ToLower();
                query = query.Where(u =>
                    u.Email.ToLower().Contains(s) ||
                    (u.FirstName != null && u.FirstName.ToLower().Contains(s)) ||
                    (u.LastName != null && u.LastName.ToLower().Contains(s)) ||
                    (u.Phone != null && u.Phone.Contains(s)));
            }

            query = sort switch
            {
                "name"    => query.OrderBy(u => u.FirstName).ThenBy(u => u.LastName),
                "email"   => query.OrderBy(u => u.Email),
                "oldest"  => query.OrderBy(u => u.CreatedAt),
                "lastseen" => query.OrderByDescending(u => u.LastLoginAt),
                _         => query.OrderByDescending(u => u.CreatedAt),
            };

            var needsPostFilter = online == true ||
                (!string.IsNullOrWhiteSpace(payment) && payment != "all");

            if (!needsPostFilter)
            {
                var total = await query.CountAsync();
                var pageUsers = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
                var rows = (await AggregateAsync(pageUsers, now)).Select(a => ToRow(a, now)).ToList();

                return Ok(new AdminPagedDto<AdminUserRowDto>
                {
                    Items = rows, Total = total, Page = page, PageSize = pageSize,
                });
            }

            // Présence et état de paiement sont des agrégats : on filtre après
            // projection, sur un lot borné pour ne pas charger toute la base.
            var candidates = await query.Take(5000).ToListAsync();
            var all = (await AggregateAsync(candidates, now)).Select(a => ToRow(a, now)).ToList();

            if (online == true) all = all.Where(r => r.IsOnline).ToList();
            if (!string.IsNullOrWhiteSpace(payment) && payment != "all")
                all = all.Where(r => r.PaymentState == payment).ToList();

            return Ok(new AdminPagedDto<AdminUserRowDto>
            {
                Items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                Total = all.Count,
                Page = page,
                PageSize = pageSize,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AdminUsers.List failed");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── GET /api/admin/users/stats ──────────────────────────────────────────

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        try
        {
            var now = DateTime.UtcNow;
            var onlineSince = now - OnlineWindow;
            var todayStart = now.Date;

            var total     = await _db.Users.CountAsync(u => !u.IsDeleted);
            var active    = await _db.Users.CountAsync(u => !u.IsDeleted && u.IsActive);
            var suspended = await _db.Users.CountAsync(u => !u.IsDeleted && !u.IsActive);
            var newToday  = await _db.Users.CountAsync(u => !u.IsDeleted && u.CreatedAt >= todayStart);

            var online = await _db.UserSessions
                .Where(s => s.IsActive && s.LastActivityAt >= onlineSince)
                .Select(s => s.UserId)
                .Distinct()
                .CountAsync();

            var paying = await _db.Subscriptions
                .Where(s => !s.IsDeleted
                         && s.Status == "active"
                         && (s.EndDate == null || s.EndDate > now))
                .Select(s => s.UserId)
                .Distinct()
                .CountAsync();

            var revenue = await _db.Payments
                .Where(p => p.Status == "completed")
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;

            return Ok(new AdminUserStatsDto
            {
                Total = total,
                Online = online,
                Active = active,
                Suspended = suspended,
                Paying = paying,
                Unpaid = Math.Max(0, total - paying),
                NewToday = newToday,
                Revenue = revenue,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AdminUsers.Stats failed");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── GET /api/admin/users/online ─────────────────────────────────────────

    /// <summary>Comptes ayant une session active (vue < 5 min).</summary>
    [HttpGet("online")]
    public async Task<IActionResult> Online()
    {
        try
        {
            var now = DateTime.UtcNow;
            var onlineSince = now - OnlineWindow;

            var ids = await _db.UserSessions
                .Where(s => s.IsActive && s.LastActivityAt >= onlineSince)
                .Select(s => s.UserId)
                .Distinct()
                .ToListAsync();

            if (ids.Count == 0) return Ok(new List<AdminUserRowDto>());

            var users = await _db.Users
                .AsNoTracking()
                .Where(u => ids.Contains(u.Id) && !u.IsDeleted)
                .ToListAsync();

            var rows = (await AggregateAsync(users, now))
                .Select(a => ToRow(a, now))
                .OrderByDescending(r => r.LastSeenAt)
                .ToList();

            return Ok(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AdminUsers.Online failed");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── GET /api/admin/users/{id} ───────────────────────────────────────────

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOne(int id)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound(new { error = "Utilisateur introuvable" });

        var now = DateTime.UtcNow;
        var agg = (await AggregateAsync(new List<User> { user }, now)).First();
        return Ok(ToRow(agg, now));
    }

    // ── POST /api/admin/users ───────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AdminCreateUserRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new { error = "L'email est requis" });

        var email = req.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == email))
            return Conflict(new { error = "Cet email est déjà utilisé" });

        var role = (req.Role ?? "student").ToLowerInvariant();
        if (!AllowedRoles.Contains(role))
            return BadRequest(new { error = "Rôle invalide" });

        // Sans mot de passe fourni, on en génère un que personne ne connaît :
        // l'utilisateur passe obligatoirement par le lien d'invitation.
        var password = string.IsNullOrWhiteSpace(req.Password)
            ? Guid.NewGuid().ToString("N") + "!Aa1"
            : req.Password;

        var user = new User
        {
            Email = email,
            PasswordHash = BC.HashPassword(password),
            FirstName = req.FirstName?.Trim(),
            LastName = req.LastName?.Trim(),
            Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim(),
            Role = role,
            IsActive = true,
            IsEmailVerified = false,
            CreatedAt = DateTime.UtcNow,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        if (req.SendInvite)
        {
            var token = Guid.NewGuid().ToString("N");
            _db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
            });
            await _db.SaveChangesAsync();
            try
            {
                await _email.SendPasswordResetAsync(user.Email, user.FirstName ?? "", token);
            }
            catch (Exception ex)
            {
                // Le compte existe : un échec d'email ne doit pas annuler la création.
                _logger.LogWarning(ex, "Invitation email failed for {Email}", user.Email);
            }
        }

        _logger.LogInformation("Admin {AdminId} created user {UserId}", CurrentAdminId(), user.Id);

        var now = DateTime.UtcNow;
        var agg = (await AggregateAsync(new List<User> { user }, now)).First();
        return CreatedAtAction(nameof(GetOne), new { id = user.Id }, ToRow(agg, now));
    }

    // ── PUT /api/admin/users/{id} ───────────────────────────────────────────

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AdminUpdateUserRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound(new { error = "Utilisateur introuvable" });

        if (req.FirstName != null) user.FirstName = req.FirstName.Trim();
        if (req.LastName != null) user.LastName = req.LastName.Trim();
        if (req.Phone != null) user.Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim();

        if (!string.IsNullOrWhiteSpace(req.Email))
        {
            var email = req.Email.Trim().ToLowerInvariant();
            if (email != user.Email.ToLowerInvariant() &&
                await _db.Users.AnyAsync(u => u.Id != id && u.Email.ToLower() == email))
                return Conflict(new { error = "Cet email est déjà utilisé" });
            user.Email = email;
        }

        if (!string.IsNullOrWhiteSpace(req.Role))
        {
            var role = req.Role.ToLowerInvariant();
            if (!AllowedRoles.Contains(role)) return BadRequest(new { error = "Rôle invalide" });
            user.Role = role;
        }

        if (!string.IsNullOrWhiteSpace(req.Status))
        {
            if (req.Status == "active") user.IsActive = true;
            else if (req.Status == "suspended") user.IsActive = false;
            else return BadRequest(new { error = "Statut invalide" });
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var agg = (await AggregateAsync(new List<User> { user }, now)).First();
        return Ok(ToRow(agg, now));
    }

    // ── PUT /api/admin/users/{id}/role ──────────────────────────────────────

    [HttpPut("{id:int}/role")]
    public async Task<IActionResult> SetRole(int id, [FromBody] AdminSetRoleRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound(new { error = "Utilisateur introuvable" });

        var role = (req.Role ?? "").ToLowerInvariant();
        if (!AllowedRoles.Contains(role)) return BadRequest(new { error = "Rôle invalide" });

        // Garde-fou : ne pas se retirer soi-même les droits admin.
        if (user.Id == CurrentAdminId() && role != "admin")
            return BadRequest(new { error = "Vous ne pouvez pas retirer votre propre rôle admin" });

        user.Role = role;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Admin {AdminId} set role of {UserId} to {Role}", CurrentAdminId(), id, role);
        return Ok(new { message = "Rôle mis à jour", role });
    }

    // ── Suspension / réactivation ───────────────────────────────────────────

    [HttpPost("{id:int}/suspend")]
    public async Task<IActionResult> Suspend(int id, [FromBody] AdminSuspendRequest? req = null)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound(new { error = "Utilisateur introuvable" });
        if (user.Id == CurrentAdminId())
            return BadRequest(new { error = "Vous ne pouvez pas suspendre votre propre compte" });

        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;

        // Un compte suspendu ne doit plus avoir de session ouverte.
        await RevokeAllSessionsAsync(id);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Admin {AdminId} suspended user {UserId}. Reason: {Reason}",
            CurrentAdminId(), id, req?.Reason ?? "—");
        return Ok(new { message = "Compte suspendu" });
    }

    [HttpPost("{id:int}/reactivate")]
    public async Task<IActionResult> Reactivate(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound(new { error = "Utilisateur introuvable" });

        user.IsActive = true;
        user.IsDeleted = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Compte réactivé" });
    }

    /// <summary>Alias historique conservé pour compatibilité front.</summary>
    [HttpPost("{id:int}/restore")]
    public Task<IActionResult> Restore(int id) => Reactivate(id);

    // ── Suppression ─────────────────────────────────────────────────────────

    /// <summary>Suppression douce : le compte reste en base, restaurable.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> SoftDelete(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound(new { error = "Utilisateur introuvable" });
        if (user.Id == CurrentAdminId())
            return BadRequest(new { error = "Vous ne pouvez pas supprimer votre propre compte" });

        user.IsDeleted = true;
        user.IsActive = false;
        user.DeletedByUserId = CurrentAdminId();
        user.UpdatedAt = DateTime.UtcNow;

        await RevokeAllSessionsAsync(id);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Admin {AdminId} soft-deleted user {UserId}", CurrentAdminId(), id);
        return Ok(new { message = "Compte supprimé" });
    }

    /// <summary>Alias historique : POST users/{id}/delete.</summary>
    [HttpPost("{id:int}/delete")]
    public Task<IActionResult> SoftDeletePost(int id) => SoftDelete(id);

    /// <summary>Suppression définitive : purge les données liées puis le compte.</summary>
    [HttpDelete("{id:int}/hard")]
    public async Task<IActionResult> HardDelete(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound(new { error = "Utilisateur introuvable" });
        if (user.Id == CurrentAdminId())
            return BadRequest(new { error = "Vous ne pouvez pas supprimer votre propre compte" });

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            _db.UserSessions.RemoveRange(_db.UserSessions.Where(s => s.UserId == id));
            _db.RefreshTokens.RemoveRange(_db.RefreshTokens.Where(t => t.UserId == id));
            _db.PasswordResetTokens.RemoveRange(_db.PasswordResetTokens.Where(t => t.UserId == id));

            // Les paiements et commandes sont conservés pour la comptabilité :
            // on les détache du compte plutôt que de les effacer.
            foreach (var p in _db.Payments.Where(p => p.UserId == id)) p.UserId = null;
            foreach (var o in _db.Orders.Where(o => o.UserId == id)) o.UserId = null;

            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogWarning("Admin {AdminId} hard-deleted user {UserId}", CurrentAdminId(), id);
            return Ok(new { message = "Compte supprimé définitivement" });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Hard delete failed for user {UserId}", id);
            return StatusCode(500, new { error = "La suppression définitive a échoué" });
        }
    }

    // ── Email / mot de passe ────────────────────────────────────────────────

    [HttpPost("{id:int}/verify-email")]
    public async Task<IActionResult> VerifyEmail(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound(new { error = "Utilisateur introuvable" });

        user.IsEmailVerified = true;
        user.VerifiedAt = DateTime.UtcNow;
        user.VerificationCode = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Email marqué comme vérifié" });
    }

    /// <summary>Envoie un lien de réinitialisation ; l'admin ne voit jamais le mot de passe.</summary>
    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound(new { error = "Utilisateur introuvable" });

        var token = Guid.NewGuid().ToString("N");
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
        });
        await _db.SaveChangesAsync();

        try
        {
            await _email.SendPasswordResetAsync(user.Email, user.FirstName ?? "", token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Password reset email failed for {UserId}", id);
            return StatusCode(502, new { error = "Le lien a été créé mais l'email n'a pas pu être envoyé" });
        }

        return Ok(new { message = "Lien de réinitialisation envoyé" });
    }

    // ── Sessions ────────────────────────────────────────────────────────────

    [HttpGet("{id:int}/sessions")]
    public async Task<IActionResult> Sessions(int id)
    {
        var onlineSince = DateTime.UtcNow - OnlineWindow;
        var sessions = await _db.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == id)
            .OrderByDescending(s => s.LastActivityAt)
            .Take(50)
            .ToListAsync();

        var dtos = sessions.Select(s =>
        {
            var (city, country) = SplitLocation(s.Location);
            return new AdminUserSessionDto
            {
                Id = s.Id,
                Device = s.DeviceName ?? s.DeviceType,
                Browser = s.UserAgent,
                Ip = s.IpAddress,
                City = city,
                Country = country,
                CreatedAt = s.CreatedAt,
                LastSeenAt = s.LastActivityAt,
                IsCurrent = s.IsActive && s.LastActivityAt >= onlineSince,
            };
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>Déconnexion forcée : révoque sessions et refresh tokens.</summary>
    [HttpDelete("{id:int}/sessions")]
    public async Task<IActionResult> RevokeSessions(int id)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == id))
            return NotFound(new { error = "Utilisateur introuvable" });

        var count = await RevokeAllSessionsAsync(id);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Admin {AdminId} revoked {Count} session(s) of user {UserId}",
            CurrentAdminId(), count, id);
        return Ok(new { message = "Sessions révoquées", revoked = count });
    }

    private async Task<int> RevokeAllSessionsAsync(int userId)
    {
        var now = DateTime.UtcNow;

        var sessions = await _db.UserSessions.Where(s => s.UserId == userId && s.IsActive).ToListAsync();
        foreach (var s in sessions) { s.IsActive = false; s.ExpiresAt = now; }

        var tokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync();
        foreach (var t in tokens) t.RevokedAt = now;

        return sessions.Count;
    }

    // ── Paiements ───────────────────────────────────────────────────────────

    [HttpGet("{id:int}/payments")]
    public async Task<IActionResult> Payments(int id)
    {
        var payments = await _db.Payments
            .AsNoTracking()
            .Where(p => p.UserId == id)
            .OrderByDescending(p => p.CreatedAt)
            .Take(100)
            .Select(p => new AdminUserPaymentDto
            {
                Id = p.Id,
                Reference = p.TransactionId ?? p.NotchpayReference,
                Amount = p.Amount,
                Currency = p.Currency,
                Method = p.PaymentMethod ?? p.Operator,
                Status = p.Status,
                PlanName = p.Description,
                PaidAt = p.CompletedAt ?? p.ProcessedAt,
                CreatedAt = p.CreatedAt,
            })
            .ToListAsync();

        return Ok(payments);
    }

    // ── Activité ────────────────────────────────────────────────────────────

    /// <summary>Journal condensé : connexions, commandes, paiements.</summary>
    [HttpGet("{id:int}/activity")]
    public async Task<IActionResult> Activity(int id, [FromQuery] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);
        var events = new List<AdminUserActivityDto>();

        var sessions = await _db.UserSessions.AsNoTracking()
            .Where(s => s.UserId == id)
            .OrderByDescending(s => s.CreatedAt).Take(limit)
            .Select(s => new { s.Id, s.CreatedAt, s.DeviceName, s.DeviceType, s.IpAddress })
            .ToListAsync();

        events.AddRange(sessions.Select(s => new AdminUserActivityDto
        {
            Id = $"session-{s.Id}",
            Type = "login",
            Label = $"Connexion depuis {s.DeviceName ?? s.DeviceType ?? "un appareil inconnu"}"
                  + (s.IpAddress != null ? $" ({s.IpAddress})" : ""),
            At = s.CreatedAt,
        }));

        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.UserId == id && !o.IsDeleted)
            .OrderByDescending(o => o.CreatedAt).Take(limit)
            .Select(o => new { o.Id, o.OrderNumber, o.TotalAmount, o.Status, o.CreatedAt })
            .ToListAsync();

        events.AddRange(orders.Select(o => new AdminUserActivityDto
        {
            Id = $"order-{o.Id}",
            Type = "order",
            Label = $"Commande {o.OrderNumber} — {o.TotalAmount:N0} XAF ({o.Status})",
            At = o.CreatedAt,
        }));

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.UserId == id)
            .OrderByDescending(p => p.CreatedAt).Take(limit)
            .Select(p => new { p.Id, p.Amount, p.Currency, p.Status, p.CreatedAt })
            .ToListAsync();

        events.AddRange(payments.Select(p => new AdminUserActivityDto
        {
            Id = $"payment-{p.Id}",
            Type = "payment",
            Label = $"Paiement {p.Amount:N0} {p.Currency} — {p.Status}",
            At = p.CreatedAt,
        }));

        return Ok(events.OrderByDescending(e => e.At).Take(limit).ToList());
    }

    // ── Octroi manuel d'abonnement ──────────────────────────────────────────

    /// <summary>
    /// Accorde un abonnement sans passage par le paiement en ligne
    /// (encaissement Mobile Money hors plateforme, geste commercial).
    /// </summary>
    [HttpPost("{id:int}/grant-subscription")]
    public async Task<IActionResult> GrantSubscription(int id, [FromBody] AdminGrantSubscriptionRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound(new { error = "Utilisateur introuvable" });

        var months = Math.Clamp(req.Months, 1, 36);
        var now = DateTime.UtcNow;

        var planId = req.PlanId ?? await _db.PricingPlans
            .Where(p => !p.IsDeleted && !p.IsArchived)
            .OrderBy(p => p.Price)
            .Select(p => p.Id)
            .FirstOrDefaultAsync();

        if (planId == 0)
            return BadRequest(new { error = "Aucun plan tarifaire disponible" });

        var existing = await _db.Subscriptions
            .Where(s => s.UserId == id && !s.IsDeleted && s.Status == "active")
            .OrderByDescending(s => s.EndDate ?? DateTime.MaxValue)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            // On prolonge à partir de la fin en cours, jamais d'écrasement.
            var from = existing.EndDate.HasValue && existing.EndDate > now ? existing.EndDate.Value : now;
            existing.EndDate = from.AddMonths(months);
            existing.PricingPlanId = planId;
            existing.RenewalCount += 1;
            existing.UpdatedAt = now;
        }
        else
        {
            _db.Subscriptions.Add(new Subscription
            {
                UserId = id,
                PricingPlanId = planId,
                StartDate = now,
                EndDate = now.AddMonths(months),
                Status = "active",
                CreatedAt = now,
            });
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Admin {AdminId} granted {Months} month(s) to user {UserId}. Note: {Note}",
            CurrentAdminId(), months, id, req.Note ?? "—");

        return Ok(new { message = $"Abonnement de {months} mois accordé" });
    }
}
