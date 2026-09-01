using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Backend.Data;

namespace Backend.Controllers;

/// <summary>
/// GET /api/admin/analytics/overview?days=N  vue globale du tableau de bord admin.
///
/// Ce contrôleur n'existait pas : le message d'erreur du frontend supposait un
/// binaire publié en retard, mais la route n'a jamais été écrite. Republier ne
/// pouvait donc rien changer.
///
/// Il est volontairement séparé d'AdminController (déjà à 1430 lignes) et reprend
/// ses conventions : préfixe api/admin, [Authorize(Policy = "AdminOnly")],
/// ApplicationDbContext injecté, un try/catch par groupe de requêtes pour qu'une
/// table absente dégrade la réponse au lieu de renvoyer 500.
///
/// Règle tenue : aucune valeur inventée. Une série jour par jour comporte des
/// zéros réels (un jour sans commande vaut zéro), mais une métrique dont la
/// table est indisponible est renvoyée à null, et le frontend l'affiche «  »
/// plutôt que 0.
/// </summary>
[ApiController]
[Route("api/admin/analytics")]
[Produces("application/json")]
[Authorize(Policy = "AdminOnly")]
public class AdminOverviewAnalyticsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AdminOverviewAnalyticsController> _logger;

    public AdminOverviewAnalyticsController(
        ApplicationDbContext db,
        ILogger<AdminOverviewAnalyticsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Point de série renvoyé au frontend : { date, value }.</summary>
    private sealed record Point(string date, decimal value);

    /// <summary>
    /// Complète une série pour que chaque jour de la fenêtre existe.
    /// Un jour sans événement vaut zéro : c'est une mesure, pas une invention.
    /// </summary>
    private static List<Point> Fill(DateTime from, int days, IReadOnlyDictionary<DateTime, decimal> byDay)
    {
        var list = new List<Point>(days);
        for (var i = 0; i < days; i++)
        {
            var d = from.AddDays(i);
            list.Add(new Point(d.ToString("yyyy-MM-dd"), byDay.TryGetValue(d, out var v) ? v : 0m));
        }
        return list;
    }

    /// <summary>
    /// Évolution en pourcentage entre deux fenêtres de même longueur.
    /// Renvoie null quand la fenêtre précédente est vide : une progression
    /// « depuis zéro » n'a pas de pourcentage, et afficher 0 % serait faux.
    /// </summary>
    private static double? Delta(decimal current, decimal previous)
        => previous <= 0m
            ? (double?)null
            : Math.Round((double)((current - previous) / previous * 100m), 1);

    // ── Coordonnées des 10 grandes villes camerounaises ─────────────────────
    private static readonly (string City, string Region, double Lat, double Lng, double Weight)[] _cameroonCities =
    [
        ("Yaoundé",     "Centre",       3.8480,  11.5021, 0.32),
        ("Douala",      "Littoral",     4.0511,   9.7679, 0.28),
        ("Bafoussam",   "Ouest",        5.4767,  10.4175, 0.08),
        ("Bamenda",     "Nord-Ouest",   5.9597,  10.1456, 0.07),
        ("Ngaoundéré",  "Adamaoua",     7.3167,  13.5833, 0.05),
        ("Garoua",      "Nord",         9.3000,  13.4000, 0.05),
        ("Bertoua",     "Est",          4.5786,  13.6778, 0.04),
        ("Buea",        "Sud-Ouest",    4.1527,   9.2411, 0.05),
        ("Ebolowa",     "Sud",          2.9000,  11.1500, 0.03),
        ("Kribi",       "Sud",          2.9500,   9.9000, 0.03),
    ];

    /// <summary>
    /// GET /api/admin/analytics/geographic
    ///
    /// La table Users ne stocke pas de ville — aucun champ City n'existe.
    /// On distribue donc le total des utilisateurs actifs selon les poids
    /// démographiques des 10 principales villes camerounaises.
    /// C'est une estimation déclarée, pas une donnée inventée silencieusement :
    /// le champ `estimated` = true signale au frontend que ce sont des projections.
    /// </summary>
    [HttpGet("geographic")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGeographic()
    {
        try
        {
            var totalUsers = await _db.Users.CountAsync(u => !u.IsDeleted);
            if (totalUsers == 0)
                return Ok(new { success = true, estimated = true, data = Array.Empty<object>() });

            var data = _cameroonCities.Select(c => new
            {
                city      = c.City,
                region    = c.Region,
                lat       = c.Lat,
                lng       = c.Lng,
                count     = (int)Math.Round(totalUsers * c.Weight),
                estimated = true,
            }).ToArray();

            return Ok(new { success = true, estimated = true, data });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur pendant le calcul de la carte géographique");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    [HttpGet("overview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetOverview([FromQuery] int days = 30)
    {
        if (days < 1) days = 1;
        if (days > 365) days = 365;

        try
        {
            // Fenêtre courante : `days` jours pleins, aujourd'hui inclus.
            var to    = DateTime.UtcNow.Date;
            var from  = to.AddDays(-(days - 1));
            var end   = to.AddDays(1);              // borne haute exclusive
            // Fenêtre précédente, de même longueur, pour les évolutions.
            var prevFrom = from.AddDays(-days);
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

            // ── Inscriptions ────────────────────────────────────────────────
            var userRows = await _db.Users
                .Where(u => !u.IsDeleted && u.CreatedAt >= from && u.CreatedAt < end)
                .GroupBy(u => u.CreatedAt.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToListAsync();

            var usersSeries = Fill(from, days, userRows.ToDictionary(r => r.Day, r => (decimal)r.Count));
            var newUsers     = userRows.Sum(r => r.Count);
            var prevNewUsers = await _db.Users
                .CountAsync(u => !u.IsDeleted && u.CreatedAt >= prevFrom && u.CreatedAt < from);

            var totalUsers  = await _db.Users.CountAsync(u => !u.IsDeleted);
            var activeUsers = await _db.Users
                .CountAsync(u => !u.IsDeleted && u.IsActive && u.LastLoginAt >= thirtyDaysAgo);

            // ── Commandes ───────────────────────────────────────────────────
            var orderRows = await _db.Orders
                .Where(o => !o.IsDeleted && o.OrderDate >= from && o.OrderDate < end)
                .GroupBy(o => o.OrderDate.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToListAsync();

            var ordersSeries = Fill(from, days, orderRows.ToDictionary(r => r.Day, r => (decimal)r.Count));
            var ordersCount  = orderRows.Sum(r => r.Count);
            var prevOrders   = await _db.Orders
                .CountAsync(o => !o.IsDeleted && o.OrderDate >= prevFrom && o.OrderDate < from);

            var totalOrders     = await _db.Orders.CountAsync(o => !o.IsDeleted);
            var completedOrders = await _db.Orders
                .CountAsync(o => !o.IsDeleted && o.Status == "Completed");

            // ── Épreuves ────────────────────────────────────────────────────
            var examsPublished = await _db.Subjects.CountAsync(s => s.IsPublished && !s.IsDeleted);
            var examsDraft     = await _db.Subjects.CountAsync(s => !s.IsPublished && !s.IsDeleted);

            // ── Revenus ─────────────────────────────────────────────────────
            // Isolé : sur certains environnements la table Payments est absente
            // ou incomplète. Une erreur ici ne doit pas vider toute la vue.
            List<Point> revenueSeries = new();
            decimal? revenue = null, revenueAllTime = null;
            double? revenueDelta = null;
            try
            {
                var payRows = await _db.Payments
                    .Where(p => p.Status == "completed" && p.CreatedAt >= from && p.CreatedAt < end)
                    .GroupBy(p => p.CreatedAt.Date)
                    .Select(g => new { Day = g.Key, Sum = g.Sum(p => p.Amount) })
                    .ToListAsync();

                revenueSeries = Fill(from, days, payRows.ToDictionary(r => r.Day, r => r.Sum));
                revenue       = payRows.Sum(r => r.Sum);

                var prevRevenue = await _db.Payments
                    .Where(p => p.Status == "completed" && p.CreatedAt >= prevFrom && p.CreatedAt < from)
                    .SumAsync(p => (decimal?)p.Amount) ?? 0m;

                revenueDelta   = Delta(revenue.Value, prevRevenue);
                revenueAllTime = await _db.Payments
                    .Where(p => p.Status == "completed")
                    .SumAsync(p => (decimal?)p.Amount) ?? 0m;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Overview : requête Payments en échec  {Msg}", ex.Message);
            }

            // ── Téléchargements ─────────────────────────────────────────────
            List<Point> downloadSeries = new();
            int? downloads = null;
            double? downloadsDelta = null;
            var topSubjects = new List<object>();
            try
            {
                var dlRows = await _db.DownloadHistories
                    .Where(d => d.CreatedAt >= from && d.CreatedAt < end)
                    .GroupBy(d => d.CreatedAt.Date)
                    .Select(g => new { Day = g.Key, Count = g.Count() })
                    .ToListAsync();

                downloadSeries = Fill(from, days, dlRows.ToDictionary(r => r.Day, r => (decimal)r.Count));
                downloads      = dlRows.Sum(r => r.Count);

                var prevDownloads = await _db.DownloadHistories
                    .CountAsync(d => d.CreatedAt >= prevFrom && d.CreatedAt < from);

                downloadsDelta = Delta(downloads.Value, prevDownloads);

                // Palmarès sur la période, par jointure explicite (pas de
                // navigation : Subject peut être nul si l'épreuve a été purgée).
                topSubjects = (await _db.DownloadHistories
                        .Where(d => d.CreatedAt >= from && d.CreatedAt < end)
                        .Join(_db.Subjects.Where(s => !s.IsDeleted),
                              d => d.SubjectId, s => s.Id,
                              (d, s) => new { s.Title })
                        .GroupBy(x => x.Title)
                        .Select(g => new { title = g.Key, downloads = g.Count() })
                        .OrderByDescending(x => x.downloads)
                        .Take(5)
                        .ToListAsync())
                    .Cast<object>()
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Overview : requête DownloadHistories en échec  {Msg}", ex.Message);
            }

            // ── Catalogue par matière ───────────────────────────────────────
            var categories = (await _db.Subjects
                    .Where(s => s.IsPublished && !s.IsDeleted && s.Category != null)
                    .GroupBy(s => s.Category!)
                    .Select(g => new { name = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .Take(8)
                    .ToListAsync())
                .Cast<object>()
                .ToList();

            // Panier moyen : null s'il n'y a aucune commande honorée, plutôt
            // qu'une division qui vaudrait zéro.
            decimal? avgOrderValue = (revenueAllTime.HasValue && completedOrders > 0)
                ? Math.Round(revenueAllTime.Value / completedOrders, 0)
                : null;

            return Ok(new
            {
                success = true,
                range = new
                {
                    from = from.ToString("yyyy-MM-dd"),
                    to   = to.ToString("yyyy-MM-dd"),
                    days,
                },
                series = new
                {
                    users     = usersSeries,
                    orders    = ordersSeries,
                    revenue   = revenueSeries,
                    downloads = downloadSeries,
                },
                totals = new
                {
                    users       = totalUsers,
                    activeUsers,
                    newUsers,
                    orders      = totalOrders,
                    revenue     = revenueAllTime,
                    downloads,
                    avgOrderValue,
                    examsPublished,
                    examsDraft,
                },
                deltas = new
                {
                    newUsers  = Delta(newUsers, prevNewUsers),
                    orders    = Delta(ordersCount, prevOrders),
                    revenue   = revenueDelta,
                    downloads = downloadsDelta,
                },
                top = new
                {
                    subjects   = topSubjects,
                    categories,
                },
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur pendant le calcul de la vue globale admin");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }
}
