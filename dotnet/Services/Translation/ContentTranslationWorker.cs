using System.Threading.Channels;
using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Backend.Services.Translation;

/// <summary>File des contenus à traduire en tâche de fond (dédoublonnée).</summary>
public interface IContentTranslationQueue
{
    void Enqueue(ContentRef item);
    ValueTask<ContentRef> DequeueAsync(CancellationToken ct);
    bool TryDequeue(out ContentRef item);
}

public sealed class ContentTranslationQueue : IContentTranslationQueue
{
    private readonly Channel<ContentRef> _channel = Channel.CreateUnbounded<ContentRef>();
    public void Enqueue(ContentRef item) => _channel.Writer.TryWrite(item);
    public ValueTask<ContentRef> DequeueAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);
    public bool TryDequeue(out ContentRef item) => _channel.Reader.TryRead(out item!);
}

/// <summary>
/// Détecte, à chaque enregistrement EF, les contenus dont le français a changé
/// (création, modification, publication) : quel que soit l'écran ou l'API qui
/// écrit (admin, professeur, scripts), la traduction suit sans intervention.
///  - même sauvegarde : état « pending » (ou « review » pour une traduction humaine) ;
///  - après la sauvegarde : mise en file pour la tâche de fond.
/// L'enregistrement n'attend jamais la traduction et n'échoue jamais à cause d'elle.
/// </summary>
public sealed class ContentTranslationSaveInterceptor : SaveChangesInterceptor
{
    private static readonly Dictionary<Type, string[]> SourceProps = new()
    {
        // Lot 10.7 : changer la langue d'origine (Language) relance aussi le suivi.
        [typeof(Subject)] = new[] { "Title", "Description", "Category", "Level", "IsPublished", "Language" },
        [typeof(Exam)] = new[] { "Title", "Description", "ExamType", "Category", "Level", "Session", "Difficulty", "IsPublished", "Language" },
        [typeof(Course)] = new[] { "Title", "ShortDescription", "Description", "Tags", "Requirements", "Objectives", "Category", "Level", "Status", "Language" },
        [typeof(CourseSection)] = new[] { "Title", "Description" },
        [typeof(CourseLesson)] = new[] { "Title", "Description" },
        [typeof(ConcoursEvent)] = ContentTranslationService.ConcoursFields.Select(f => f.Fr).ToArray(),
    };

    private readonly IContentTranslationQueue _queue;
    // Une liste par contexte EF : plusieurs requêtes enregistrent en parallèle.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbContext, List<object>> _pending = new();

    public ContentTranslationSaveInterceptor(IContentTranslationQueue queue) => _queue = queue;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Flush(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Flush(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context != null) _pending.Remove(eventData.Context);
    }

    /// <summary>Entités dont un champ source a changé (exposé pour les tests).</summary>
    public static List<object> ChangedSources(DbContext? context)
    {
        var changed = new List<object>();
        if (context == null) return changed;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (!SourceProps.TryGetValue(entry.Entity.GetType(), out var props)) continue;
            var relevant = entry.State == EntityState.Added
                || (entry.State == EntityState.Modified && props.Any(p => entry.Metadata.FindProperty(p) != null && entry.Property(p).IsModified
                       && !Equals(entry.Property(p).OriginalValue, entry.Property(p).CurrentValue)
                       && !(entry.Property(p).OriginalValue is List<string> a && entry.Property(p).CurrentValue is List<string> b && a.SequenceEqual(b))));
            if (!relevant) continue;
            // Langue d'origine précisée au dépôt, sinon déduite (GCE, Form 1-5…).
            if (entry.State == EntityState.Added) ContentSourceLanguage.ApplyOnCreate(entry.Entity);
            if (entry.Entity is ITranslatableContent row) ContentTranslationService.MarkSourceChanged(row);
            if (entry.Entity is ConcoursEvent ev)
            {
                var meta = ConcoursTranslationMeta.Parse(ev.TranslationMetaJson);
                meta.Pending = true;
                ev.TranslationMetaJson = meta.ToJson();
            }
            changed.Add(entry.Entity);
        }
        return changed;
    }

    private void Collect(DbContext? context)
    {
        if (context == null) return;
        var changed = ChangedSources(context);
        _pending.Remove(context);
        if (changed.Count > 0) _pending.Add(context, changed);
    }

    private void Flush(DbContext? context)
    {
        if (context == null || !_pending.TryGetValue(context, out var items)) return;
        _pending.Remove(context);
        foreach (var e in items)
        {
            ContentRef? r = e switch
            {
                Subject s => new(ContentKinds.Subject, s.Id),
                Exam x => new(ContentKinds.Exam, x.Id),
                Course c => new(ContentKinds.Course, c.Id),
                CourseSection s => new(ContentKinds.Course, s.CourseId),
                CourseLesson l => new(ContentKinds.Course, l.CourseId),
                ConcoursEvent ev => new(ContentKinds.ConcoursEvent, ev.Id),
                _ => null,
            };
            if (r != null && r.Id > 0) _queue.Enqueue(r);
        }
    }
}

/// <summary>
/// Tâche de fond : traduit les contenus mis en file (quelques secondes après
/// l'enregistrement, doublons regroupés) et, toutes les heures, reprend les
/// traductions en attente ou en échec (WinAI indisponible, quota).
///
/// Le contenu antérieur au lot (état null) n'est PAS repris d'office : la
/// traduction initiale se lance par POST /api/admin/translations/translate-missing.
///
/// Configuration : ContentTranslation:Enabled (vrai par défaut),
/// ContentTranslation:RetryIntervalMinutes (60).
/// </summary>
public sealed class ContentTranslationWorker : BackgroundService
{
    private readonly IContentTranslationQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ContentTranslationWorker> _logger;
    private readonly bool _enabled;
    private readonly TimeSpan _retryInterval;
    private DateTime _nextRetry;

    public ContentTranslationWorker(IContentTranslationQueue queue, IServiceScopeFactory scopes, IConfiguration config,
        ILogger<ContentTranslationWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
        _enabled = config.GetValue("ContentTranslation:Enabled", true);
        _retryInterval = TimeSpan.FromMinutes(Math.Max(5, config.GetValue("ContentTranslation:RetryIntervalMinutes", 60)));
        _nextRetry = DateTime.UtcNow.AddMinutes(10);
    }

    /// <summary>Progression du dernier rattrapage (lu par l'écran admin).</summary>
    public static volatile string LastRun = "";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Traduction automatique des contenus désactivée (ContentTranslation:Enabled=false)");
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(1));
                ContentRef first;
                try { first = await _queue.DequeueAsync(timeout.Token); }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    if (DateTime.UtcNow >= _nextRetry) await RetryAsync(stoppingToken);
                    continue;
                }

                // Laisse arriver les autres écritures de la même action (formation + chapitres).
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                var batch = new HashSet<ContentRef> { first };
                while (_queue.TryDequeue(out var next)) batch.Add(next);
                await ProcessAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tâche de traduction des contenus");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task ProcessAsync(IEnumerable<ContentRef> refs, CancellationToken ct)
    {
        var list = refs.ToList();
        int done = 0, failed = 0, tokens = 0;
        foreach (var r in list)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ContentTranslationService>();
                var outcome = await service.TranslateAsync(r, ct: ct);
                tokens += outcome.TokensUsed;
                if (outcome.State == TranslationStates.Failed) failed++; else done++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                _logger.LogWarning(ex, "Traduction de {Kind} {Id} impossible", r.Kind, r.Id);
            }
            LastRun = $"{DateTime.UtcNow:O} : {done + failed}/{list.Count} traités, {failed} en échec, {tokens} tokens";
        }
        if (list.Count > 0) _logger.LogInformation("Traductions : {Done} faites, {Failed} en échec, {Tokens} tokens", done, failed, tokens);
    }

    private async Task RetryAsync(CancellationToken ct)
    {
        _nextRetry = DateTime.UtcNow.Add(_retryInterval);
        List<ContentRef> refs;
        using (var scope = _scopes.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ContentTranslationService>();
            try { refs = (await service.FindMissingAsync(includeNeverRequested: false, ct)).Refs; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Reprise des traductions : lecture impossible (script SQL passé ?)");
                return;
            }
        }
        if (refs.Count == 0) return;
        _logger.LogInformation("Reprise planifiée : {Count} contenu(s) à traduire", refs.Count);
        await ProcessAsync(refs, ct);
    }
}
