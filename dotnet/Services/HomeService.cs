using Backend.Data;
using Backend.Models.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Service pour les données et fonctionnalités de la HomePage
/// </summary>
public class HomeService : IHomeService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<HomeService> _logger;

    public HomeService(ApplicationDbContext context, ILogger<HomeService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Récupère les statistiques globales de la plateforme
    /// </summary>
    public async Task<HomeStatsDto> GetHomeStatsAsync()
    {
        try
        {
            // Compter les utilisateurs actifs
            var totalUsers = await _context.Users
                .Where(u => u.IsActive && !u.IsDeleted)
                .CountAsync();

            // Compter les examens disponibles
            var totalExams = await _context.Exams
                .Where(e => e.IsPublished && !e.IsDeleted)
                .CountAsync();

            // Compter les révisions disponibles
            var totalRevisions = await _context.Revisions
                .Where(r => !r.IsDeleted)
                .CountAsync();

            // Calculer le taux de réussite moyen
            var completedEnrollments = await _context.Enrollments
                .Where(e => e.IsCompleted)
                .CountAsync();

            var totalEnrollments = await _context.Enrollments
                .CountAsync();

            var successRate = totalEnrollments > 0
                ? Math.Round((double)completedEnrollments / totalEnrollments * 100, 2)
                : 0;

            // Statistiques des utilisateurs par rôle
            var studentCount = await _context.Users
                .Where(u => u.Role == "Student" && u.IsActive && !u.IsDeleted)
                .CountAsync();

            var teacherCount = await _context.Users
                .Where(u => u.Role == "Teacher" && u.IsActive && !u.IsDeleted)
                .CountAsync();

            var parentCount = await _context.Users
                .Where(u => u.Role == "Parent" && u.IsActive && !u.IsDeleted)
                .CountAsync();

            return new HomeStatsDto
            {
                TotalUsers = totalUsers,
                TotalExams = totalExams,
                TotalRevisions = totalRevisions,
                SuccessRate = successRate,
                StudentCount = studentCount,
                TeacherCount = teacherCount,
                ParentCount = parentCount,
                CompletedEnrollments = completedEnrollments,
                TotalEnrollments = totalEnrollments,
                UpdatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting home statistics");
            throw;
        }
    }

    /// <summary>
    /// Récupère les fonctionnalités affichées sur la HomePage
    /// </summary>
    public async Task<IEnumerable<HomeFeatureDto>> GetHomeFeaturesAsync()
    {
        try
        {
            var features = await _context.HomePageFeatures
                .Where(f => f.IsActive)
                .OrderBy(f => f.Order)
                .Select(f => new HomeFeatureDto
                {
                    Id = f.Id,
                    Title = f.Title,
                    Description = f.Description,
                    Icon = f.Icon,
                    ImageUrl = f.ImageUrl,
                    Order = f.Order
                })
                .ToListAsync();

            return features;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting home features");
            throw;
        }
    }

    /// <summary>
    /// Récupère les informations de contact depuis les institutions
    /// </summary>
    public async Task<IEnumerable<ContactInfoDto>> GetContactInfoAsync()
    {
        try
        {
            var contacts = await _context.Institutions
                .Where(i => i.IsActive && !i.IsDeleted)
                .OrderBy(i => i.Country)
                .Select(i => new ContactInfoDto
                {
                    Id = i.Id,
                    Name = i.Name,
                    Country = i.Country,
                    City = i.City,
                    Region = i.Region,
                    Email = i.Email,
                    Phone = i.Phone,
                    Address = i.Address,
                    Type = i.Type
                })
                .ToListAsync();

            return contacts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting contact information");
            throw;
        }
    }

    /// <summary>
    /// Récupère le contenu "À propos" depuis la table Pages
    /// </summary>
    public async Task<PageContentDto> GetAboutContentAsync()
    {
        try
        {
            var aboutPage = await _context.Pages
                .FirstOrDefaultAsync(p => p.Slug == "about" && p.IsPublished);

            if (aboutPage == null)
            {
                return new PageContentDto
                {
                    Slug = "about",
                    Title = "À propos de WinPlus",
                    Content = "Le contenu de la page À propos n'est pas encore disponible.",
                    MetaDescription = "Découvrez WinPlus",
                    IsPublished = false
                };
            }

            return new PageContentDto
            {
                Slug = aboutPage.Slug,
                Title = aboutPage.Title,
                Content = aboutPage.Content,
                MetaDescription = aboutPage.MetaDescription,
                MetaKeywords = aboutPage.MetaKeywords,
                IsPublished = aboutPage.IsPublished,
                PublishedAt = aboutPage.PublishedAt,
                UpdatedAt = aboutPage.UpdatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting about content");
            throw;
        }
    }

    /// <summary>
    /// Récupère les données du footer depuis la configuration ou la base de données
    /// </summary>
    public async Task<FooterDto> GetFooterAsync()
    {
        try
        {
            // Récupérer les infos de contact générales pour le footer
            var contactEmail = "contact@winplus.cm";
            var contactPhone = "+237 2XX XXX XXX";
            var contactAddress = "Yaoundé, Cameroun";
            
            var footerData = new FooterDto
            {
                CompanyName = "WinPlus",
                CompanyDescription = "Plateforme d'apprentissage en ligne pour étudiants, enseignants et parents",
                Email = contactEmail,
                Phone = contactPhone,
                Address = contactAddress,
                Copyright = $"© {DateTime.UtcNow.Year} WinPlus. Tous droits réservés.",
                Links = new Dictionary<string, string>
                {
                    { "Accueil", "/" },
                    { "Catalogue", "/catalog" },
                    { "Tarifs", "/pricing" },
                    { "À propos", "/about" },
                    { "Contact", "/contact" },
                    { "FAQ", "/faq" },
                    { "Politique de confidentialité", "/privacy" },
                    { "Conditions d'utilisation", "/terms" },
                    { "Cookies", "/cookies" }
                },
                SocialMedia = new Dictionary<string, string>
                {
                    { "Facebook", "https://facebook.com/winplus" },
                    { "Twitter", "https://twitter.com/winplus" },
                    { "LinkedIn", "https://linkedin.com/company/winplus" },
                    { "Instagram", "https://instagram.com/winplus" }
                },
                UpdatedAt = DateTime.UtcNow
            };

            return await Task.FromResult(footerData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting footer data");
            throw;
        }
    }
}
