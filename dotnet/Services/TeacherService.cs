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
    /// Solde WinPlus dépensable (Module 2, US-CAT-06), lu dans le journal de
    /// portefeuille (lot 2, Module 1) : somme des écritures confirmées.
    /// Pour un débit, utiliser <see cref="IWalletService.DebitAsync"/> sous
    /// verrou plutôt que de lire ce solde puis d'écrire.
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
    /// Historique unifié filtrable par source (Module 7, 7B), lu dans le
    /// journal de portefeuille (lot 2, Module 1), trié par date.
    /// </summary>
    Task<IEnumerable<dynamic>> GetTransactionsAsync(int teacherId, string? source);
}

public class TeacherService : ITeacherService
{
    private readonly ApplicationDbContext _context;
    private readonly ISessionRepository _sessionRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<TeacherService> _logger;
    private readonly IWalletService _wallet;

    public TeacherService(
        ApplicationDbContext context,
        ISessionRepository sessionRepository,
        IUserRepository userRepository,
        ILogger<TeacherService> logger,
        IWalletService wallet)
    {
        _context = context;
        _wallet = wallet;
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
            // professeur  chaque professeur voyait les stats de TOUTE la
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
                createdAt = teacher.CreatedAt,
                // Module 15 (lot 6) : corrige le bug où ces matières/niveaux
                // n'étaient visibles que sur /users/profile (UsersController),
                // jamais sur ce profil professeur dédié. Champs ajoutés sans
                // renommer les champs existants pour ne casser aucun
                // consommateur actuel.
                teachingSubjects = teacher.TeachingSubjects,
                teachingLevels = teacher.TeachingLevels
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
            // filtrer par professeur  chaque professeur voyait le chiffre
            // d'affaires total de WinPlus affiché comme son propre revenu.
            // On attribue maintenant chaque vente via OrderItem.Subject.AuthorUserId
            // (Module 2) : le contenu créé avant cette colonne n'a pas
            // d'auteur connu et n'est légitimement attribué à personne,
            // plutôt que faussement à tout le monde.
            var myItems = _context.OrderItems
                .AsNoTracking()
                .Where(oi => PaidOrderStatus.All.Contains(oi.Order.Status.ToLower()) && oi.Subject != null && oi.Subject.AuthorUserId == teacherId);

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

    /// <summary>
    /// Module 1 (lot 2) : le solde est la somme des écritures confirmées du
    /// journal de portefeuille (<see cref="IWalletService"/>), et non plus une
    /// sommation des tables métier (commandes, assignations, réservations,
    /// commissions, retraits) recalculée à chaque appel. La forme de retour est
    /// inchangée pour les appelants existants : un solde négatif (anomalie,
    /// signalée par le journal) est ramené à zéro comme auparavant.
    /// </summary>
    public async Task<decimal> GetSpendableBalanceAsync(int teacherId)
    {
        var available = await _wallet.GetAvailableAsync(teacherId);
        if (available < 0)
            _logger.LogError("Anomalie de portefeuille : solde négatif ({Available} XAF) pour l'utilisateur {UserId}", available, teacherId);
        return Math.Max(0, available);
    }

    /// <summary>Part enseignant lue sur le plan actif (règle partagée avec le journal, voir <see cref="RevenueSplit"/>).</summary>
    private Task<decimal> GetRevenueShareAsync(int teacherId) => RevenueSplit.GetTeacherShareAsync(_context, teacherId);

    public async Task<IEnumerable<dynamic>> GetRevenueHistoryAsync(int teacherId, int days)
    {
        days = Math.Clamp(days, 1, 365);
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));

        var catalogRows = await _context.OrderItems
            .AsNoTracking()
            .Where(oi => PaidOrderStatus.All.Contains(oi.Order.Status.ToLower()) && oi.Subject != null && oi.Subject.AuthorUserId == teacherId
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
            .Where(oi => PaidOrderStatus.All.Contains(oi.Order.Status.ToLower()) && oi.Subject != null && oi.Subject.AuthorUserId == teacherId)
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
        var revenueShare = await GetRevenueShareAsync(teacherId);
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
            // Même décomposition qu'en historique unifié (Module 19) : seule
            // la commission est arrondie, le net s'en déduit par soustraction,
            // pour que net + commission retombe exactement sur le prix payé.
            // Les deux endroits arrondissaient auparavant chacun leur part,
            // et pouvaient annoncer un net différent de 1 XAF pour la même
            // séance.
            netAmountXaf = RevenueSplit.NetXaf(b.PriceXaf, revenueShare),
            date = b.EscrowReleasedAt,
        }).ToList();
    }

    /// <summary>
    /// Module 1 (lot 2) : historique lu dans le journal de portefeuille, avec
    /// la même forme de réponse qu'avant (date, type, source, label, brut,
    /// commission, net, statut). Les sources historiques (catalogue,
    /// cours_particulier, achat) gardent leur nom ; s'y ajoutent affiliation,
    /// recharge, credit_admin et retrait. Les écritures de montant nul
    /// (traitement d'un retrait) sont omises de cette vue historique.
    /// </summary>
    public async Task<IEnumerable<dynamic>> GetTransactionsAsync(int teacherId, string? source)
    {
        var (items, _) = await _wallet.GetHistoryAsync(teacherId, source, 1, 200);
        return items
            .Where(i => i.AmountXaf != 0)
            .Select(i => (dynamic)new
            {
                date = i.Date,
                type = i.Type,
                source = i.Source,
                label = i.Label,
                grossAmountXaf = i.GrossAmountXaf,
                commissionXaf = i.CommissionXaf,
                netAmountXaf = Math.Abs(i.AmountXaf),
                status = i.Status,
            })
            .ToList();
    }
}
