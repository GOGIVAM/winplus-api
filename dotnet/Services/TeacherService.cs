using Backend.Data;
using Backend.Models.Entities;
using Backend.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public interface ITeacherService
{
    Task<IEnumerable<CourseContent>> GetTeacherContentsAsync(int teacherId, int limit = 50);
    Task<IEnumerable<User>> GetTeacherStudentsAsync(int teacherId, int limit = 10);
    Task<IEnumerable<Session>> GetUpcomingSessionsAsync(int teacherId, int limit = 10);
    Task<IEnumerable<dynamic>> GetTeacherQuizzesAsync(int teacherId, int limit = 10);
    Task<IEnumerable<dynamic>> GetTeacherRevisionsAsync(int teacherId, int limit = 10);
    Task<dynamic> GetTeacherStatsAsync(int teacherId);
    Task<dynamic> GetTeacherProfileAsync(int teacherId);
    Task<dynamic> GetTeacherRevenuesAsync(int teacherId);

    /// <summary>
    /// Solde WinPlus dépensable (Module 2, US-CAT-06) : revenus de vente
    /// catalogue moins tout ce qui a déjà été dépensé via ce solde
    /// (assignations de contenu à une classe, achats catalogue payés par solde).
    /// </summary>
    Task<decimal> GetSpendableBalanceAsync(int teacherId);

    /// <summary>
    /// Historique journalier des revenus (US-REP-10) : deux séries "Catalogue"
    /// et "Cours particuliers" sur les N derniers jours.
    /// </summary>
    Task<IEnumerable<dynamic>> GetRevenueHistoryAsync(int teacherId, int days);

    /// <summary>Ventilation des revenus catalogue par contenu (tri + tendance 30j).</summary>
    Task<IEnumerable<dynamic>> GetRevenueByContentAsync(int teacherId, string sort, string dir);

    /// <summary>Détail des transactions "cours particuliers" (US-REP-10).</summary>
    Task<IEnumerable<dynamic>> GetTutoringTransactionsAsync(int teacherId);

    /// <summary>
    /// Historique unifié filtrable par source (Module 7, 7B) : ventes
    /// catalogue et cours particuliers (crédits) + assignations classe et
    /// achats sur solde (débits), dans un seul flux trié par date.
    /// </summary>
    Task<IEnumerable<dynamic>> GetTransactionsAsync(int teacherId, string? source);
}

public class TeacherService : ITeacherService
{
    private readonly ApplicationDbContext _context;
    private readonly ISessionRepository _sessionRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<TeacherService> _logger;

    public TeacherService(
        ApplicationDbContext context,
        ISessionRepository sessionRepository,
        IUserRepository userRepository,
        ILogger<TeacherService> logger)
    {
        _context = context;
        _sessionRepository = sessionRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<IEnumerable<CourseContent>> GetTeacherContentsAsync(int teacherId, int limit = 50)
    {
        try
        {
            // Un enseignant ne doit voir que SES publications. Sans ce filtre,
            // chaque professeur recevait les contenus de tous les autres.
            return await _context.CourseContents
                .AsNoTracking()
                .Where(c => c.CreatedByUserId == teacherId)
                .OrderByDescending(c => c.CreatedAt)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher contents for teacher {TeacherId}", teacherId);
            return Enumerable.Empty<CourseContent>();
        }
    }

    public async Task<IEnumerable<User>> GetTeacherStudentsAsync(int teacherId, int limit = 10)
    {
        try
        {
            var enrollments = await _context.Enrollments
                .AsNoTracking()
                .Where(e => e.EnrolledAt > DateTime.UtcNow.AddMonths(-1))
                .OrderByDescending(e => e.EnrolledAt)
                .Take(limit)
                .Select(e => e.UserId)
                .ToListAsync();

            var students = await _context.Users
                .AsNoTracking()
                .Where(u => enrollments.Contains(u.Id))
                .ToListAsync();

            return students;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher students for teacher {TeacherId}", teacherId);
            return Enumerable.Empty<User>();
        }
    }

    public async Task<IEnumerable<Session>> GetUpcomingSessionsAsync(int teacherId, int limit = 10)
    {
        try
        {
            var sessions = await _sessionRepository.GetByTeacherAsync(teacherId);
            var now = DateTime.UtcNow;
            
            return sessions
                .Where(s => s.StartDate > now)
                .OrderBy(s => s.StartDate)
                .Take(limit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting upcoming sessions for teacher {TeacherId}", teacherId);
            return Enumerable.Empty<Session>();
        }
    }

    public async Task<IEnumerable<dynamic>> GetTeacherQuizzesAsync(int teacherId, int limit = 10)
    {
        try
        {
            // Placeholder: needs a Quizzes table
            return Enumerable.Empty<dynamic>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher quizzes for teacher {TeacherId}", teacherId);
            return Enumerable.Empty<dynamic>();
        }
    }

    public async Task<IEnumerable<dynamic>> GetTeacherRevisionsAsync(int teacherId, int limit = 10)
    {
        try
        {
            // Placeholder: needs a Revisions table
            return Enumerable.Empty<dynamic>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher revisions for teacher {TeacherId}", teacherId);
            return Enumerable.Empty<dynamic>();
        }
    }

    public async Task<dynamic> GetTeacherStatsAsync(int teacherId)
    {
        try
        {
            // ⚠ Corrigé : ces trois requêtes n'étaient scoping ni par
            // teacherId ni par le contenu réellement possédé par ce
            // professeur — chaque professeur voyait les stats de TOUTE la
            // plateforme (inscriptions, note moyenne, nombre de contenus)
            // affichées comme les siennes sur le tableau de bord.
            var mySubjectIds = await _context.CourseContents
                .AsNoTracking()
                .Where(c => c.CreatedByUserId == teacherId)
                .Select(c => c.SubjectId)
                .Distinct()
                .ToListAsync();

            var enrollments = mySubjectIds.Count == 0 ? 0 : await _context.Enrollments
                .AsNoTracking()
                .Where(e => mySubjectIds.Contains(e.SubjectId) && e.EnrolledAt > DateTime.UtcNow.AddMonths(-3))
                .CountAsync();

            var reviews = mySubjectIds.Count == 0 ? 0.0 : await _context.Reviews
                .AsNoTracking()
                .Where(r => !r.IsDeleted && mySubjectIds.Contains(r.SubjectId))
                .Select(r => (double?)r.Rating)
                .AverageAsync() ?? 0.0;

            var contents = await _context.CourseContents
                .AsNoTracking()
                .CountAsync(c => c.CreatedByUserId == teacherId);

            var sessions = await _sessionRepository.GetByTeacherAsync(teacherId);

            return new
            {
                totalStudents = enrollments,
                averageRating = Math.Round(reviews, 2),
                contentCount = contents,
                sessionCount = sessions.Count()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher stats for teacher {TeacherId}", teacherId);
            return new { totalStudents = 0, averageRating = 0, contentCount = 0, sessionCount = 0 };
        }
    }

    public async Task<dynamic> GetTeacherProfileAsync(int teacherId)
    {
        try
        {
            var teacher = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == teacherId && u.Role == "teacher");

            if (teacher == null)
                return null;

            return new
            {
                teacherId = teacher.Id,
                name = $"{teacher.FirstName} {teacher.LastName}",
                email = teacher.Email,
                phone = teacher.Phone,
                profileImageUrl = teacher.ProfileImageUrl,
                bio = teacher.Bio,
                createdAt = teacher.CreatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher profile");
            return null;
        }
    }

    public async Task<dynamic> GetTeacherRevenuesAsync(int teacherId)
    {
        try
        {
            // ⚠ Corrigé : sommait TOUTES les commandes de la plateforme sans
            // filtrer par professeur — chaque professeur voyait le chiffre
            // d'affaires total de WinPlus affiché comme son propre revenu.
            // On attribue maintenant chaque vente via OrderItem.Subject.AuthorUserId
            // (Module 2) : le contenu créé avant cette colonne n'a pas
            // d'auteur connu et n'est légitimement attribué à personne,
            // plutôt que faussement à tout le monde.
            var myItems = _context.OrderItems
                .AsNoTracking()
                .Where(oi => oi.Order.Status == "completed" && oi.Subject != null && oi.Subject.AuthorUserId == teacherId);

            var totalRevenue = await myItems.SumAsync(oi => (decimal?)oi.PriceAtPurchase) ?? 0m;

            var monthlyRevenue = await myItems
                .Where(oi => oi.Order.CreatedAt.Month == DateTime.UtcNow.Month && oi.Order.CreatedAt.Year == DateTime.UtcNow.Year)
                .SumAsync(oi => (decimal?)oi.PriceAtPurchase) ?? 0m;

            var transactionCount = await myItems.CountAsync();

            return new
            {
                totalRevenue = totalRevenue,
                monthlyRevenue = monthlyRevenue,
                transactionCount = transactionCount,
                averagePerTransaction = transactionCount > 0 ? totalRevenue / transactionCount : 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teacher revenues");
            return new { totalRevenue = 0, monthlyRevenue = 0, transactionCount = 0, averagePerTransaction = 0 };
        }
    }

    public async Task<decimal> GetSpendableBalanceAsync(int teacherId)
    {
        var totalRevenue = await _context.OrderItems
            .AsNoTracking()
            .Where(oi => oi.Order.Status == "completed" && oi.Subject != null && oi.Subject.AuthorUserId == teacherId)
            .SumAsync(oi => (decimal?)oi.PriceAtPurchase) ?? 0m;

        var spentOnClassAssignments = await _context.TeacherClassContents
            .AsNoTracking()
            .Where(tcc => tcc.AssignedByUserId == teacherId)
            .SumAsync(tcc => (decimal?)tcc.PriceChargedXaf) ?? 0m;

        var spentOnBalancePurchases = await _context.Orders
            .AsNoTracking()
            .Where(o => o.UserId == teacherId && o.Status == "completed" && o.PaymentMethod == "balance")
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;

        // Cours particuliers (Module 6) : fonds crédités dès la libération de
        // l'escrow simulé (TutorBookingLifecycleService), à la même part
        // enseignant que le catalogue — voir TeacherContentController.GetRevenueShareAsync.
        var revenueShare = await GetRevenueShareAsync(teacherId) ?? 0.80m;
        var tutoringRevenue = await _context.TutorBookings
            .AsNoTracking()
            .Where(b => b.EscrowReleasedAt != null && b.TutorProfile!.UserId == teacherId)
            .SumAsync(b => (decimal?)b.PriceXaf) ?? 0m;

        // Programme d'affiliation (2026-09-11) : seules les commissions déjà
        // "confirmed" (délai de rétractation commande écoulé, voir
        // AffiliateCommissionMaturityService) alimentent le solde retirable —
        // les "pending" ne sont pas encore acquises.
        var affiliateEarnings = await _context.AffiliateCommissions
            .AsNoTracking()
            .Where(c => c.AffiliateAccount!.UserId == teacherId && c.Status == "confirmed")
            .SumAsync(c => (decimal?)c.CommissionAmount) ?? 0m;

        // Retraits déjà effectués ou en cours (Module 7) : réservés dès la
        // demande pour empêcher un double retrait pendant le traitement
        // manuel Mobile Money — voir Withdrawal.cs.
        var withdrawn = await _context.Withdrawals
            .AsNoTracking()
            .Where(w => w.UserId == teacherId && (w.Status == "pending" || w.Status == "completed"))
            .SumAsync(w => (decimal?)w.AmountXaf) ?? 0m;

        return Math.Max(0, totalRevenue - spentOnClassAssignments - spentOnBalancePurchases + tutoringRevenue * revenueShare + affiliateEarnings - withdrawn);
    }

    /// <summary>Part enseignant lue sur le plan actif (dupliqué de TeacherContentController — même formule, contexte différent).</summary>
    private async Task<decimal?> GetRevenueShareAsync(int teacherId) =>
        await _context.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == teacherId && s.Status == "active" && !s.IsDeleted)
            .OrderByDescending(s => s.StartDate)
            .Select(s => s.PricingPlan != null ? s.PricingPlan.TeacherRevenueShare : null)
            .FirstOrDefaultAsync();

    public async Task<IEnumerable<dynamic>> GetRevenueHistoryAsync(int teacherId, int days)
    {
        days = Math.Clamp(days, 1, 365);
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));

        var catalogRows = await _context.OrderItems
            .AsNoTracking()
            .Where(oi => oi.Order.Status == "completed" && oi.Subject != null && oi.Subject.AuthorUserId == teacherId
                && oi.Order.CreatedAt >= since)
            .Select(oi => new { oi.Order.CreatedAt, oi.PriceAtPurchase })
            .ToListAsync();

        var tutoringRows = await _context.TutorBookings
            .AsNoTracking()
            .Where(b => b.TutorProfile!.UserId == teacherId && b.EscrowReleasedAt != null && b.EscrowReleasedAt >= since)
            .Select(b => new { CreatedAt = b.EscrowReleasedAt!.Value, b.PriceXaf })
            .ToListAsync();

        var byDay = Enumerable.Range(0, days)
            .Select(i => since.AddDays(i))
            .Select(date => new
            {
                date = date.ToString("yyyy-MM-dd"),
                catalogAmount = catalogRows.Where(r => r.CreatedAt.Date == date).Sum(r => r.PriceAtPurchase),
                tutoringAmount = tutoringRows.Where(r => r.CreatedAt.Date == date).Sum(r => r.PriceXaf),
            })
            .ToList();

        return byDay;
    }

    public async Task<IEnumerable<dynamic>> GetRevenueByContentAsync(int teacherId, string sort, string dir)
    {
        var now = DateTime.UtcNow;
        var last30 = now.AddDays(-30);
        var prev30 = now.AddDays(-60);

        var items = await _context.OrderItems
            .AsNoTracking()
            .Where(oi => oi.Order.Status == "completed" && oi.Subject != null && oi.Subject.AuthorUserId == teacherId)
            .Select(oi => new { oi.SubjectId, Title = oi.Subject!.Title, oi.PriceAtPurchase, oi.Order.CreatedAt })
            .ToListAsync();

        var rows = items.GroupBy(i => new { i.SubjectId, i.Title }).Select(g =>
        {
            var recent = g.Where(x => x.CreatedAt >= last30).Sum(x => x.PriceAtPurchase);
            var previous = g.Where(x => x.CreatedAt >= prev30 && x.CreatedAt < last30).Sum(x => x.PriceAtPurchase);
            var trend = previous > 0 ? Math.Round((double)((recent - previous) / previous) * 100, 1) : (recent > 0 ? 100.0 : 0.0);
            return new
            {
                id = g.Key.SubjectId,
                title = g.Key.Title,
                sales = g.Count(),
                revenue = g.Sum(x => x.PriceAtPurchase),
                trend,
            };
        });

        rows = dir == "asc"
            ? sort switch
            {
                "title" => rows.OrderBy(r => r.title),
                "sales" => rows.OrderBy(r => r.sales),
                "trend" => rows.OrderBy(r => r.trend),
                _ => rows.OrderBy(r => r.revenue),
            }
            : sort switch
            {
                "title" => rows.OrderByDescending(r => r.title),
                "sales" => rows.OrderByDescending(r => r.sales),
                "trend" => rows.OrderByDescending(r => r.trend),
                _ => rows.OrderByDescending(r => r.revenue),
            };

        return rows.ToList();
    }

    public async Task<IEnumerable<dynamic>> GetTutoringTransactionsAsync(int teacherId)
    {
        var revenueShare = await GetRevenueShareAsync(teacherId) ?? 0.80m;
        var bookings = await _context.TutorBookings
            .AsNoTracking()
            .Include(b => b.Student)
            .Where(b => b.TutorProfile!.UserId == teacherId && b.EscrowReleasedAt != null)
            .OrderByDescending(b => b.EscrowReleasedAt)
            .ToListAsync();

        return bookings.Select(b => new
        {
            id = b.Id,
            studentName = b.Student != null ? $"{b.Student.FirstName} {b.Student.LastName}".Trim() : "Élève",
            subject = b.Subject,
            durationMinutes = (int)(b.EndTime - b.StartTime).TotalMinutes,
            grossAmountXaf = b.PriceXaf,
            commissionPercent = Math.Round((1 - revenueShare) * 100, 1),
            netAmountXaf = Math.Round(b.PriceXaf * revenueShare, 0),
            date = b.EscrowReleasedAt,
        }).ToList();
    }

    public async Task<IEnumerable<dynamic>> GetTransactionsAsync(int teacherId, string? source)
    {
        var revenueShare = await GetRevenueShareAsync(teacherId) ?? 0.80m;
        var rows = new List<(DateTime Date, string Type, string Source, string Label, decimal Gross, decimal Commission, decimal Net, string Status)>();

        // Vente catalogue (crédit) — pas de commission déduite dans ce modèle
        // (voir GetSpendableBalanceAsync : totalRevenue n'applique aucune part
        // enseignant sur les ventes catalogue, contrairement aux cours
        // particuliers). Affiché tel quel plutôt que d'inventer une commission.
        var catalogSales = await _context.OrderItems
            .AsNoTracking()
            .Where(oi => oi.Order.Status == "completed" && oi.Subject != null && oi.Subject.AuthorUserId == teacherId)
            .Select(oi => new { oi.Order.CreatedAt, Title = oi.Subject!.Title, oi.PriceAtPurchase })
            .ToListAsync();
        rows.AddRange(catalogSales.Select(s => (s.CreatedAt, "credit", "catalogue", s.Title, s.PriceAtPurchase, 0m, s.PriceAtPurchase, "completed")));

        // Cours particulier (crédit)
        var tutoring = await _context.TutorBookings
            .AsNoTracking()
            .Include(b => b.Student)
            .Where(b => b.TutorProfile!.UserId == teacherId && b.EscrowReleasedAt != null)
            .ToListAsync();
        rows.AddRange(tutoring.Select(b =>
        {
            var studentName = b.Student != null ? $"{b.Student.FirstName} {b.Student.LastName}".Trim() : "Élève";
            var label = string.IsNullOrWhiteSpace(b.Subject) ? $"Séance avec {studentName}" : $"{b.Subject} — {studentName}";
            var commission = Math.Round(b.PriceXaf * (1 - revenueShare), 0);
            return (b.EscrowReleasedAt!.Value, "credit", "cours_particulier", label, b.PriceXaf, commission, b.PriceXaf - commission, "completed");
        }));

        // Achat catalogue — assignation à une classe (débit)
        var assignments = await _context.TeacherClassContents
            .AsNoTracking()
            .Where(tcc => tcc.AssignedByUserId == teacherId && tcc.PriceChargedXaf > 0)
            .Select(tcc => new { tcc.AssignedAt, Title = tcc.Subject != null ? tcc.Subject.Title : "Contenu", tcc.PriceChargedXaf })
            .ToListAsync();
        rows.AddRange(assignments.Select(a => (a.AssignedAt, "debit", "achat", $"Assignation classe — {a.Title}", a.PriceChargedXaf, 0m, a.PriceChargedXaf, "completed")));

        // Achat catalogue — payé sur le solde WinPlus (débit)
        var balancePurchases = await _context.Orders
            .AsNoTracking()
            .Where(o => o.UserId == teacherId && o.Status == "completed" && o.PaymentMethod == "balance")
            .Select(o => new { o.CreatedAt, o.OrderNumber, o.TotalAmount })
            .ToListAsync();
        rows.AddRange(balancePurchases.Select(o => (o.CreatedAt, "debit", "achat", $"Achat panier {o.OrderNumber}", o.TotalAmount, 0m, o.TotalAmount, "completed")));

        IEnumerable<(DateTime Date, string Type, string Source, string Label, decimal Gross, decimal Commission, decimal Net, string Status)> filtered = rows;
        if (!string.IsNullOrWhiteSpace(source) && source != "all")
            filtered = filtered.Where(r => r.Source == source);

        return filtered
            .OrderByDescending(r => r.Date)
            .Select(r => new
            {
                date = r.Date,
                type = r.Type,
                source = r.Source,
                label = r.Label,
                grossAmountXaf = r.Gross,
                commissionXaf = r.Commission,
                netAmountXaf = r.Net,
                status = r.Status,
            })
            .ToList();
    }
}
