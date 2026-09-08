using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Envoie la notification d'un message direct programmé au moment défini
/// (US-MSG, Module 7). Le message est déjà en base dès sa création — c'est la
/// visibilité côté destinataire (filtrée par date dans MessagesController) et
/// la notification qui sont différées jusqu'à l'heure programmée.
/// </summary>
public class ScheduledMessageDeliveryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledMessageDeliveryService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public ScheduledMessageDeliveryService(IServiceScopeFactory scopeFactory, ILogger<ScheduledMessageDeliveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScheduledMessageDeliveryService démarré (intervalle: 1 minute)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeliverDueMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'envoi des messages programmés");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task DeliverDueMessagesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();

        var now = DateTime.UtcNow;
        var due = await db.DirectMessages
            .Where(m => !m.ScheduledNotificationSent && m.ScheduledSendAt != null && m.ScheduledSendAt <= now)
            .ToListAsync(ct);

        if (due.Count == 0) return;

        foreach (var msg in due)
        {
            msg.ScheduledNotificationSent = true;
            var preview = msg.Type == "text" ? Truncate(msg.Content, 80) : "📎 Pièce jointe";
            await ntfy.PublishAsync($"winplus-user-{msg.ToUserId}", "Nouveau message",
                preview ?? "Nouveau message", userId: msg.ToUserId, type: "DirectMessage");
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("{Count} message(s) programmé(s) délivré(s)", due.Count);
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength] + "…";
}
