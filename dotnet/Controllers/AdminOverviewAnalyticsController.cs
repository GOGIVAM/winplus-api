using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Services;

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

    // ── Coordonnées des principales villes camerounaises (placement sur la carte) ──
    private static readonly (string City, string Region, double Lat, double Lng)[] _cameroonCities =
    [
        ("Yaoundé",     "Centre",       3.8480,  11.5021),
        ("Douala",      "Littoral",     4.0511,   9.7679),
        ("Bafoussam",   "Ouest",        5.4767,  10.4175),
        ("Bamenda",     "Nord-Ouest",   5.9597,  10.1456),
        ("Ngaoundéré",  "Adamaoua",     7.3167,  13.5833),
        ("Garoua",      "Nord",         9.3000,  13.4000),
        ("Maroua",      "Extrême-Nord", 10.5910, 14.3159),
        ("Bertoua",     "Est",          4.5786,  13.6778),
        ("Buea",        "Sud-Ouest",    4.1527,   9.2411),
        ("Limbé",       "Sud-Ouest",    4.0167,   9.2167),
        ("Kumba",       "Sud-Ouest",    4.6363,   9.4469),
        ("Ebolowa",     "Sud",          2.9000,  11.1500),
        ("Kribi",       "Sud",          2.9500,   9.9000),
        ("Edéa",        "Littoral",     3.8000,  10.1333),
        ("Nkongsamba",  "Littoral",     4.9547,   9.9404),
        ("Dschang",     "Ouest",        5.4500,  10.0667),
        ("Foumban",     "Ouest",        5.7167,  10.9000),
    ];

    private static string Fold(string s)
    {
        var normalized = s.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var ch in normalized)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString();
    }

    /// <summary>
    /// GET /api/admin/analytics/geographic
    ///
    /// Lot 9.7a (18.1) : répartition RÉELLE des comptes par ville déclarée
    /// (User.City). L'ancienne version multipliait le total des comptes par
    /// des poids démographiques fixes : elle affichait des utilisateurs dans
    /// des villes où il n'y en avait aucun. Les villes connues sont placées
    /// sur la carte ; les autres sont listées sans coordonnées, et les comptes
    /// sans ville déclarée sont comptés à part.
    /// </summary>
    [HttpGet("geographic")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGeographic()
    {
        try
        {
            var users = _db.Users.AsNoTracking().Where(u => !u.IsDeleted && u.Role != "admin");
            var totalUsers = await users.CountAsync();
            var rows = await users
                .Where(u => u.City != null && u.City.Trim() != "")
                .GroupBy(u => u.City!.Trim())
                .Select(g => new { City = g.Key, Count = g.Count() })
                .ToListAsync();

            var known = _cameroonCities.ToDictionary(c => Fold(c.City));
            var merged = rows
                .GroupBy(r => Fold(r.City))
                .Select(g =>
                {
                    known.TryGetValue(g.Key, out var k);
                    var hasCoords = k.City != null;
                    return new
                    {
                        city = hasCoords ? k.City : g.OrderByDescending(x => x.Count).First().City,
                        region = hasCoords ? k.Region : null,
                        lat = hasCoords ? k.Lat : (double?)null,
                        lng = hasCoords ? k.Lng : (double?)null,
                        count = g.Sum(x => x.Count),
                        estimated = false,
                    };
                })
                .OrderByDescending(x => x.count)
                .ToList();

            var withCity = merged.Sum(m => m.count);
            return Ok(new
            {
                success = true,
                estimated = false,
                totalUsers,
                withoutCity = totalUsers - withCity,
                data = merged,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur pendant le calcul de la carte géographique");
            return StatusCode(500, new { success = false, error = "Erreur serveur" });
        }
    }

    /// <summary>
    /// GET /api/admin/analytics/conversion-rate?days=N  Conversion de la
    /// période, calculée en SQL (voir <see cref="AdminMetrics.ConversionAsync"/>),
    /// avec la période précédente pour l'évolution. Lot 9.7a : le « panier
    /// abandonné » (sans définition mesurable) est retiré.
    /// </summary>
    [HttpGet("conversion-rate")]
    public async Task<IActionResult> GetConversionRate([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var to = DateTime.UtcNow;
        var from = to.AddDays(-days);
        var current = await AdminMetrics.ConversionAsync(_db, from, to);
        var previous = await AdminMetrics.ConversionAsync(_db, from.AddDays(-days), from);
        return Ok(new
        {
            success = true,
            data = new
            {
                days,
                conversionRate = current.Rate,
                registered = current.Registered,
                buyers = current.Buyers,
                previousConversionRate = previous.Rate,
            },
        });
    }

    /// <summary>
    /// GET /api/admin/analytics/popular-subjects?days=N&limit=K  Contenus les
    /// plus téléchargés sur la période (lot 9.7a : calculé en base, l'ancienne
    /// version renvoyait les K premiers contenus, sans classement).
    /// </summary>
    [HttpGet("popular-subjects")]
    public async Task<IActionResult> GetPopularSubjects([FromQuery] int days = 30, [FromQuery] int limit = 10)
    {
        days = Math.Clamp(days, 1, 365);
        limit = Math.Clamp(limit, 1, 50);
        var from = DateTime.UtcNow.AddDays(-days);
        var top = await _db.DownloadHistories.AsNoTracking()
            .Where(d => d.CreatedAt >= from)
            .Join(_db.Subjects.Where(s => !s.IsDeleted), d => d.SubjectId, s => s.Id, (d, s) => new { s.Id, s.Title, s.Category })
            .GroupBy(x => new { x.Id, x.Title, x.Category })
            .Select(g => new { id = g.Key.Id, title = g.Key.Title, category = g.Key.Category, downloads = g.Count() })
            .OrderByDescending(x => x.downloads)
            .Take(limit)
            .ToListAsync();
        return Ok(new { success = true, data = top, days });
    }

    private static (DateTime From, DateTime End, int Days) Window(int days)
    {
        days = Math.Clamp(days, 1, 365);
        var to = DateTime.UtcNow.Date;
        return (to.AddDays(-(days - 1)), to.AddDays(1), days);
    }

    /// <summary>
    /// GET /api/admin/analytics/revenues?days=N  Écran Revenus (lot 9.7a,
    /// 18.1) : tout est calculé sur la période demandée, à partir des
    /// paiements réellement encaissés (Payments completed) et des commandes
    /// encaissées. Croissance : revenus de la période contre la période
    /// précédente de même longueur (et non plus l'évolution du nombre de
    /// commandes) ; commandes complètes : uniquement les commandes encaissées
    /// (et non plus toutes) ; panier moyen : montant des commandes encaissées
    /// divisé par leur nombre ; meilleurs produits : chiffre d'affaires réel
    /// des lignes de commande encaissées.
    /// </summary>
    [HttpGet("revenues")]
    public async Task<IActionResult> GetRevenues([FromQuery] int days = 30)
    {
        try
        {
            var (from, end, n) = Window(days);
            var prevFrom = from.AddDays(-n);
            var paid = AdminMetrics.CompletedOrderStatuses;

            var payRows = await _db.Payments.AsNoTracking()
                .Where(p => p.Status == "completed" && (p.CompletedAt ?? p.CreatedAt) >= from && (p.CompletedAt ?? p.CreatedAt) < end)
                .GroupBy(p => (p.CompletedAt ?? p.CreatedAt).Date)
                .Select(g => new { Day = g.Key, Sum = g.Sum(p => p.Amount), Count = g.Count() })
                .ToListAsync();
            var revenue = payRows.Sum(r => r.Sum);
            var previousRevenue = await _db.Payments.AsNoTracking()
                .Where(p => p.Status == "completed" && (p.CompletedAt ?? p.CreatedAt) >= prevFrom && (p.CompletedAt ?? p.CreatedAt) < from)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;

            var orders = _db.Orders.AsNoTracking()
                .Where(o => !o.IsDeleted && paid.Contains(o.Status.ToLower())
                         && (o.CompletedDate ?? o.CreatedAt) >= from && (o.CompletedDate ?? o.CreatedAt) < end);
            var completedOrders = await orders.CountAsync();
            var ordersAmount = await orders.SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;

            var topProducts = await _db.OrderItems.AsNoTracking()
                .Where(oi => !oi.Order.IsDeleted && paid.Contains(oi.Order.Status.ToLower())
                          && (oi.Order.CompletedDate ?? oi.Order.CreatedAt) >= from && (oi.Order.CompletedDate ?? oi.Order.CreatedAt) < end)
                .Select(oi => new
                {
                    Name = oi.Course != null ? oi.Course.Title : oi.Subject != null ? oi.Subject.Title : "Autre",
                    oi.PriceAtPurchase,
                })
                .GroupBy(x => x.Name)
                .Select(g => new { name = g.Key, revenue = g.Sum(x => x.PriceAtPurchase), orders = g.Count() })
                .OrderByDescending(x => x.revenue)
                .Take(10)
                .ToListAsync();

            var conversion = await AdminMetrics.ConversionAsync(_db, from, end);
            var byDay = payRows.ToDictionary(r => r.Day, r => r.Sum);

            return Ok(new
            {
                success = true,
                range = new { from = from.ToString("yyyy-MM-dd"), to = end.AddDays(-1).ToString("yyyy-MM-dd"), days = n },
                revenue,
                previousRevenue,
                growth = Delta(revenue, previousRevenue),
                completedOrders,
                avgOrderValue = completedOrders > 0 ? Math.Round(ordersAmount / completedOrders, 0) : (decimal?)null,
                payments = payRows.Sum(r => r.Count),
                conversionRate = conversion.Rate,
                conversionRegistered = conversion.Registered,
                conversionBuyers = conversion.Buyers,
                series = Fill(from, n, byDay),
                topProducts,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur pendant le calcul des revenus");
            return StatusCode(500, new { success = false, error = "Erreur serveur" });
        }
    }

    /// <summary>GET /api/admin/analytics/revenues/export?days=N  Paiements encaissés de la période, en CSV (séparateur « ; », UTF-8).</summary>
    [HttpGet("revenues/export")]
    public async Task<IActionResult> ExportRevenues([FromQuery] int days = 30)
    {
        var (from, end, n) = Window(days);
        var rows = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == "completed" && (p.CompletedAt ?? p.CreatedAt) >= from && (p.CompletedAt ?? p.CreatedAt) < end)
            .OrderBy(p => p.CompletedAt ?? p.CreatedAt)
            .Select(p => new
            {
                Date = p.CompletedAt ?? p.CreatedAt,
                OrderNumber = p.Order != null ? p.Order.OrderNumber : null,
                Email = p.User != null ? p.User.Email : p.GuestEmail,
                p.Amount, p.Currency, p.PaymentMethod, p.Operator, p.Description,
                Reference = p.TransactionId ?? p.NotchpayReference,
            })
            .ToListAsync();

        static string Cell(object? v)
        {
            var s = v switch
            {
                null => "",
                DateTime d => d.ToString("yyyy-MM-dd HH:mm"),
                decimal m => m.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                _ => v.ToString() ?? "",
            };
            return s.Contains(';') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Date;Commande;Client;Montant;Devise;Moyen de paiement;Opérateur;Libellé;Référence");
        foreach (var r in rows)
            sb.AppendLine(string.Join(';', new object?[] { r.Date, r.OrderNumber, r.Email, r.Amount, r.Currency, r.PaymentMethod, r.Operator, r.Description, r.Reference }.Select(Cell)));

        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"revenus_{from:yyyy-MM-dd}_{end.AddDays(-1):yyyy-MM-dd}.csv");
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
                .CountAsync(o => !o.IsDeleted && o.Status.ToLower() == "completed");
            var uniqueBuyers    = await _db.Orders
                .Where(o => !o.IsDeleted && o.Status.ToLower() == "completed")
                .Select(o => o.UserId)
                .Distinct()
                .CountAsync();

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
                              (d, s) => new { s.Id, s.Title })
                        // Par épreuve (et non par titre) : deux épreuves de même
                        // titre ne sont plus fusionnées, et l'id permet d'ouvrir
                        // le détail depuis « Contenus populaires ».
                        .GroupBy(x => new { x.Id, x.Title })
                        .Select(g => new { id = g.Key.Id, title = g.Key.Title, downloads = g.Count() })
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
                    ordersInPeriod = ordersCount,
                    completedOrders,
                    uniqueBuyers,
                    revenue     = revenueAllTime,
                    revenueInPeriod = revenue,
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
