using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>Canal d'envoi d'une notification (Module 22).</summary>
public enum NotificationChannel
{
    /// <summary>Canal temps réel ntfy (web SSE, mobile) : préférence PushNotifications.</summary>
    Push,
    /// <summary>Notification enregistrée dans la liste in-app (table Notifications).</summary>
    InApp,
    /// <summary>Courriel : préférence EmailNotifications.</summary>
    Email,
}

/// <summary>
/// Catégorie d'une notification, qui décide quelle préférence utilisateur
/// s'applique (Module 22).
/// </summary>
public enum NotificationCategory
{
    /// <summary>Argent, commande, compte, sécurité, consentement : jamais désactivable.</summary>
    Transactional,
    /// <summary>Soumis aux seules préférences de canal (push, e-mail).</summary>
    General,
    /// <summary>Préférence « Communauté &amp; cours » (forums, canaux de cours, avis).</summary>
    CourseCommunity,
    /// <summary>Préférence « Rappels d'apprentissage ».</summary>
    LearningReminders,
    /// <summary>Préférence « Promotions ».</summary>
    Promotions,
    /// <summary>Préférence « Newsletter » (diffusions générales de l'administration).</summary>
    Newsletters,
}

/// <summary>
/// Correspondance entre le « type » d'une notification (champ Notification.Type,
/// passé par chaque émetteur à INtfyService.PublishAsync) et sa catégorie.
///
/// Module 22 : HYPOTHÈSE À FAIRE VALIDER PAR LE PRODUCT OWNER. La liste des
/// notifications transactionnelles non désactivables et le rattachement des
/// types aux préférences n'ont pas été tranchés ; cette table est le seul
/// endroit à modifier si la décision diffère. Tout type absent de la table est
/// « General » : il reste soumis aux préférences de canal mais à aucune
/// préférence de catégorie, pour ne couper aucune notification par erreur.
/// </summary>
public static class NotificationPolicy
{
    private static readonly Dictionary<string, NotificationCategory> ByType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Transactionnel : argent, commande, remboursement, retrait,
            // réservation payante (séquestre), session annulée (remboursement),
            // contenu offert par un parent (achat), consentement de contact d'un
            // mineur, alertes administrateur.
            ["payment"] = NotificationCategory.Transactional,
            ["Order"] = NotificationCategory.Transactional,
            ["Refund"] = NotificationCategory.Transactional,
            ["Withdrawal"] = NotificationCategory.Transactional,
            ["Subscription"] = NotificationCategory.Transactional,
            ["TutorBooking"] = NotificationCategory.Transactional,
            ["SessionCancelled"] = NotificationCategory.Transactional,
            ["content"] = NotificationCategory.Transactional,
            ["access_request"] = NotificationCategory.Transactional,
            ["Account"] = NotificationCategory.Transactional,
            ["Security"] = NotificationCategory.Transactional,
            ["Admin"] = NotificationCategory.Transactional,

            // Communauté & cours.
            ["forum"] = NotificationCategory.CourseCommunity,
            ["CourseChannel"] = NotificationCategory.CourseCommunity,
            ["TutorReview"] = NotificationCategory.CourseCommunity,

            // Rappels d'apprentissage.
            ["CourseSection"] = NotificationCategory.LearningReminders,
            ["CourseGamification"] = NotificationCategory.LearningReminders,
            ["revision"] = NotificationCategory.LearningReminders,
        };

    public static NotificationCategory CategoryOf(string? type) =>
        type != null && ByType.TryGetValue(type, out var category) ? category : NotificationCategory.General;
}

/// <summary>
/// Point unique de lecture des préférences de notification (Module 22) : tout
/// émetteur (ntfy, in-app, e-mail) passe par ici avant d'envoyer, pour qu'un
/// nouvel émetteur en bénéficie sans dupliquer la règle.
/// </summary>
public interface INotificationPreferenceService
{
    Task<bool> AllowsAsync(int userId, NotificationChannel channel, NotificationCategory category, CancellationToken ct = default);

    /// <summary>Préférences de l'utilisateur, ou valeurs par défaut s'il n'en a jamais enregistré.</summary>
    Task<UserNotificationSettings> GetSettingsAsync(int userId, CancellationToken ct = default);

    /// <summary>Filtre une liste d'adresses e-mail (envois en lot) : renvoie celles qui acceptent l'envoi.</summary>
    Task<List<string>> FilterEmailsAsync(IEnumerable<string> emails, NotificationCategory category, CancellationToken ct = default);
}

public sealed class NotificationPreferenceService : INotificationPreferenceService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationPreferenceService> _logger;

    public NotificationPreferenceService(IServiceScopeFactory scopeFactory, ILogger<NotificationPreferenceService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<bool> AllowsAsync(int userId, NotificationChannel channel, NotificationCategory category, CancellationToken ct = default)
    {
        if (category == NotificationCategory.Transactional) return true;
        return Evaluate(await GetSettingsAsync(userId, ct), channel, category);
    }

    public async Task<UserNotificationSettings> GetSettingsAsync(int userId, CancellationToken ct = default)
    {
        try
        {
            // Contexte dédié : la lecture ne doit ni dépendre ni toucher du suivi
            // de modifications de l'appelant.
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var settings = await db.UserNotificationSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == userId, ct);
            if (settings != null) return settings;
        }
        catch (Exception ex)
        {
            // Préférences illisibles : on envoie (valeurs par défaut) plutôt que
            // de perdre silencieusement une notification.
            _logger.LogWarning(ex, "Préférences de notification illisibles pour l'utilisateur {UserId} : valeurs par défaut appliquées", userId);
        }
        return new UserNotificationSettings { UserId = userId };
    }

    public async Task<List<string>> FilterEmailsAsync(IEnumerable<string> emails, NotificationCategory category, CancellationToken ct = default)
    {
        var list = emails.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (category == NotificationCategory.Transactional || list.Count == 0) return list;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var lowered = list.Select(e => e.ToLower()).ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => lowered.Contains(u.Email.ToLower()))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct);
        var userIds = users.Select(u => u.Id).ToList();
        var settingsByUser = (await db.UserNotificationSettings.AsNoTracking()
                .Where(s => userIds.Contains(s.UserId))
                .ToListAsync(ct))
            .GroupBy(s => s.UserId)
            .ToDictionary(g => g.Key, g => g.First());
        var byEmail = users
            .GroupBy(u => u.Email.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => settingsByUser.GetValueOrDefault(g.First().Id));

        // Une adresse sans compte (cible saisie à la main) n'a pas de préférence :
        // valeurs par défaut.
        return list
            .Where(e => Evaluate(byEmail.GetValueOrDefault(e.ToLowerInvariant()) ?? new UserNotificationSettings(), NotificationChannel.Email, category))
            .ToList();
    }

    /// <summary>
    /// Règle unique. Sans enregistrement de préférences, les valeurs par défaut
    /// de l'entité s'appliquent (tout activé sauf Promotions).
    /// </summary>
    public static bool Evaluate(UserNotificationSettings s, NotificationChannel channel, NotificationCategory category)
    {
        if (category == NotificationCategory.Transactional) return true;

        var channelAllowed = channel switch
        {
            NotificationChannel.Push => s.PushNotifications,
            NotificationChannel.Email => s.EmailNotifications,
            _ => true, // la liste in-app n'a pas d'interrupteur de canal
        };
        if (!channelAllowed) return false;

        return category switch
        {
            NotificationCategory.CourseCommunity => s.CourseCommunity,
            NotificationCategory.LearningReminders => s.LearningReminders,
            NotificationCategory.Promotions => s.Promotions,
            NotificationCategory.Newsletters => s.Newsletters,
            _ => true,
        };
    }
}
