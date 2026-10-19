using System.Text.Json;
using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Marketing;

/// <summary>
/// Lot 10.3 : enregistre un achat confirmé dans le registre des conversions
/// et prépare un envoi par destination configurée. Appelé depuis la
/// confirmation de paiement (webhook NotchPay et synchronisation de statut)
/// et depuis l'activation immédiate d'un abonnement payé par le solde.
///
/// Garanties : ne lève jamais d'exception, n'appelle jamais le réseau (l'envoi
/// est fait par <see cref="ConversionDispatchService"/> en arrière-plan), un
/// seul enregistrement par transaction (index unique), et rien n'est gardé
/// sans consentement mesure ou publicité.
/// </summary>
public interface IConversionTracker
{
    Task RecordOrderPurchaseAsync(int orderId, CancellationToken ct = default);
    Task RecordTutorBookingPurchaseAsync(string notchpayReference, CancellationToken ct = default);
}

/// <summary>Réveil du service d'envoi dès qu'une conversion est enregistrée.</summary>
public sealed class ConversionSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try { if (_signal.CurrentCount == 0) _signal.Release(); }
        catch (SemaphoreFullException) { /* déjà signalé */ }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

/// <summary>
/// Logique d'enregistrement, sur un contexte fourni (testable). Le traceur
/// de production l'appelle dans un scope dédié, pour ne jamais mêler ses
/// écritures à celles de l'appelant.
/// </summary>
public sealed class ConversionRecorder
{
    /// <summary>Recharges de portefeuille : ce n'est pas une vente.</summary>
    public const string TopUpPrefix = "WP-RCH-";

    private readonly ApplicationDbContext _db;
    private readonly IEnumerable<IConversionDestination> _destinations;

    public ConversionRecorder(ApplicationDbContext db, IEnumerable<IConversionDestination> destinations)
    {
        _db = db;
        _destinations = destinations;
    }

    /// <returns>La conversion créée, ou null (non payée, sans consentement, déjà enregistrée).</returns>
    public async Task<ConversionEvent?> RecordOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Include(o => o.Items).ThenInclude(i => i.Subject)
            .Include(o => o.Items).ThenInclude(i => i.Course)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null || !PaidOrderStatus.IsPaid(order.Status) || order.TotalAmount <= 0) return null;
        if (order.OrderNumber.StartsWith(TopUpPrefix, StringComparison.OrdinalIgnoreCase)) return null;

        var occurred = order.CompletedDate ?? DateTime.UtcNow;
        var attribution = order.Attribution ?? await LastTouchAsync(order.UserId, occurred, ct);

        var isSubscription = order.OrderNumber.StartsWith("SUB-", StringComparison.OrdinalIgnoreCase);
        var items = order.Items.Select(i => new Dictionary<string, object?>
        {
            ["item_id"] = i.CourseId.HasValue ? $"formation-{i.CourseId}" : $"epreuve-{i.SubjectId}",
            ["item_name"] = Truncate(i.Course?.Title ?? i.Subject?.Title, 100),
            ["item_category"] = i.CourseId.HasValue ? "formation" : "epreuve",
            ["price"] = decimal.Round(i.PriceAtPurchase, 0),
            ["quantity"] = 1,
        }).ToList();
        var category = isSubscription ? "abonnement"
            : items.Count == 0 ? "mixte"
            : items.Select(i => (string)i["item_category"]!).Distinct().Count() == 1 ? (string)items[0]["item_category"]! : "mixte";
        if (isSubscription)
            items = new() { new() { ["item_id"] = "abonnement", ["item_name"] = "Abonnement WinPlus", ["item_category"] = "abonnement", ["price"] = decimal.Round(order.TotalAmount, 0), ["quantity"] = 1 } };

        return await SaveAsync(order.OrderNumber, category, "order", order.Id, order.UserId, order.TotalAmount, occurred,
            attribution, JsonSerializer.Serialize(items), ct);
    }

    public async Task<ConversionEvent?> RecordTutorBookingAsync(string reference, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var booking = await _db.TutorBookings.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(b => b.NotchpayReference == reference, ct);
        if (booking == null || booking.PaymentStatus != "completed" || booking.Status == "cancelled" || booking.PriceXaf <= 0)
            return null;

        var occurred = booking.UpdatedAt ?? DateTime.UtcNow;
        var payer = booking.PayerUserId ?? booking.BookedByUserId ?? booking.StudentUserId;
        var attribution = await LastTouchAsync(payer, occurred, ct);
        var items = new[]
        {
            new Dictionary<string, object?>
            {
                ["item_id"] = $"repetiteur-{booking.TutorProfileId}",
                ["item_name"] = Truncate(string.IsNullOrWhiteSpace(booking.Subject) ? "Séance de répétiteur" : $"Séance de répétiteur : {booking.Subject}", 100),
                ["item_category"] = "repetiteur",
                ["price"] = decimal.Round(booking.PriceXaf, 0),
                ["quantity"] = 1,
            },
        };
        return await SaveAsync(reference, "repetiteur", "tutor_booking", booking.Id, payer, booking.PriceXaf, occurred,
            attribution, JsonSerializer.Serialize(items), ct);
    }

    private async Task<MarketingAttribution?> LastTouchAsync(int? userId, DateTime occurred, CancellationToken ct)
    {
        if (userId is not int id) return null;
        var last = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == id).Select(u => u.LastTouch).FirstOrDefaultAsync(ct);
        if (last?.CapturedAt is DateTime captured && (captured > occurred.AddMinutes(5) || captured < occurred - AttributionRules.ClickWindow))
            return null;
        return last;
    }

    private async Task<ConversionEvent?> SaveAsync(string transactionId, string category, string sourceType, int sourceId,
        int? userId, decimal value, DateTime occurred, MarketingAttribution? attribution, string itemsJson, CancellationToken ct)
    {
        // Décision Q15 b : sans consentement mesure ni publicité, aucune trace.
        if (attribution == null || (!attribution.ConsentAnalytics && !attribution.ConsentAds)) return null;
        if (await _db.ConversionEvents.AnyAsync(c => c.EventName == "purchase" && c.TransactionId == transactionId, ct)) return null;

        var conversion = new ConversionEvent
        {
            TransactionId = transactionId,
            EventName = "purchase",
            Category = category,
            SourceType = sourceType,
            SourceId = sourceId,
            UserId = userId,
            ValueXaf = value,
            Currency = "XAF",
            OccurredAt = DateTime.SpecifyKind(occurred, DateTimeKind.Utc),
            Gclid = attribution.ConsentAds ? attribution.Gclid : null,
            Gbraid = attribution.ConsentAds ? attribution.Gbraid : null,
            Wbraid = attribution.ConsentAds ? attribution.Wbraid : null,
            GaClientId = attribution.ConsentAnalytics ? attribution.GaClientId : null,
            GaSessionId = attribution.ConsentAnalytics ? attribution.GaSessionId : null,
            // Lot 10.3 bis (Meta) : seulement avec le consentement publicité.
            Fbc = AttributionRules.FbcFor(attribution),
            Fbp = attribution.ConsentAds ? attribution.Fbp : null,
            ClientIp = attribution.ConsentAds ? attribution.ClientIp : null,
            ClientUserAgent = attribution.ConsentAds ? attribution.ClientUserAgent : null,
            LandingPage = attribution.LandingPage,
            UtmSource = attribution.UtmSource,
            UtmMedium = attribution.UtmMedium,
            UtmCampaign = attribution.UtmCampaign,
            ConsentAnalytics = attribution.ConsentAnalytics,
            ConsentAds = attribution.ConsentAds,
            ItemsJson = itemsJson,
        };
        foreach (var destination in _destinations.Where(d => d.IsConfigured && d.Accepts(conversion)))
            conversion.Deliveries.Add(new ConversionDelivery { Destination = destination.Name, NextAttemptAt = DateTime.UtcNow });

        _db.ConversionEvents.Add(conversion);
        try
        {
            await _db.SaveChangesAsync(ct);
            return conversion;
        }
        catch (DbUpdateException)
        {
            // Course entre deux confirmations (webhook et synchronisation) :
            // l'index unique a gardé la première, celle-ci est abandonnée.
            _db.Entry(conversion).State = EntityState.Detached;
            foreach (var d in conversion.Deliveries) _db.Entry(d).State = EntityState.Detached;
            return null;
        }
    }

    private static string? Truncate(string? v, int max) =>
        string.IsNullOrEmpty(v) ? v : v.Length > max ? v[..max] : v;
}

public sealed class ConversionTracker : IConversionTracker
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ConversionSignal _signal;
    private readonly ILogger<ConversionTracker> _logger;

    public ConversionTracker(IServiceScopeFactory scopes, ConversionSignal signal, ILogger<ConversionTracker> logger)
    {
        _scopes = scopes;
        _signal = signal;
        _logger = logger;
    }

    public Task RecordOrderPurchaseAsync(int orderId, CancellationToken ct = default) =>
        RunAsync(r => r.RecordOrderAsync(orderId, ct), $"commande {orderId}");

    public Task RecordTutorBookingPurchaseAsync(string notchpayReference, CancellationToken ct = default) =>
        RunAsync(r => r.RecordTutorBookingAsync(notchpayReference, ct), $"réservation {notchpayReference}");

    private async Task RunAsync(Func<ConversionRecorder, Task<ConversionEvent?>> record, string label)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var recorder = new ConversionRecorder(
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                scope.ServiceProvider.GetServices<IConversionDestination>());
            var created = await record(recorder);
            if (created?.Deliveries.Count > 0) _signal.Notify();
        }
        catch (Exception ex)
        {
            // La mesure ne doit jamais faire échouer une confirmation de paiement.
            _logger.LogWarning(ex, "Conversion non enregistrée ({Label})", label);
        }
    }
}

/// <summary>
/// Envoi des conversions en attente : reprise avec délai croissant sur
/// erreur temporaire, abandon après <see cref="MaxAttempts"/> tentatives ou
/// sur refus définitif. Un envoi réussi n'est jamais refait.
/// </summary>
public sealed class ConversionDispatcher
{
    public const int MaxAttempts = 8;

    private static readonly TimeSpan[] Backoff =
    {
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1),
        TimeSpan.FromHours(3), TimeSpan.FromHours(6), TimeSpan.FromHours(12), TimeSpan.FromHours(24),
    };

    private readonly ApplicationDbContext _db;
    private readonly IReadOnlyDictionary<string, IConversionDestination> _destinations;

    public ConversionDispatcher(ApplicationDbContext db, IEnumerable<IConversionDestination> destinations)
    {
        _db = db;
        _destinations = destinations.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
    }

    public static TimeSpan DelayAfter(int attempts) => Backoff[Math.Clamp(attempts - 1, 0, Backoff.Length - 1)];

    /// <returns>Nombre d'envois réussis.</returns>
    public async Task<int> ProcessDueAsync(DateTime nowUtc, int batchSize = 50, CancellationToken ct = default)
    {
        var configured = _destinations.Values.Where(d => d.IsConfigured).Select(d => d.Name).ToList();
        if (configured.Count == 0) return 0;

        var due = await _db.ConversionDeliveries
            .Include(d => d.ConversionEvent)
            .Where(d => d.Status == ConversionDeliveryStatus.Pending && d.NextAttemptAt <= nowUtc && configured.Contains(d.Destination))
            .OrderBy(d => d.NextAttemptAt)
            .Take(batchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var delivery in due)
        {
            if (ct.IsCancellationRequested) break;
            var destination = _destinations[delivery.Destination];
            ConversionSendResult result;
            try { result = await destination.SendAsync(delivery.ConversionEvent!, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { result = new(ConversionSendOutcome.Retry, ex.GetType().Name); }

            delivery.Attempts++;
            switch (result.Outcome)
            {
                case ConversionSendOutcome.Sent:
                    delivery.Status = ConversionDeliveryStatus.Sent;
                    delivery.SentAt = nowUtc;
                    delivery.LastError = null;
                    sent++;
                    break;
                case ConversionSendOutcome.Rejected:
                    delivery.Status = ConversionDeliveryStatus.Failed;
                    delivery.LastError = Trim(result.Error);
                    break;
                default:
                    delivery.LastError = Trim(result.Error);
                    if (delivery.Attempts >= MaxAttempts) delivery.Status = ConversionDeliveryStatus.Failed;
                    else delivery.NextAttemptAt = nowUtc + DelayAfter(delivery.Attempts);
                    break;
            }
            // Enregistré après chaque envoi : un arrêt du service ne fait pas renvoyer ce qui est parti.
            await _db.SaveChangesAsync(ct);
        }
        return sent;
    }

    private static string? Trim(string? v) => v == null ? null : v.Length > 1000 ? v[..1000] : v;
}

/// <summary>Service en arrière-plan : vide la file à chaque signal, et au moins une fois par minute.</summary>
public sealed class ConversionDispatchService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ConversionSignal _signal;
    private readonly MarketingConfig _config;
    private readonly ILogger<ConversionDispatchService> _logger;

    public ConversionDispatchService(IServiceScopeFactory scopes, ConversionSignal signal, MarketingConfig config, ILogger<ConversionDispatchService> logger)
    {
        _scopes = scopes;
        _signal = signal;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Sans destination configurée, le service reste inactif et silencieux.
        if (!_config.AnyConfigured) return;
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var dispatcher = new ConversionDispatcher(
                    scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                    scope.ServiceProvider.GetServices<IConversionDestination>());
                await dispatcher.ProcessDueAsync(DateTime.UtcNow, ct: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Envoi des conversions en attente interrompu, reprise au prochain cycle");
            }

            try { await _signal.WaitAsync(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
