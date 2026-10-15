using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Controller pour les opérations administrateur
/// </summary>
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly ILogger<AdminController> _logger;
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IStorageService _storage;
    private readonly IEmailService _email;
    private readonly IFastApiClient _fastApi;
    private readonly INotificationPreferenceService _notificationPreferences;
    private readonly IAdminAuditService _audit;
    private readonly INtfyService _ntfy;
    private readonly IAdminEmailCampaignService _campaigns;

    public AdminController(IAdminService adminService, ILogger<AdminController> logger, ApplicationDbContext db, IConfiguration configuration, IHttpClientFactory httpClientFactory, IStorageService storage, IEmailService email, IFastApiClient fastApi, INotificationPreferenceService notificationPreferences,
        IAdminAuditService audit, INtfyService ntfy, IAdminEmailCampaignService campaigns)
    {
        _audit = audit;
        _ntfy = ntfy;
        _campaigns = campaigns;
        _adminService = adminService;
        _logger = logger;
        _db = db;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _storage = storage;
        _email = email;
        _fastApi = fastApi;
        _notificationPreferences = notificationPreferences;
    }

    private HttpClient PyClient() => _httpClientFactory.CreateClient("FastApiClient");

    private static bool HasReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length >= AdminUsersController.MinReasonLength;

    private IActionResult ReasonRequired() =>
        BadRequest(new { code = "reason_required", error = $"Un motif d'au moins {AdminUsersController.MinReasonLength} caractères est obligatoire pour cette action." });

    /// <summary>Notification in-app + ntfy à l'auteur d'un contenu (décision de modération).</summary>
    private async Task NotifyAuthorAsync(int? authorId, int subjectId, string title, string message)
    {
        if (authorId == null) return;
        try
        {
            await _ntfy.PublishAsync($"winplus-user-{authorId}", title, message,
                userId: authorId, type: "Content", relatedEntityType: "subject", relatedEntityId: subjectId);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Notification à l'auteur non envoyée (contenu {SubjectId})", subjectId); }
    }

    /// <summary>
    /// Lot 9.7a (18.1) : un relais vers FastAPI renvoie le VRAI code HTTP de
    /// FastAPI (il renvoyait toujours 200, y compris sur une erreur 500 ou un
    /// refus 403, que l'écran prenait pour un succès).
    /// </summary>
    private static async Task<IActionResult> RelayAsync(HttpResponseMessage res)
    {
        var json = await res.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json))
            json = res.IsSuccessStatusCode ? "{}" : "{\"error\":\"Réponse vide du service WinAI\"}";
        return new ContentResult { Content = json, ContentType = "application/json", StatusCode = (int)res.StatusCode };
    }

    private void ForwardAuth(HttpClient c)
    {
        if (Request.Headers.TryGetValue("Authorization", out var v))
            c.DefaultRequestHeaders.Authorization =
                System.Net.Http.Headers.AuthenticationHeaderValue.Parse(v.ToString());
    }

    // Lot 9.7a (18.9, consigne du 2026-10-13 : activer plutôt que supprimer).
    // GET subjects est remplacée par GET /api/admin/catalog (même usage, DTO
    // complet) et GET orders par AdminOrdersController (même route, paginée,
    // avec détail) ; GET dashboard, pur doublon de statistics qui ajoutait un
    // état de santé inventé (« Healthy »), est retirée. statistics et
    // dashboard/stats sont recalculées en base et affichées par la vue globale
    // (inventaire) et la Santé système (volumes).

    /// <summary>
    /// Inventaire de la plateforme (comptes, contenus, commandes), compté en
    /// base. Lot 9.7a : l'ancienne version passait par des dépôts génériques
    /// et ne distinguait ni actifs, ni publiés, ni encaissés.
    /// </summary>
    [HttpGet("statistics")]
    public async Task<IActionResult> GetSystemStatistics()
    {
        try
        {
            return Ok(await BuildInventoryAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system statistics");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>Même inventaire, enveloppé { data, success } (forme lue par la vue globale).</summary>
    [HttpGet("dashboard/stats")]
    public async Task<IActionResult> GetDashboardStats()
    {
        try
        {
            return Ok(new { data = await BuildInventoryAsync(), success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard stats");
            return StatusCode(500, new { success = false, error = "Erreur serveur" });
        }
    }

    private async Task<object> BuildInventoryAsync()
    {
        var paid = AdminMetrics.CompletedOrderStatuses;
        return new
        {
            totalUsers = await _db.Users.CountAsync(u => !u.IsDeleted),
            activeAccounts = await _db.Users.CountAsync(u => !u.IsDeleted && u.IsActive),
            suspendedAccounts = await _db.Users.CountAsync(u => !u.IsDeleted && !u.IsActive),
            deletedAccounts = await _db.Users.IgnoreQueryFilters().CountAsync(u => u.IsDeleted),
            totalSubjects = await _db.Subjects.CountAsync(s => !s.IsDeleted),
            publishedSubjects = await _db.Subjects.CountAsync(s => !s.IsDeleted && s.IsPublished),
            totalCourses = await _db.Courses.CountAsync(),
            publishedCourses = await _db.Courses.CountAsync(c => c.Status == "published"),
            totalOrders = await _db.Orders.CountAsync(o => !o.IsDeleted),
            completedOrders = await _db.Orders.CountAsync(o => !o.IsDeleted && paid.Contains(o.Status.ToLower())),
            lastUpdated = DateTime.UtcNow,
        };
    }
    /// <summary>
    /// Récupère les activités récentes
    /// </summary>
    /// <returns>Activités récentes</returns>
    /// <response code="200">Activités retournées</response>
    /// <response code="401">Non autorisé</response>
    /// <response code="500">Erreur serveur</response>
    [HttpGet("activities/recent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetRecentActivities([FromQuery] int limit = 50)
    {
        try
        {
            if (limit < 1) limit = 50;
            if (limit > 200) limit = 200;

            // Hypothèse B : GuestEmail/GuestName absentes si migration 20260614 non appliquée.
            // Fallback sans ces colonnes si la requête échoue.
            List<AdminActivityEntry> orders;
            try
            {
                orders = await _db.Orders
                    .Where(o => !o.IsDeleted)
                    .OrderByDescending(o => o.CreatedAt)
                    .Take(limit / 2)
                    .Select(o => new AdminActivityEntry(
                        $"order-{o.Id}",
                        "order",
                        o.CreatedAt,
                        $"Commande #{o.OrderNumber}",
                        o.GuestEmail ?? "",
                        o.Status.ToLower() == "completed" ? "success" : o.Status.ToLower() == "failed" ? "failure" : "warning",
                        o.GuestName ?? "Anonyme",
                        o.GuestEmail ?? "",
                        $"Commande #{o.OrderNumber} · {o.TotalAmount:N0} XAF"
                    ))
                    .ToListAsync();
            }
            catch (Exception exOrders)
            {
                // Colonnes GuestEmail/GuestName absentes  appliquer: dotnet ef database update 20260614_AddGuestOrderSupport
                _logger.LogWarning("activities/recent: Orders query failed (migration 20260614 likely missing). Falling back to users only. Exception: {Msg}", exOrders.Message);
                orders = await _db.Orders
                    .Where(o => !o.IsDeleted)
                    .OrderByDescending(o => o.CreatedAt)
                    .Take(limit / 2)
                    .Select(o => new AdminActivityEntry(
                        $"order-{o.Id}",
                        "order",
                        o.CreatedAt,
                        $"Commande #{o.OrderNumber}",
                        "",
                        o.Status.ToLower() == "completed" ? "success" : o.Status.ToLower() == "failed" ? "failure" : "warning",
                        "Anonyme",
                        "",
                        $"Commande #{o.OrderNumber} · {o.TotalAmount:N0} XAF"
                    ))
                    .ToListAsync();
            }

            var users = await _db.Users
                .Where(u => !u.IsDeleted)
                .OrderByDescending(u => u.CreatedAt)
                .Take(limit / 2)
                .Select(u => new AdminActivityEntry(
                    $"user-{u.Id}",
                    "user",
                    u.CreatedAt,
                    "Nouvel utilisateur inscrit",
                    u.Email,
                    "success",
                    (u.FirstName + " " + u.LastName).Trim() == "" ? u.Email : (u.FirstName + " " + u.LastName).Trim(),
                    u.Email,
                    $"Inscription · rôle {u.Role}"
                ))
                .ToListAsync();

            var merged = orders
                .Concat(users)
                .OrderByDescending(a => a.Timestamp)
                .Take(limit)
                .Select(a => new
                {
                    id        = a.Id,
                    type      = a.Type,
                    action    = a.Type,
                    title     = a.Title,
                    description = a.Description,
                    timestamp = a.Timestamp,
                    status    = a.Status,
                    userName  = a.UserName,
                    userEmail = a.UserEmail,
                    target    = a.Target,
                });

            return Ok(new { data = merged, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent activities");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Santé système (lot 9.7a, suivi 18.2) : uniquement des mesures réelles.
    /// Chaque service est sondé par une vraie requête chronométrée (base :
    /// SELECT 1 ; WinAI : /health ; e-mails : API Resend authentifiée ;
    /// stockage : liste d'un objet du bucket S3). Mémoire et processeur sont
    /// ceux du processus de l'API, la charge celle de la machine (Linux,
    /// /proc/loadavg). Une mesure impossible est renvoyée à null et l'écran
    /// ne l'affiche pas ; plus aucune valeur n'est inventée (l'ancienne
    /// version renvoyait une charge de 20 %, 95 ms de réponse, un S3 toujours
    /// « up » et des disponibilités calculées).
    /// </summary>
    [HttpGet("system/health")]
    public async Task<IActionResult> GetSystemHealth(CancellationToken ct)
    {
        try
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess();

            // Processeur du processus : temps CPU consommé sur 500 ms, rapporté
            // au nombre de cœurs.
            var cpuBefore = proc.TotalProcessorTime;
            var wall = System.Diagnostics.Stopwatch.StartNew();
            await Task.Delay(500, ct);
            proc.Refresh();
            var cpuPercent = Math.Round((proc.TotalProcessorTime - cpuBefore).TotalMilliseconds
                / (wall.Elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100, 1);

            var gcInfo = GC.GetGCMemoryInfo();
            var processMemoryMb = Math.Round(proc.WorkingSet64 / 1024.0 / 1024.0, 0);
            double? machineMemoryMb = gcInfo.TotalAvailableMemoryBytes > 0
                ? Math.Round(gcInfo.TotalAvailableMemoryBytes / 1024.0 / 1024.0, 0)
                : null;

            double[]? loadAverage = null;
            try
            {
                if (System.IO.File.Exists("/proc/loadavg"))
                {
                    var parts = (await System.IO.File.ReadAllTextAsync("/proc/loadavg", ct)).Split(' ');
                    loadAverage = parts.Take(3).Select(p => double.Parse(p, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                }
            }
            catch { loadAverage = null; }

            async Task<object> Probe(string key, string name, Func<Task<(string Status, string? Detail)>> probe)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var (status, detail) = await probe();
                    sw.Stop();
                    return new { key, name, status, responseMs = status == "not_configured" ? (long?)null : sw.ElapsedMilliseconds, detail };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return new { key, name, status = "down", responseMs = (long?)sw.ElapsedMilliseconds, detail = (string?)ex.GetType().Name };
                }
            }

            var timeout = TimeSpan.FromSeconds(5);
            var services = new List<object>
            {
                await Probe("database", "PostgreSQL", async () =>
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(timeout);
                    await _db.Database.ExecuteSqlRawAsync("SELECT 1", cts.Token);
                    return ("up", null);
                }),
                await Probe("winai", "WinAI (FastAPI)", async () =>
                {
                    var client = _httpClientFactory.CreateClient("FastApiClient");
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(timeout);
                    var r = await client.GetAsync("/health", cts.Token);
                    return (r.IsSuccessStatusCode ? "up" : "down", $"HTTP {(int)r.StatusCode}");
                }),
                await Probe("email", "E-mails (Resend)", async () =>
                {
                    var apiKey = _configuration["Resend:ApiKey"];
                    if (string.IsNullOrWhiteSpace(apiKey)) return ("not_configured", "Clé Resend absente");
                    var client = _httpClientFactory.CreateClient("ResendClient");
                    using var req = new HttpRequestMessage(HttpMethod.Get, "/domains");
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(timeout);
                    var r = await client.SendAsync(req, cts.Token);
                    return ((int)r.StatusCode switch
                    {
                        >= 200 and < 300 => "up",
                        401 or 403 => "degraded",
                        _ => "down",
                    }, $"HTTP {(int)r.StatusCode}");
                }),
                await Probe("storage", "Stockage (S3)", async () =>
                {
                    if (string.IsNullOrWhiteSpace(_storage.Bucket)) return ("not_configured", "Aucun bucket configuré");
                    using var s3 = _storage.CreateS3Client();
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(timeout);
                    await s3.ListObjectsV2Async(new Amazon.S3.Model.ListObjectsV2Request { BucketName = _storage.Bucket, MaxKeys = 1 }, cts.Token);
                    return ("up", _storage.Bucket);
                }),
            };

            static string StatusOf(object o) => (string)o.GetType().GetProperty("status")!.GetValue(o)!;
            var dbUp = StatusOf(services[0]) == "up";
            var anyDown = services.Any(s => StatusOf(s) == "down");
            var anyDegraded = services.Any(s => StatusOf(s) == "degraded");
            var overall = !dbUp ? "critical" : anyDown || anyDegraded ? "warning" : "healthy";

            return Ok(new
            {
                success = true,
                data = new
                {
                    status = overall,
                    measuredAt = DateTime.UtcNow,
                    process = new
                    {
                        startedAt = proc.StartTime.ToUniversalTime(),
                        uptimeHours = Math.Round((DateTime.UtcNow - proc.StartTime.ToUniversalTime()).TotalHours, 1),
                        memoryMb = processMemoryMb,
                        managedHeapMb = Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 0),
                        cpuPercent,
                        threads = proc.Threads.Count,
                    },
                    machine = new
                    {
                        processorCount = Environment.ProcessorCount,
                        memoryAvailableMb = machineMemoryMb,
                        loadAverage,
                    },
                    services,
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system health");
            return StatusCode(500, new { success = false, error = "Erreur serveur" });
        }
    }
    /// <summary>
    /// Comptes actifs, nouveaux et attrition sur une fenêtre glissante
    /// (lot 9.7a : calcul SQL ; l'ancienne version chargeait au plus 100
    /// comptes). Actifs : connectés dans les N derniers jours ; attrition :
    /// part des comptes actifs sur la fenêtre précédente qui ne se sont pas
    /// reconnectés sur la fenêtre courante.
    /// </summary>
    [HttpGet("analytics/active-users")]
    public async Task<IActionResult> GetActiveUsers([FromQuery] int days = 30)
    {
        try
        {
            days = Math.Clamp(days, 1, 365);
            var now = DateTime.UtcNow;
            var from = now.AddDays(-days);
            var prevFrom = from.AddDays(-days);
            var users = _db.Users.AsNoTracking().Where(u => !u.IsDeleted && u.Role != "admin");

            var activeUserCount = await users.CountAsync(u => u.LastLoginAt >= from);
            var newUserCount = await users.CountAsync(u => u.CreatedAt >= from);
            // Comptes vus sur la fenêtre précédente (dernière connexion ou session).
            var previousActive = users.Where(u => _db.UserSessions.Any(s => s.UserId == u.Id && s.LastActivityAt >= prevFrom && s.LastActivityAt < from));
            var previousActiveCount = await previousActive.CountAsync();
            var churned = await previousActive.CountAsync(u => !(u.LastLoginAt >= from)
                && !_db.UserSessions.Any(s => s.UserId == u.Id && s.LastActivityAt >= from));
            double? churnRate = previousActiveCount > 0 ? Math.Round(churned * 100.0 / previousActiveCount, 2) : null;

            return Ok(new
            {
                data = new { days, activeUserCount, newUserCount, previousActiveCount, churnedCount = churned, churnRate },
                success = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active users");
            return StatusCode(500, new { success = false, error = "Erreur serveur" });
        }
    }
    /// <summary>
    /// Métriques analytiques clés pour le tableau de bord admin
    /// </summary>
    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics()
    {
        try
        {
            var now = DateTime.UtcNow;
            var today = now.AddDays(-1);
            var sevenDaysAgo = now.AddDays(-7);
            var thirtyDaysAgo = now.AddDays(-30);
            var thisMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var lastMonthStart = thisMonthStart.AddMonths(-1);

            var totalUsers        = await _db.Users.CountAsync(u => !u.IsDeleted);
            var activeUsers       = await _db.Users.CountAsync(u => !u.IsDeleted && u.IsActive && u.LastLoginAt >= thirtyDaysAgo);
            var newUsersToday     = await _db.Users.CountAsync(u => !u.IsDeleted && u.CreatedAt >= today);
            var newUsersThisWeek  = await _db.Users.CountAsync(u => !u.IsDeleted && u.CreatedAt >= sevenDaysAgo);

            var totalSubjects     = await _db.Subjects.CountAsync(s => !s.IsDeleted);
            var publishedSubjects = await _db.Subjects.CountAsync(s => s.IsPublished && !s.IsDeleted);
            var pendingSubjects   = await _db.Subjects.CountAsync(s => !s.IsPublished && !s.IsDeleted);

            var totalOrders       = await _db.Orders.CountAsync(o => !o.IsDeleted);
            // Module 19 : ces compteurs valaient tous zéro. Le code écrit les
            // statuts en minuscules partout et les comparait ici en casse
            // capitalisée, avec une comparaison sensible à la casse en SQL.
            //
            // ⚠ Dette assumée : on corrige la COMPARAISON (insensible à la
            // casse) plutôt que de migrer les données. Le volume réel en base
            // n'a pas pu être mesuré depuis cet environnement, et une
            // migration de statuts touche le calcul des revenus et les accès
            // au contenu : elle doit être décidée et mesurée à part. Tant
            // qu'elle n'a pas eu lieu, toute nouvelle comparaison de statut
            // doit rester insensible à la casse.
            var pendingOrders     = await _db.Orders.CountAsync(o => !o.IsDeleted && o.Status.ToLower() == "pending");
            var completedOrders   = await _db.Orders.CountAsync(o => !o.IsDeleted && o.Status.ToLower() == "completed");

            decimal revenue = 0m, thisMonthRevenue = 0m, lastMonthRevenue = 0m;
            double revenueGrowth = 0.0;
            try
            {
                revenue          = await _db.Payments.Where(p => p.Status == "completed").SumAsync(p => (decimal?)p.Amount) ?? 0m;
                thisMonthRevenue = await _db.Payments.Where(p => p.Status == "completed" && p.CreatedAt >= thisMonthStart).SumAsync(p => (decimal?)p.Amount) ?? 0m;
                lastMonthRevenue = await _db.Payments.Where(p => p.Status == "completed" && p.CreatedAt >= lastMonthStart && p.CreatedAt < thisMonthStart).SumAsync(p => (decimal?)p.Amount) ?? 0m;
                revenueGrowth    = lastMonthRevenue > 0 ? Math.Round((double)((thisMonthRevenue - lastMonthRevenue) / lastMonthRevenue * 100), 1) : 0.0;
            }
            catch (Exception exPay) { _logger.LogWarning("Analytics: Payments query failed  {Msg}", exPay.Message); }

            int subscribedUsers = 0;
            try
            {
                subscribedUsers = await _db.Subscriptions.Where(s => s.Status == "active" && !s.IsDeleted).Select(s => s.UserId).Distinct().CountAsync();
            }
            catch (Exception exSub) { _logger.LogWarning("Analytics: Subscriptions query failed  {Msg}", exSub.Message); }

            // Lot 9.7a (18.1) : même définition que partout ailleurs (AdminMetrics),
            // sur les 30 derniers jours. L'ancienne valeur était la part
            // d'abonnés actifs, affichée sous le nom « taux de conversion ».
            var (_, _, conversion30) = await AdminMetrics.ConversionAsync(_db, thirtyDaysAgo, now);
            var conversionRate = conversion30 ?? 0.0;

            return Ok(new
            {
                success         = true,
                totalUsers,     activeUsers,    newUsersToday,   newUsersThisWeek,
                totalSubjects,  publishedSubjects, pendingSubjects,
                totalOrders,    pendingOrders,  completedOrders,
                revenue,        totalRevenue = revenue, revenueGrowth,
                conversionRate, subscribedUsers, activeSubjects = publishedSubjects,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting analytics");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    // Lot 9.7a : analytics/geo, doublon de la Carte qui inventait des chiffres,
    // est fusionnée avec GET analytics/geographic (comptes par User.City
    // réelle, AdminOverviewAnalyticsController). analytics/revenues,
    // analytics/popular-subjects et analytics/conversion-rate y sont
    // recalculées en base sur la période demandée.

    /// <summary>GET /admin/subjects/pending  Sujets non publiés en attente de validation</summary>
    [HttpGet("subjects/pending")]
    public async Task<IActionResult> GetPendingSubjects(
        [FromQuery] int limit = 50,
        [FromQuery] bool countOnly = false)
    {
        try
        {
            var query = _db.Subjects.Where(s => !s.IsPublished && !s.IsDeleted);

            if (countOnly)
            {
                var count = await query.CountAsync();
                return Ok(new { total = count, count });
            }

            var subjectList = await query
                .OrderByDescending(s => s.CreatedAt)
                .Take(limit)
                .ToListAsync();

            var subjectIds = subjectList.Select(s => s.Id).ToList();

            // Récupère le premier DocumentUrl disponible par sujet
            var docUrls = await _db.CourseContents
                .Where(cc => subjectIds.Contains(cc.SubjectId) && cc.DocumentUrl != null)
                .GroupBy(cc => cc.SubjectId)
                .Select(g => new { SubjectId = g.Key, Url = g.First().DocumentUrl })
                .ToDictionaryAsync(x => x.SubjectId, x => x.Url);

            var authorIds = subjectList.Where(s => s.AuthorUserId.HasValue).Select(s => s.AuthorUserId!.Value).Distinct().ToList();
            var authorNames = await _db.Users.AsNoTracking()
                .Where(u => authorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());

            var subjects = subjectList.Select(s => new
            {
                id           = s.Id,
                title        = s.Title,
                type         = "epreuve",
                subject      = s.Category,
                // Était codé en dur à "Admin" pour tout le monde, quel que
                // soit l'auteur réel (AuthorUserId) : l'admin ne pouvait pas
                // savoir qui avait vraiment soumis un contenu à valider.
                teacherName  = s.AuthorUserId.HasValue && authorNames.TryGetValue(s.AuthorUserId.Value, out var n) && !string.IsNullOrWhiteSpace(n)
                    ? n : "Admin",
                teacherId    = s.AuthorUserId,
                submittedAt  = s.CreatedAt,
                thumbnailUrl = s.ThumbnailUrl,
                documentUrl  = docUrls.TryGetValue(s.Id, out var url) ? url : null,
                price        = s.Price,
                description  = s.Description,
            }).ToList();

            return Ok(new { data = subjects, total = subjects.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending subjects");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>POST /admin/subjects/{id}/approve  Publier un sujet</summary>
    [HttpPost("subjects/{id}/approve")]
    public async Task<IActionResult> ApproveSubject(int id)
    {
        try
        {
            var subject = await _db.Subjects.FindAsync(id);
            if (subject == null) return NotFound(new { error = "Subject not found" });
            subject.IsPublished = true;
            subject.UpdatedAt = DateTime.UtcNow;
            // Garde "Mes contenus" (CourseContent.Status) cohérent avec la
            // décision d'approbation : sinon le badge restait "En révision"
            // à vie même après publication.
            await _db.CourseContents.Where(cc => cc.SubjectId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(cc => cc.Status, "published").SetProperty(cc => cc.UpdatedAt, DateTime.UtcNow));
            this.AuditAdd(_audit, Backend.Models.Entities.AdminAuditActions.ContentApproved, "Subject", id, subject.Title,
                new { isPublished = false }, new { isPublished = true });
            await _db.SaveChangesAsync();
            await NotifyAuthorAsync(subject.AuthorUserId, id, "Contenu publié",
                $"Ton contenu « {subject.Title} » a été validé et est maintenant publié.");
            _logger.LogInformation("Subject {Id} approved by admin", id);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving subject {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>POST /admin/subjects/{id}/reject  Rejeter un sujet</summary>
    [HttpPost("subjects/{id}/reject")]
    public async Task<IActionResult> RejectSubject(int id, [FromBody] RejectSubjectRequest request)
    {
        // Lot 9.7a (18.1) : le motif est obligatoire et envoyé à l'auteur ;
        // il n'était ni exigé ni transmis (seulement écrit dans les journaux).
        var reason = (request?.Reason ?? string.Empty).Trim();
        if (reason.Length < AdminUsersController.MinReasonLength)
            return BadRequest(new { code = "reason_required", error = $"Le motif du rejet est obligatoire ({AdminUsersController.MinReasonLength} caractères au moins) : il est envoyé à l'auteur." });
        if (reason.Length > 1000) reason = reason[..1000];
        try
        {
            var subject = await _db.Subjects.FindAsync(id);
            if (subject == null) return NotFound(new { error = "Contenu introuvable" });
            if (subject.IsPublished || subject.IsDeleted)
                return Conflict(new { error = "Ce contenu n'est plus en attente de validation." });
            subject.IsDeleted = true;
            subject.UpdatedAt = DateTime.UtcNow;
            await _db.CourseContents.Where(cc => cc.SubjectId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(cc => cc.Status, "archived").SetProperty(cc => cc.UpdatedAt, DateTime.UtcNow));
            this.AuditAdd(_audit, Backend.Models.Entities.AdminAuditActions.ContentRejected, "Subject", id, subject.Title,
                new { status = "pending" }, new { status = "rejected" }, reason);
            await _db.SaveChangesAsync();
            await NotifyAuthorAsync(subject.AuthorUserId, id, "Contenu non validé",
                $"Ton contenu « {subject.Title} » n'a pas été validé. Motif : {reason}");
            _logger.LogInformation("Subject {Id} rejected: {Reason}", id, reason);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting subject {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// POST /admin/subjects/bulk-approve  Validation groupée (plusieurs
    /// documents sélectionnés, ou "Tout valider"). Chaque id est re-vérifié
    /// encore en attente juste avant d'être approuvé : un contenu déjà
    /// publié ou rejeté entre-temps (par un autre admin, un autre onglet) ne
    /// peut jamais être re-traité par ce point d'entrée.
    /// </summary>
    [HttpPost("subjects/bulk-approve")]
    public async Task<IActionResult> BulkApproveSubjects([FromBody] BulkApproveRequest request)
    {
        if (request.Ids == null || request.Ids.Count == 0)
            return BadRequest(new { error = "Aucun identifiant fourni." });
        if (request.Ids.Count > 200)
            return BadRequest(new { error = "200 documents maximum par validation groupée." });

        try
        {
            var stillPendingIds = await _db.Subjects
                .Where(s => request.Ids.Contains(s.Id) && !s.IsPublished && !s.IsDeleted)
                .Select(s => s.Id)
                .ToListAsync();

            var now = DateTime.UtcNow;
            await _db.Subjects.Where(s => stillPendingIds.Contains(s.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsPublished, true).SetProperty(x => x.UpdatedAt, now));
            await _db.CourseContents.Where(cc => stillPendingIds.Contains(cc.SubjectId))
                .ExecuteUpdateAsync(s => s.SetProperty(cc => cc.Status, "published").SetProperty(cc => cc.UpdatedAt, now));

            var skipped = request.Ids.Except(stillPendingIds).ToList();
            if (stillPendingIds.Count > 0)
            {
                this.AuditAdd(_audit, Backend.Models.Entities.AdminAuditActions.ContentApproved, "Subject", "lot",
                    $"Validation groupée ({stillPendingIds.Count})", after: new { ids = stillPendingIds });
                await _db.SaveChangesAsync();
            }
            _logger.LogInformation("Validation groupée : {Approved} approuvés, {Skipped} déjà traités/invalides", stillPendingIds.Count, skipped.Count);
            return Ok(new { success = true, approvedIds = stillPendingIds, skippedIds = skipped });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk-approving subjects");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// GET /admin/emails/recipients?target=  Nombre de destinataires d'un envoi
    /// de masse (après désabonnements), affiché dans la confirmation.
    /// </summary>
    [HttpGet("emails/recipients")]
    public async Task<IActionResult> CountEmailRecipients([FromQuery] string target = "all", [FromQuery] string? customEmail = null)
    {
        if (!AdminEmailTargets.All.Contains(target)) return BadRequest(new { error = "Cible inconnue." });
        var (recipients, optedOut) = await _campaigns.CountRecipientsAsync(target, customEmail);
        return Ok(new { success = true, data = new { recipients, optedOut } });
    }

    /// <summary>
    /// POST /admin/emails/send  Met en file un envoi de masse (lot 9.7a, 18.1) :
    /// l'administrateur a confirmé le nombre de destinataires (champ
    /// <c>ExpectedRecipients</c>, comparé au compte actuel) ; l'envoi part en
    /// tâche de fond et son rapport se lit par GET emails/campaigns.
    /// </summary>
    [HttpPost("emails/send")]
    public async Task<IActionResult> SendAdminEmail([FromBody] AdminEmailRequest request)
    {
        if (!AdminEmailTargets.All.Contains(request.Target ?? ""))
            return BadRequest(new { error = "Cible inconnue." });
        if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 300)
            return BadRequest(new { error = "Objet requis (300 caractères au maximum)." });
        if (string.IsNullOrWhiteSpace(request.Body))
            return BadRequest(new { error = "Message requis." });
        if (request.Target == "custom" && string.IsNullOrWhiteSpace(request.CustomEmail))
            return BadRequest(new { error = "Adresse e-mail requise pour la cible « adresse précise »." });

        try
        {
            var (recipients, _) = await _campaigns.CountRecipientsAsync(request.Target!, request.CustomEmail);
            if (request.Target != "custom" && request.ExpectedRecipients != recipients)
                return Conflict(new
                {
                    code = "recipients_changed",
                    error = $"Le nombre de destinataires a changé ({recipients}). Confirmez à nouveau.",
                    recipients,
                });

            var campaign = await _campaigns.QueueAsync(request.Target!, request.CustomEmail, request.Subject, request.Body, User.GetUserId());
            await this.AuditAsync(_audit, Backend.Models.Entities.AdminAuditActions.MassEmailQueued, "EmailCampaign", campaign.Id,
                campaign.Subject, after: new { campaign.Target, campaign.RecipientCount, campaign.OptedOutCount });

            return Ok(new
            {
                success = true,
                data = CampaignDto(campaign),
                message = campaign.RecipientCount == 0
                    ? "Aucun destinataire : rien à envoyer."
                    : $"Envoi programmé à {campaign.RecipientCount} destinataire(s) : il se poursuit en arrière-plan.",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error queueing admin email");
            return StatusCode(500, new { error = "Erreur serveur" });
        }
    }

    /// <summary>GET /admin/emails/campaigns  Envois de masse et leur rapport (paginé).</summary>
    [HttpGet("emails/campaigns")]
    public async Task<IActionResult> GetEmailCampaigns([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await _db.AdminEmailCampaigns.CountAsync();
        var rows = await _db.AdminEmailCampaigns.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new { success = true, data = new { items = rows.Select(CampaignDto).ToList(), total, page, pageSize } });
    }

    private static object CampaignDto(Backend.Models.Entities.AdminEmailCampaign c) => new
    {
        id = c.Id, target = c.Target, subject = c.Subject, status = c.Status,
        recipients = c.RecipientCount, sent = c.SentCount, failed = c.FailedCount, optedOut = c.OptedOutCount,
        error = c.Error, createdAt = c.CreatedAt, startedAt = c.StartedAt, finishedAt = c.FinishedAt,
    };
    // ─── WinAI Admin ────────────────────────────────────────────────────────────

    /// <summary>GET /admin/winai/stats  Statistiques WinAI agrégées depuis les tables de chat</summary>
    [HttpGet("winai/stats")]
    public async Task<IActionResult> GetWinAIStats()
    {
        try
        {
            var totalConversations  = await _db.Conversations.CountAsync(c => !c.IsDeleted);
            var activeConversations = await _db.Conversations.CountAsync(c => !c.IsDeleted && c.IsActive);
            var totalMessages       = await _db.Messages.CountAsync(m => !m.IsDeleted);
            var avgMsgPerConv       = totalConversations > 0 ? Math.Round((double)totalMessages / totalConversations, 1) : 0.0;
            var last30 = DateTime.UtcNow.AddDays(-30);
            var recentConversations = await _db.Conversations.CountAsync(c => !c.IsDeleted && c.CreatedAt >= last30);
            var avgTokens    = await _db.Messages.Where(m => !m.IsDeleted && m.TokensUsed.HasValue).AverageAsync(m => (double?)m.TokensUsed) ?? 0;
            var avgRespTime  = await _db.Messages.Where(m => !m.IsDeleted && m.GenerationTimeMs.HasValue).AverageAsync(m => (double?)m.GenerationTimeMs) ?? 0;
            var positiveRatings = await _db.Messages.CountAsync(m => !m.IsDeleted && m.FeedbackRating == 1);
            var ratedMessages   = await _db.Messages.CountAsync(m => !m.IsDeleted && m.FeedbackRating.HasValue);
            var satisfaction    = ratedMessages > 0 ? Math.Round((double)positiveRatings / ratedMessages * 100, 1) : 0.0;

            return Ok(new
            {
                success = true,
                data = new
                {
                    totalConversations, activeConversations, totalMessages,
                    avgMessagesPerConversation = avgMsgPerConv,
                    recentConversations,
                    avgTokensUsed    = (int)Math.Round(avgTokens),
                    avgResponseTimeMs = (int)Math.Round(avgRespTime),
                    satisfactionRate = satisfaction,
                    status    = "operational",
                    modelName = "WinAI"
                }
            });
        }
        catch (PostgresException pg) when (pg.SqlState == PostgresErrorCodes.UndefinedTable
                                        || pg.SqlState == PostgresErrorCodes.UndefinedColumn)
        {
            // Les tables de chat ont ete creees a la main, sans le schema complet
            // des entites : des colonnes declarees dans Models/Entities (dont
            // Messages.GenerationTimeMs) sont absentes en base, et EF genere du
            // SQL qui les reference -> 42703.
            // Correctif de la cause : Migrations/SQL_FixChatbotColumns.sql
            //
            // Ici, on evite le 500 : un module d'analytics degrade ne doit pas
            // faire remonter une erreur dans la console d'administration alors
            // que le reste de la plateforme fonctionne. Charge neutre, marquee
            // indisponible, et la cause reste dans les logs.
            _logger.LogWarning(
                "WinAI stats indisponible: schema de chat incomplet ({SqlState} {Message}). Appliquer Migrations/SQL_FixChatbotColumns.sql.",
                pg.SqlState, pg.MessageText);

            return Ok(new
            {
                success = true,
                data = new
                {
                    totalConversations = 0,
                    activeConversations = 0,
                    totalMessages = 0,
                    avgMessagesPerConversation = 0.0,
                    recentConversations = 0,
                    avgTokensUsed = 0,
                    avgResponseTimeMs = 0,
                    satisfactionRate = 0.0,
                    status = "unavailable",
                    reason = "chat_schema_missing",
                    modelName = "WinAI"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting WinAI stats");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ─── Chat Admin ──────────────────────────────────────────────────────────────

    /// <summary>GET /admin/chat/sessions  Liste paginée des sessions de chat</summary>
    [HttpGet("chat/sessions")]
    public async Task<IActionResult> GetChatSessions([FromQuery] int limit = 100, [FromQuery] int page = 1)
    {
        try
        {
            if (limit < 1) limit = 100;
            if (limit > 200) limit = 200;
            if (page  < 1) page  = 1;

            var total = await _db.Conversations.CountAsync(c => !c.IsDeleted);
            var sessions = await _db.Conversations
                .Where(c => !c.IsDeleted)
                .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
                .Skip((page - 1) * limit)
                .Take(limit)
                .Select(c => new
                {
                    id            = c.Id,
                    userId        = c.UserId,
                    userName      = (c.User.FirstName + " " + c.User.LastName).Trim() != "" ? (c.User.FirstName + " " + c.User.LastName).Trim() : c.User.Email,
                    userEmail     = c.User.Email,
                    title         = c.Title,
                    messageCount  = c.MessageCount,
                    isActive      = c.IsActive,
                    lastMessageAt = c.LastMessageAt,
                    createdAt     = c.CreatedAt,
                })
                .ToListAsync();

            return Ok(new { success = true, data = sessions, total, page, limit, totalPages = (int)Math.Ceiling(total / (double)limit) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting chat sessions");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>GET /admin/chat/stats  Statistiques globales des sessions de chat</summary>
    [HttpGet("chat/stats")]
    public async Task<IActionResult> GetChatStats()
    {
        try
        {
            var total    = await _db.Conversations.CountAsync(c => !c.IsDeleted);
            var active   = await _db.Conversations.CountAsync(c => !c.IsDeleted && c.IsActive);
            var closed   = await _db.Conversations.CountAsync(c => !c.IsDeleted && !c.IsActive);
            var msgs     = await _db.Messages.CountAsync(m => !m.IsDeleted);
            var last30   = DateTime.UtcNow.AddDays(-30);
            var recentSessions = await _db.Conversations.CountAsync(c => !c.IsDeleted && c.CreatedAt >= last30);
            var deletedMsgs    = await _db.Messages.CountAsync(m => m.IsDeleted);

            return Ok(new
            {
                success = true,
                data = new
                {
                    totalSessions = total, activeSessions = active, closedSessions = closed,
                    totalMessages = msgs, recentSessions, deletedMessages = deletedMsgs,
                    avgMessagesPerSession = total > 0 ? Math.Round((double)msgs / total, 1) : 0.0
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting chat stats");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// GET /admin/chat/sessions/{id}/messages?reason=  Messages d'une
    /// conversation WinAI d'un utilisateur. Lot 9.7a (suivi 18.4) : la
    /// consultation exige un motif saisi par l'administrateur, inscrit au
    /// journal d'audit avec la conversation et son propriétaire.
    /// </summary>
    [HttpGet("chat/sessions/{id}/messages")]
    public async Task<IActionResult> GetSessionMessages(int id, [FromQuery] string? reason = null)
    {
        if (!HasReason(reason)) return ReasonRequired();
        try
        {
            var conv = await _db.Conversations.AsNoTracking()
                .Where(c => c.Id == id && !c.IsDeleted)
                .Select(c => new { c.Id, c.UserId, c.Title, OwnerEmail = c.User.Email })
                .FirstOrDefaultAsync();
            if (conv == null) return NotFound(new { error = "Conversation introuvable" });
            await this.AuditAsync(_audit, Backend.Models.Entities.AdminAuditActions.WinAiConversationViewed, "Conversation", id,
                $"{conv.Title} ({conv.OwnerEmail})", after: new { ownerUserId = conv.UserId }, reason: reason);

            var messages = await _db.Messages
                .Where(m => m.ConversationId == id && !m.IsDeleted)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    id               = m.Id,
                    role             = m.Role,
                    content          = m.Content,
                    tokensUsed       = m.TokensUsed,
                    feedbackRating   = m.FeedbackRating,
                    feedbackComment  = m.FeedbackComment,
                    generationTimeMs = m.GenerationTimeMs,
                    createdAt        = m.CreatedAt,
                })
                .ToListAsync();

            return Ok(new { success = true, data = messages, sessionId = id, total = messages.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session messages for {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>PATCH /admin/chat/sessions/{id}/close  Ferme une session de chat</summary>
    [HttpPatch("chat/sessions/{id}/close")]
    public async Task<IActionResult> CloseSession(int id)
    {
        try
        {
            var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
            if (conv == null) return NotFound(new { error = "Session not found" });
            conv.IsActive  = false;
            conv.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing session {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>DELETE /admin/chat/messages/{id}  Supprime (soft-delete) un message</summary>
    [HttpDelete("chat/messages/{id}")]
    public async Task<IActionResult> DeleteChatMessage(int id, [FromQuery] string? reason = null)
    {
        if (!HasReason(reason)) return ReasonRequired();
        try
        {
            var msg = await _db.Messages.FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted);
            if (msg == null) return NotFound(new { error = "Message introuvable" });
            msg.IsDeleted = true;
            this.AuditAdd(_audit, Backend.Models.Entities.AdminAuditActions.WinAiMessageDeleted, "Message", id,
                after: new { conversationId = msg.ConversationId }, reason: reason);
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting message {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ─── Subscriptions Admin ─────────────────────────────────────────────────────

    // Lot 9.7b : GET /admin/subscriptions/plans et /admin/subscriptions/stats
    // retirés, purs doublons de /admin/plans et /admin/plans/stats
    // (AdminPlansController, versions, prix par période, abonnés par version).

    // ─── Certificates Admin ──────────────────────────────────────────────────────

    /// <summary>GET /admin/certificates  Liste des certificats avec infos utilisateur et cours</summary>
    [HttpGet("certificates")]
    public async Task<IActionResult> GetCertificates([FromQuery] int page = 1, [FromQuery] int limit = 50, [FromQuery] string? q = null)
    {
        try
        {
            if (page  < 1) page  = 1;
            if (limit < 1) limit = 50;
            if (limit > 100) limit = 100;

            // Lot 9.7a : recherche serveur (élève, e-mail, numéro, contenu) ;
            // la pagination existait côté serveur mais l'écran n'en lisait que la page 1.
            var query = _db.Certificates.AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(c => c.CertificateNumber.ToLower().Contains(term)
                    || c.User.Email.ToLower().Contains(term)
                    || (c.User.FirstName + " " + c.User.LastName).ToLower().Contains(term)
                    || c.Subject.Title.ToLower().Contains(term));
            }

            var total = await query.CountAsync();
            var certs = await query
                .OrderByDescending(c => c.IssuedAt)
                .Skip((page - 1) * limit)
                .Take(limit)
                .Select(c => new
                {
                    id                = c.Id,
                    certificateNumber = c.CertificateNumber,
                    userName          = (c.User.FirstName + " " + c.User.LastName).Trim() != "" ? (c.User.FirstName + " " + c.User.LastName).Trim() : c.User.Email,
                    userEmail         = c.User.Email,
                    subjectTitle      = c.Subject.Title,
                    grade             = c.Grade,
                    issuedAt          = c.IssuedAt,
                    completionDate    = c.CompletionDate,
                    fileUrl           = c.FileUrl,
                })
                .ToListAsync();

            return Ok(new { success = true, data = certs, total, page, limit, totalPages = (int)Math.Ceiling(total / (double)limit) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting certificates");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ─── Application Logs Admin ──────────────────────────────────────────────────

    /// <summary>GET /admin/logs  Logs d'erreurs applicatifs paginés</summary>
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs([FromQuery] int page = 1, [FromQuery] int limit = 50, [FromQuery] string? level = null,
        [FromQuery] string? q = null, [FromQuery] bool? resolved = null)
    {
        try
        {
            if (page  < 1) page  = 1;
            if (limit < 1) limit = 50;
            if (limit > 200) limit = 200;

            // Lot 9.7a : filtres appliqués en base (niveau sans tenir compte de
            // la casse, texte, état résolu) au lieu de filtrer 200 lignes à l'écran.
            var query = _db.ApplicationLogs.AsQueryable();
            if (!string.IsNullOrWhiteSpace(level) && level != "all")
            {
                var lv = level.Trim().ToLower();
                query = lv == "warn" || lv == "warning"
                    ? query.Where(l => l.Level.ToLower() == "warning" || l.Level.ToLower() == "warn")
                    : query.Where(l => l.Level.ToLower() == lv);
            }
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(l => l.Message.ToLower().Contains(term) || l.Category.ToLower().Contains(term)
                    || (l.RequestPath != null && l.RequestPath.ToLower().Contains(term)));
            }
            if (resolved.HasValue)
                query = query.Where(l => l.IsResolved == resolved.Value);

            var total = await query.CountAsync();
            var logs  = await query
                .OrderByDescending(l => l.CreatedAt)
                .Skip((page - 1) * limit)
                .Take(limit)
                .Select(l => new
                {
                    id          = l.Id,
                    level       = l.Level,
                    category    = l.Category,
                    message     = l.Message,
                    requestPath = l.RequestPath,
                    userId      = l.UserId,
                    isResolved  = l.IsResolved,
                    resolvedAt  = l.ResolvedAt,
                    createdAt   = l.CreatedAt,
                })
                .ToListAsync();

            return Ok(new { success = true, data = logs, total, page, limit, totalPages = (int)Math.Ceiling(total / (double)limit) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting logs");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>POST /admin/chat/sessions/{id}/messages  Envoyer un message admin dans une session ouverte</summary>
    [HttpPost("chat/sessions/{id}/messages")]
    public async Task<IActionResult> SendSessionMessage(int id, [FromBody] AdminChatMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Content) || request.Content.Length > 2000)
            return BadRequest(new { error = "Le contenu doit comporter entre 1 et 2000 caractères." });

        try
        {
            var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
            if (conv == null) return NotFound(new { error = "Session introuvable." });
            if (!conv.IsActive)
                return Conflict(new { error = "Cette session est clôturée et ne peut plus recevoir de messages." });

            var adminId    = User.GetUserId();
            var admin      = await _db.Users.FindAsync(adminId);
            var senderName = admin != null
                ? ((admin.FirstName + " " + admin.LastName).Trim() is { Length: > 0 } n ? n : admin.Email)
                : "Admin";

            var msg = new Backend.Models.Entities.Message
            {
                ConversationId = id,
                Role           = "admin",
                Content        = request.Content.Trim(),
                CreatedAt      = DateTime.UtcNow,
            };
            _db.Messages.Add(msg);
            conv.MessageCount  = conv.MessageCount + 1;
            conv.LastMessageAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Admin {AdminId} sent message to session {SessionId}", adminId, id);
            return Ok(new
            {
                id         = msg.Id,
                sessionId  = id,
                content    = msg.Content,
                role       = msg.Role,
                senderName,
                createdAt  = msg.CreatedAt,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending admin message to session {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>PATCH /admin/logs/{id}/resolve  Marque un log comme résolu</summary>
    [HttpPatch("logs/{id}/resolve")]
    public async Task<IActionResult> ResolveLog(int id)
    {
        try
        {
            var log = await _db.ApplicationLogs.FindAsync(id);
            if (log == null) return NotFound(new { error = "Log not found" });
            log.IsResolved = true;
            log.ResolvedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving log {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>POST /admin/subjects/{id}/pdf  Upload du PDF d'un sujet vers S3</summary>
    [HttpPost("subjects/{id}/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    // Meme correction que AdminUploadsController.Direct : sans [FromForm], le
    // fichier est cherche dans un corps JSON et la requete multipart est
    // refusee en 415.
    public async Task<IActionResult> UploadSubjectPdf(int id, [FromForm] IFormFile file)
    {
        const long MaxPdfSize = 50L * 1024 * 1024;

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Fichier requis" });

        if (file.Length > MaxPdfSize)
            return BadRequest(new { error = "Le fichier dépasse la limite de 50 Mo" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var mime = file.ContentType?.ToLower();
        if (ext != ".pdf" || (mime != "application/pdf" && mime != "application/x-pdf"))
            return BadRequest(new { error = "Seuls les fichiers PDF sont acceptés" });

        var exam = await _db.Exams.FirstOrDefaultAsync(e => e.SubjectId == id && !e.IsDeleted);
        if (exam == null)
            return NotFound(new { error = "Aucun examen trouvé pour ce sujet" });

        try
        {
            var key = $"exams/subject_{id}_{Guid.NewGuid():N}.pdf";
            await using var stream = file.OpenReadStream();
            var url = await _storage.PutAsync(stream, key, "application/pdf");

            exam.DocumentUrl = url;
            await _db.SaveChangesAsync();

            _fastApi.QueueRagIngestion(
                docId: $"examdoc_{exam.Id}",
                title: exam.Title,
                fileUrl: url,
                authorizationHeader: Request.Headers["Authorization"].ToString(),
                category: exam.Category,
                subjectId: exam.SubjectId);

            _logger.LogInformation("PDF uploaded for subject {SubjectId}: {Url}", id, url);
            await this.AuditAsync(_audit, Backend.Models.Entities.AdminAuditActions.ExamPdfReplaced, "Subject", id, exam.Title,
                after: new { examId = exam.Id });
            return Ok(new { data = new { examId = exam.Id, documentUrl = url }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading PDF for subject {SubjectId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── WinAI proxy endpoints ─────────────────────────────────────────────────

    [HttpPost("winai/anomaly-detection")]
    public async Task<IActionResult> AnomalyDetection([FromBody] object? body = null)
    {
        try
        {
            var c = PyClient(); ForwardAuth(c);
            var res = await c.PostAsJsonAsync("/api/admin/anomaly-detection", body ?? new { force_refresh = false });
            return await RelayAsync(res);
        }
        catch (Exception ex) { _logger.LogError(ex, "AnomalyDetection proxy error"); return StatusCode(502, new { error = "IA unavailable" }); }
    }

    [HttpGet("winai/anomalies/active")]
    public async Task<IActionResult> GetActiveAnomalies()
    {
        try
        {
            var c = PyClient(); ForwardAuth(c);
            var res = await c.GetAsync("/api/admin/anomalies/active");
            return await RelayAsync(res);
        }
        catch (Exception ex) { _logger.LogError(ex, "GetActiveAnomalies proxy error"); return StatusCode(502, new { error = "IA unavailable" }); }
    }

    [HttpPost("winai/anomaly/{id}/resolve")]
    public async Task<IActionResult> ResolveAnomaly(int id, [FromQuery] string resolution)
    {
        try
        {
            var c = PyClient(); ForwardAuth(c);
            var res = await c.PostAsync($"/api/admin/anomaly/{id}/resolve?resolution={Uri.EscapeDataString(resolution)}", null);
            return await RelayAsync(res);
        }
        catch (Exception ex) { _logger.LogError(ex, "ResolveAnomaly proxy error"); return StatusCode(502, new { error = "IA unavailable" }); }
    }

    [HttpPost("winai/content-health-check")]
    public async Task<IActionResult> ContentHealthCheck([FromBody] object body)
    {
        try
        {
            var c = PyClient(); ForwardAuth(c);
            var res = await c.PostAsJsonAsync("/api/admin/content-health-check", body);
            return await RelayAsync(res);
        }
        catch (Exception ex) { _logger.LogError(ex, "ContentHealthCheck proxy error"); return StatusCode(502, new { error = "IA unavailable" }); }
    }

    [HttpPost("winai/growth-insights")]
    public async Task<IActionResult> GrowthInsights([FromBody] object? body = null)
    {
        try
        {
            var c = PyClient(); ForwardAuth(c);
            c.Timeout = TimeSpan.FromSeconds(60);
            var res = await c.PostAsJsonAsync("/api/admin/growth-insights", body ?? new { period_days = 7 });
            return await RelayAsync(res);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("GrowthInsights timeout (FastAPI > 60s)");
            return StatusCode(504, new { error = "La génération d'insights a pris trop de temps. Réessayez dans quelques instants." });
        }
        catch (Exception ex) { _logger.LogError(ex, "GrowthInsights proxy error"); return StatusCode(502, new { error = "IA unavailable" }); }
    }

    [HttpPost("winai/revenue-forecast")]
    public async Task<IActionResult> RevenueForecast([FromBody] object? body = null)
    {
        try
        {
            var c = PyClient(); ForwardAuth(c);
            c.Timeout = TimeSpan.FromSeconds(60);
            var res = await c.PostAsJsonAsync("/api/admin/revenue-forecast", body ?? new { currency = "XAF" });
            return await RelayAsync(res);
        }
        catch (Exception ex) { _logger.LogError(ex, "RevenueForecast proxy error"); return StatusCode(502, new { error = "IA unavailable" }); }
    }

    [HttpGet("winai/moderation/pending")]
    public async Task<IActionResult> GetPendingModeration()
    {
        try
        {
            var c = PyClient(); ForwardAuth(c);
            var res = await c.GetAsync("/api/admin/moderation/pending");
            return await RelayAsync(res);
        }
        catch (Exception ex) { _logger.LogError(ex, "GetPendingModeration proxy error"); return StatusCode(502, new { error = "IA unavailable" }); }
    }

    /// <summary>
    /// Décision sur un message du forum retenu par la modération WinAI.
    /// Lot 9.7a (18.1) : la décision s'applique au forum (elle ne faisait que
    /// changer le statut de la file Python) : « approve » rend le message
    /// visible, « reject » le masque (ForumPost.IsHidden). Journalisée.
    /// </summary>
    [HttpPost("winai/moderation/{id}/resolve")]
    public async Task<IActionResult> ResolveModeration(int id, [FromQuery] string action, [FromQuery] string? note = null)
    {
        if (action is not ("approve" or "reject"))
            return BadRequest(new { error = "Action attendue : approve ou reject." });
        try
        {
            var c = PyClient(); ForwardAuth(c);
            var qs = $"action={Uri.EscapeDataString(action)}{(note != null ? $"&note={Uri.EscapeDataString(note)}" : "")}";
            var res = await c.PostAsync($"/api/admin/moderation/{id}/resolve?{qs}", null);
            if (!res.IsSuccessStatusCode) return await RelayAsync(res);

            var json = await res.Content.ReadAsStringAsync();
            int? postId = null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("post_id", out var p) && p.TryGetInt32(out var pid)) postId = pid;
            }
            catch (System.Text.Json.JsonException) { }

            var hidden = action == "reject";
            if (postId.HasValue)
            {
                var post = await _db.ForumPosts.IgnoreQueryFilters().FirstOrDefaultAsync(fp => fp.Id == postId.Value);
                if (post != null)
                {
                    post.IsHidden = hidden;
                    await _db.SaveChangesAsync();
                }
            }

            await this.AuditAsync(_audit, Backend.Models.Entities.AdminAuditActions.ForumModerationResolved, "ForumPost", postId ?? id,
                after: new { action, isHidden = hidden, moderationId = id }, reason: note);
            return Ok(new { success = true, post_id = postId, action, isHidden = hidden });
        }
        catch (Exception ex) { _logger.LogError(ex, "ResolveModeration proxy error"); return StatusCode(502, new { error = "WinAI indisponible" }); }
    }
    // ── Support & messages ───────────────────────────────────────────────────

    /// <summary>GET /admin/support/threads  Fils de discussion support (messages DirectMessage vers/depuis les admins)</summary>
    [HttpGet("support/threads")]
    public async Task<IActionResult> GetSupportThreads()
    {
        try
        {
            var adminIds = await _db.Users
                .Where(u => u.Role == "admin" && !u.IsDeleted)
                .Select(u => u.Id)
                .ToListAsync();

            var userIds = await _db.DirectMessages
                .Where(m => adminIds.Contains(m.ToUserId) || adminIds.Contains(m.FromUserId))
                .Select(m => adminIds.Contains(m.ToUserId) ? m.FromUserId : m.ToUserId)
                .Distinct()
                .ToListAsync();

            // Exclure les autres admins
            userIds = userIds.Where(id => !adminIds.Contains(id)).ToList();

            var threads = new List<object>();
            foreach (var uid in userIds)
            {
                var user = await _db.Users.FindAsync(uid);
                if (user == null || user.IsDeleted) continue;

                var lastMsg = await _db.DirectMessages
                    .Where(m => (adminIds.Contains(m.ToUserId) && m.FromUserId == uid) ||
                                (adminIds.Contains(m.FromUserId) && m.ToUserId == uid))
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => new { m.Content, m.CreatedAt })
                    .FirstOrDefaultAsync();

                var unread = await _db.DirectMessages
                    .CountAsync(m => adminIds.Contains(m.ToUserId) && m.FromUserId == uid && !m.IsRead);

                var name = $"{user.FirstName} {user.LastName}".Trim();
                threads.Add(new
                {
                    userId       = user.Id,
                    userName     = name.Length > 0 ? name : user.Email,
                    userEmail    = user.Email,
                    userRole     = user.Role,
                    avatarUrl    = user.AvatarUrl,
                    lastMessage  = lastMsg?.Content,
                    lastAt       = lastMsg?.CreatedAt,
                    unread,
                });
            }

            var sorted = threads
                .OrderByDescending(t => (DateTime?)((dynamic)t).lastAt ?? DateTime.MinValue)
                .ToList();

            return Ok(new { success = true, data = sorted, total = sorted.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting support threads");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>GET /admin/support/threads/{userId}/messages  Messages d'un fil support</summary>
    [HttpGet("support/threads/{userId:int}/messages")]
    public async Task<IActionResult> GetSupportThreadMessages(int userId)
    {
        try
        {
            var adminIds = await _db.Users
                .Where(u => u.Role == "admin" && !u.IsDeleted)
                .Select(u => u.Id)
                .ToListAsync();

            var messages = await _db.DirectMessages
                .Where(m => (adminIds.Contains(m.ToUserId) && m.FromUserId == userId) ||
                            (adminIds.Contains(m.FromUserId) && m.ToUserId == userId))
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    id          = m.Id,
                    content     = m.Content,
                    isFromAdmin = adminIds.Contains(m.FromUserId),
                    sentAt      = m.CreatedAt,
                    isRead      = m.IsRead,
                })
                .ToListAsync();

            // Marquer comme lus
            var unread = await _db.DirectMessages
                .Where(m => adminIds.Contains(m.ToUserId) && m.FromUserId == userId && !m.IsRead)
                .ToListAsync();
            foreach (var m in unread) { m.IsRead = true; m.ReadAt = DateTime.UtcNow; }
            if (unread.Count > 0) await _db.SaveChangesAsync();

            return Ok(new { success = true, data = messages });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting support thread messages for {UserId}", userId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>POST /admin/support/threads/{userId}/reply  Répondre à un utilisateur</summary>
    [HttpPost("support/threads/{userId:int}/reply")]
    public async Task<IActionResult> ReplySupportThread(int userId, [FromBody] SendSupportReply req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Content))
                return BadRequest(new { error = "Contenu requis" });

            var adminIdStr = User.FindFirst("userId")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(adminIdStr, out var adminId))
                return Unauthorized();

            var user = await _db.Users.FindAsync(userId);
            if (user == null || user.IsDeleted) return NotFound(new { error = "Utilisateur introuvable" });

            var msg = new Backend.Models.Entities.DirectMessage
            {
                FromUserId = adminId,
                ToUserId   = userId,
                Content    = req.Content,
            };
            _db.DirectMessages.Add(msg);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Admin {AdminId} replied to user {UserId} in support thread", adminId, userId);
            return Ok(new { success = true, id = msg.Id, sentAt = msg.CreatedAt });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replying to support thread {UserId}", userId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── Teachers ────────────────────────────────────────────────────────────
    // Lot 9.7a (18.8) : PATCH teachers/{id}/verify est retirée avec le bouton
    // « Vérifier » de la liste des professeurs : elle ne faisait que marquer
    // l'e-mail vérifié (pur doublon de POST users/{id}/verify-email, toujours
    // utilisée par la fiche du compte). La vraie vérification est celle du 9.5.

    // ── Vérification diplôme Répétiteur (Module 1, US-PRO-03, Workflow 1 étape 5) ──

    /// <summary>
    /// File de vérification des diplômes (status=pending, défaut) ou
    /// historique des diplômes traités (status=processed), paginé.
    /// </summary>
    [HttpGet("tutor-verification-documents")]
    public async Task<IActionResult> GetPendingTutorVerificationDocuments([FromQuery] string status = "pending",
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.TutorVerificationDocuments.AsNoTracking().AsQueryable();
        query = status == "processed" ? query.Where(d => d.Status != "pending") : query.Where(d => d.Status == "pending");
        var ordered = status == "processed" ? query.OrderByDescending(d => d.ReviewedAt) : query.OrderBy(d => d.SubmittedAt);
        var docs = await ordered
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new
            {
                d.Id,
                d.DocumentUrl,
                d.SubmittedAt,
                d.Status,
                d.RejectionReason,
                d.ReviewedAt,
                reviewedBy = _db.Users.Where(u => u.Id == d.ReviewedByUserId).Select(u => u.Email).FirstOrDefault(),
                userId = d.TutorProfile!.UserId,
                userName = (d.TutorProfile.User!.FirstName + " " + d.TutorProfile.User.LastName).Trim(),
                userEmail = d.TutorProfile.User.Email,
            })
            .ToListAsync();
        // La file reste un tableau (forme lue par le badge de la console) ;
        // l'historique est paginé : { items, total, page, pageSize }.
        if (status == "processed")
            return Ok(new { items = docs, total = await query.CountAsync(), page, pageSize });
        return Ok(docs);
    }

    /// <summary>Approuve un diplôme : le badge "Vérifié Diplôme" s'affiche immédiatement sur le profil public.</summary>
    [HttpPost("tutor-verification-documents/{id:int}/approve")]
    public async Task<IActionResult> ApproveTutorVerificationDocument(int id)
    {
        var doc = await _db.TutorVerificationDocuments
            .Include(d => d.TutorProfile!).ThenInclude(p => p!.User)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound(new { error = "Document introuvable" });
        // Lot 9.7a : le statut est revérifié (un document déjà traité, par un
        // autre administrateur ou un double clic, n'est jamais retraité).
        if (doc.Status != "pending")
            return Conflict(new { error = "Ce document a déjà été traité." });

        doc.Status = "approved";
        doc.ReviewedAt = DateTime.UtcNow;
        doc.ReviewedByUserId = User.GetUserId();
        if (doc.TutorProfile != null)
            doc.TutorProfile.IsDiplomaVerified = true;
        this.AuditAdd(_audit, Backend.Models.Entities.AdminAuditActions.TutorDiplomaApproved, "TutorVerificationDocument", id,
            doc.TutorProfile?.User?.Email, new { status = "pending" }, new { status = "approved" });
        await _db.SaveChangesAsync();

        var email = doc.TutorProfile?.User?.Email;
        if (!string.IsNullOrEmpty(email))
        {
            _ = _email.SendGenericEmailAsync(email, "Diplôme vérifié · WinPlus",
                "<p>Bonne nouvelle : ton diplôme a été validé par l'équipe WinPlus. " +
                "Le badge « Vérifié Diplôme » est maintenant visible sur ton profil répétiteur.</p>",
                NotificationCategory.Transactional);
        }

        _logger.LogInformation("Admin {AdminId} a approuvé le document de vérification {DocId}", User.GetUserId(), id);
        return Ok(new { message = "Diplôme approuvé", verified = true });
    }

    /// <summary>Rejette un diplôme avec un motif ; le professeur peut redéposer un nouveau document.</summary>
    [HttpPost("tutor-verification-documents/{id:int}/reject")]
    public async Task<IActionResult> RejectTutorVerificationDocument(int id, [FromBody] RejectSubjectRequest request)
    {
        var reason = (request?.Reason ?? string.Empty).Trim();
        if (reason.Length < AdminUsersController.MinReasonLength) return ReasonRequired();
        if (reason.Length > 500) reason = reason[..500];

        var doc = await _db.TutorVerificationDocuments
            .Include(d => d.TutorProfile!).ThenInclude(p => p!.User)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound(new { error = "Document introuvable" });
        if (doc.Status != "pending")
            return Conflict(new { error = "Ce document a déjà été traité." });

        doc.Status = "rejected";
        doc.RejectionReason = reason;
        doc.ReviewedAt = DateTime.UtcNow;
        doc.ReviewedByUserId = User.GetUserId();
        this.AuditAdd(_audit, Backend.Models.Entities.AdminAuditActions.TutorDiplomaRejected, "TutorVerificationDocument", id,
            doc.TutorProfile?.User?.Email, new { status = "pending" }, new { status = "rejected" }, reason);
        await _db.SaveChangesAsync();

        var email = doc.TutorProfile?.User?.Email;
        if (!string.IsNullOrEmpty(email))
        {
            // Lot 9.7a (18.1) : le motif saisi est échappé avant d'entrer dans
            // le HTML de l'e-mail (il y était inséré brut).
            _ = _email.SendGenericEmailAsync(email, "Diplôme non validé · WinPlus",
                $"<p>Ton document n'a pas pu être validé : <strong>{System.Net.WebUtility.HtmlEncode(doc.RejectionReason)}</strong>. " +
                "Tu peux déposer un nouveau document depuis ton profil répétiteur.</p>",
                NotificationCategory.Transactional);
        }

        _logger.LogInformation("Admin {AdminId} a rejeté le document de vérification {DocId}", User.GetUserId(), id);
        return Ok(new { message = "Diplôme rejeté", verified = false });
    }
}
public record AdminActivityEntry(string Id, string Type, DateTime Timestamp, string Title, string Description, string Status, string UserName, string UserEmail, string Target);
public record RejectSubjectRequest(string? Reason);
public record BulkApproveRequest(List<int> Ids);
public record AdminEmailRequest(string? Target, string Subject, string Body, string? CustomEmail, int? ExpectedRecipients = null);
public record AdminChatMessageRequest(string? Content);
public record SendSupportReply(string Content);
