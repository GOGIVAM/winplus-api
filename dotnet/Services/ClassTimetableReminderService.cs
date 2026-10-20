using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Rappel de cours : toutes les 5 minutes, les créneaux d'emploi du temps qui
/// commencent dans les 10 à 15 prochaines minutes (heure du Cameroun, UTC+1)
/// déclenchent une notification aux élèves de la classe et à l'enseignant.
/// Un même créneau n'est rappelé qu'une fois par jour (mémoire du service).
/// </summary>
public sealed class ClassTimetableReminderService : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CameroonOffset = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ClassTimetableReminderService> _logger;
    private readonly HashSet<string> _sent = new();
    private DateOnly _sentDay;

    public ClassTimetableReminderService(IServiceScopeFactory scopeFactory, ILogger<ClassTimetableReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "ClassTimetableReminderService run failed."); }
            try { await Task.Delay(Tick, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var local = DateTime.UtcNow + CameroonOffset;
        var today = DateOnly.FromDateTime(local);
        if (today != _sentDay) { _sent.Clear(); _sentDay = today; }

        var dow = local.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)local.DayOfWeek;
        var from = local.AddMinutes(10).ToString("HH:mm");
        var to = local.AddMinutes(15).ToString("HH:mm");
        if (string.CompareOrdinal(to, from) < 0) return; // passage de minuit

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();

        var slots = await db.ClassTimetableSlots.AsNoTracking()
            .Where(s => s.DayOfWeek == dow && string.Compare(s.StartTime, from) >= 0 && string.Compare(s.StartTime, to) < 0)
            .Select(s => new { s.Id, s.StudyGroupId, s.StartTime, s.Subject, s.Room, s.TeacherUserId, group = s.StudyGroup != null ? s.StudyGroup.Name : "" })
            .ToListAsync(ct);

        foreach (var s in slots)
        {
            if (!_sent.Add($"{s.Id}")) continue;
            var students = await db.StudyGroupMembers.AsNoTracking()
                .Where(m => m.StudyGroupId == s.StudyGroupId && m.Role != "teacher").Select(m => m.UserId).ToListAsync(ct);
            var where = string.IsNullOrWhiteSpace(s.Room) ? string.Empty : $" · salle {s.Room}";
            foreach (var id in students)
            {
                try
                {
                    await ntfy.PublishAsync($"winplus-user-{id}", $"{s.Subject} à {s.StartTime}", $"Ton cours de {s.Subject} commence bientôt ({s.group}{where}).",
                        userId: id, type: "TimetableReminder", relatedEntityType: "ClassTimetableSlot", relatedEntityId: s.Id);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Rappel de cours non envoyé à {UserId}", id); }
            }
            if (s.TeacherUserId != null)
            {
                try
                {
                    await ntfy.PublishAsync($"winplus-user-{s.TeacherUserId}", $"Cours à {s.StartTime} · {s.group}", $"{s.Subject} avec {s.group}{where}. Pensez à remplir le cahier de textes après le cours.",
                        userId: s.TeacherUserId, type: "TimetableReminder", relatedEntityType: "ClassTimetableSlot", relatedEntityId: s.Id);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Rappel de cours non envoyé à l'enseignant {UserId}", s.TeacherUserId); }
            }
        }
    }
}
