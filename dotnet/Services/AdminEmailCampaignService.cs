using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// E-mails de masse de l'administration (lot 9.7a, suivi 18.1).
///
/// Avant : envoi synchrone par lots de 10 dans la requête HTTP, sans
/// confirmation ; au-delà de quelques centaines de destinataires, nginx
/// coupait la requête à 90 s, l'administrateur voyait une erreur alors que
/// l'envoi continuait (ou s'arrêtait) sans rapport.
///
/// Maintenant : l'écran demande d'abord le nombre de destinataires
/// (<see cref="CountRecipientsAsync"/>), l'administrateur confirme, la
/// campagne est enregistrée (<see cref="QueueAsync"/>) et envoyée en tâche de
/// fond par <see cref="AdminEmailCampaignWorker"/>, qui met à jour le rapport
/// (envoyés, échecs, désabonnés) au fil de l'envoi.
/// </summary>
public interface IAdminEmailCampaignService
{
    Task<(int Recipients, int OptedOut)> CountRecipientsAsync(string target, string? customEmail);
    Task<AdminEmailCampaign> QueueAsync(string target, string? customEmail, string subject, string body, int? adminUserId);
}

public static class AdminEmailTargets
{
    public static readonly string[] All = { "all", "students", "teachers", "parents", "institutions", "custom" };
}

public class AdminEmailCampaignService : IAdminEmailCampaignService
{
    private readonly ApplicationDbContext _db;
    private readonly INotificationPreferenceService _preferences;

    public AdminEmailCampaignService(ApplicationDbContext db, INotificationPreferenceService preferences)
    {
        _db = db;
        _preferences = preferences;
    }

    /// <summary>Adresses visées par une cible, avant filtrage des préférences.</summary>
    public static async Task<List<string>> ResolveAddressesAsync(ApplicationDbContext db, string target, string? customEmail)
    {
        if (target == "custom")
            return string.IsNullOrWhiteSpace(customEmail) ? new List<string>() : new List<string> { customEmail.Trim() };

        var users = db.Users.AsNoTracking().Where(u => u.IsActive && !u.IsDeleted);
        users = target switch
        {
            "students" => users.Where(u => u.Role == "student"),
            "teachers" => users.Where(u => u.Role == "teacher"),
            "parents" => users.Where(u => u.Role == "parent"),
            "institutions" => users.Where(u => u.Role == "institution"),
            _ => users,
        };
        return await users.Select(u => u.Email).Distinct().ToListAsync();
    }

    public async Task<(int Recipients, int OptedOut)> CountRecipientsAsync(string target, string? customEmail)
    {
        var emails = await ResolveAddressesAsync(_db, target, customEmail);
        if (target == "custom") return (emails.Count, 0);
        // Module 22 / Partie 12.3 : une diffusion relève de la préférence
        // « Newsletter » ; EmailService la revérifie à chaque envoi.
        var allowed = await _preferences.FilterEmailsAsync(emails, NotificationCategory.Newsletters);
        return (allowed.Count, emails.Count - allowed.Count);
    }

    public async Task<AdminEmailCampaign> QueueAsync(string target, string? customEmail, string subject, string body, int? adminUserId)
    {
        var (recipients, optedOut) = await CountRecipientsAsync(target, customEmail);
        var campaign = new AdminEmailCampaign
        {
            Target = target,
            CustomEmail = target == "custom" ? customEmail?.Trim() : null,
            Subject = subject.Trim(),
            Body = body,
            RecipientCount = recipients,
            OptedOutCount = optedOut,
            Status = recipients == 0 ? "completed" : "queued",
            FinishedAt = recipients == 0 ? DateTime.UtcNow : null,
            CreatedByUserId = adminUserId,
            CreatedAt = DateTime.UtcNow,
        };
        _db.AdminEmailCampaigns.Add(campaign);
        await _db.SaveChangesAsync();
        return campaign;
    }
}

/// <summary>
/// Envoie en tâche de fond les campagnes en file (une à la fois, par lots de
/// 10 adresses). Une campagne est « réclamée » par une mise à jour
/// conditionnelle (queued → sending) : deux instances de l'API ne l'envoient
/// jamais deux fois. Une campagne restée « sending » après un arrêt du
/// processus est marquée « failed » au démarrage, avec ce qui a déjà été
/// envoyé : elle n'est jamais renvoyée en entier automatiquement.
/// </summary>
public sealed class AdminEmailCampaignWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdminEmailCampaignWorker> _logger;

    public AdminEmailCampaignWorker(IServiceScopeFactory scopeFactory, ILogger<AdminEmailCampaignWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await MarkInterruptedAsync(stoppingToken); }
        catch (Exception ex) { _logger.LogWarning(ex, "Campagnes e-mail : reprise au démarrage impossible (table absente ?)"); }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessNextAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Campagnes e-mail : erreur de traitement"); }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task MarkInterruptedAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.AdminEmailCampaigns.Where(c => c.Status == "sending")
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, "failed")
                .SetProperty(c => c.Error, "Envoi interrompu par un redémarrage du serveur.")
                .SetProperty(c => c.FinishedAt, DateTime.UtcNow), ct);
    }

    private async Task ProcessNextAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var nextId = await db.AdminEmailCampaigns.AsNoTracking()
            .Where(c => c.Status == "queued").OrderBy(c => c.CreatedAt).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
        if (nextId == null) return;

        var claimed = await db.AdminEmailCampaigns
            .Where(c => c.Id == nextId && c.Status == "queued")
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, "sending").SetProperty(c => c.StartedAt, DateTime.UtcNow), ct);
        if (claimed == 0) return;

        var campaign = await db.AdminEmailCampaigns.FirstAsync(c => c.Id == nextId, ct);
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var preferences = scope.ServiceProvider.GetRequiredService<INotificationPreferenceService>();

        try
        {
            var addresses = await AdminEmailCampaignService.ResolveAddressesAsync(db, campaign.Target, campaign.CustomEmail);
            if (campaign.Target != "custom")
            {
                var allowed = await preferences.FilterEmailsAsync(addresses, NotificationCategory.Newsletters);
                campaign.OptedOutCount = addresses.Count - allowed.Count;
                addresses = allowed;
            }
            campaign.RecipientCount = addresses.Count;
            await db.SaveChangesAsync(ct);

            var category = campaign.Target == "custom" ? NotificationCategory.Transactional : NotificationCategory.Newsletters;
            var html = BuildHtml(campaign.Body);
            for (var i = 0; i < addresses.Count; i += BatchSize)
            {
                ct.ThrowIfCancellationRequested();
                var results = await Task.WhenAll(addresses.Skip(i).Take(BatchSize)
                    .Select(a => email.SendGenericEmailAsync(a, campaign.Subject, html, category)));
                campaign.SentCount += results.Count(r => r);
                campaign.FailedCount += results.Count(r => !r);
                await db.SaveChangesAsync(ct);
            }

            campaign.Status = "completed";
            campaign.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Campagne e-mail {Id} terminée : {Sent} envoyés, {Failed} échecs, {OptedOut} désabonnés",
                campaign.Id, campaign.SentCount, campaign.FailedCount, campaign.OptedOutCount);
        }
        catch (OperationCanceledException)
        {
            campaign.Status = "failed";
            campaign.Error = "Envoi interrompu par l'arrêt du serveur.";
            campaign.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Campagne e-mail {Id} en échec", campaign.Id);
            campaign.Status = "failed";
            campaign.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            campaign.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    /// <summary>Texte brut converti en HTML minimal (échappé, sauts de ligne conservés).</summary>
    public static string BuildHtml(string body) => $"""
        <div style="font-family:sans-serif;font-size:15px;line-height:1.6;color:#1a1a1a;max-width:600px;margin:auto;padding:32px 24px">
          {System.Net.WebUtility.HtmlEncode(body).Replace("\n", "<br>")}
          <hr style="margin:32px 0;border:none;border-top:1px solid #e5e5e5">
          <p style="font-size:12px;color:#888">© {DateTime.UtcNow.Year} WinPlus · Yaoundé, Cameroun<br>
          <a href="https://winplus.cm/unsubscribe" style="color:#888">Se désabonner</a></p>
        </div>
        """;
}
