using System.Collections.Concurrent;
using Backend.Models.DTOs;

namespace Backend.Services;

/// <summary>
/// Environnement de déploiement (integration.winplus.cm). La production ne
/// définit pas WINPLUS_ENV ; l'intégration le met à « integration » dans son
/// fichier d'environnement (/home/ubuntu/winplus-integration/dotnet/.env.dotnet).
/// L'intégration garde ASPNETCORE_ENVIRONMENT=Production (même configuration
/// que la production) : seule cette variable la distingue.
/// </summary>
public static class DeploymentEnvironment
{
    public const string VariableName = "WINPLUS_ENV";
    public const string Integration = "integration";

    public static bool IsIntegration(IConfiguration configuration) =>
        string.Equals(configuration[VariableName]?.Trim(), Integration, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Mode de paiement simulé (intégration seulement) : aucun appel à NotchPay, le
/// testeur confirme ou refuse chaque paiement par un bouton de l'interface de
/// test. Actif seulement si TOUTES les conditions sont réunies :
/// <list type="number">
///   <item>PAYMENTS_SIMULATED=true ;</item>
///   <item>WINPLUS_ENV=integration ;</item>
///   <item>App:FrontendUrl n'est pas le site de production (winplus.cm, www.winplus.cm) ;</item>
///   <item>aucune clé secrète NotchPay n'est configurée.</item>
/// </list>
/// En production, une variable PAYMENTS_SIMULATED=true posée par erreur ne
/// suffit donc jamais : la demande est refusée, consignée en erreur, et les
/// paiements restent réels.
/// </summary>
public static class PaymentSimulationGuard
{
    public const string VariableName = "PAYMENTS_SIMULATED";

    private static readonly string[] ProductionHosts = { "winplus.cm", "www.winplus.cm", "api.winplus.cm" };

    public static bool IsRequested(IConfiguration configuration) =>
        string.Equals(configuration[VariableName]?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Motif de refus, ou null si le mode simulé peut être activé.</summary>
    public static string? RefusalReason(IConfiguration configuration)
    {
        if (!IsRequested(configuration)) return $"{VariableName} n'est pas à true";
        if (!DeploymentEnvironment.IsIntegration(configuration))
            return $"{DeploymentEnvironment.VariableName} n'est pas « {DeploymentEnvironment.Integration} »";
        var frontend = configuration["App:FrontendUrl"];
        if (string.IsNullOrWhiteSpace(frontend)) return "App:FrontendUrl absent (hôte de l'environnement inconnu)";
        if (!Uri.TryCreate(frontend.Trim(), UriKind.Absolute, out var uri)) return "App:FrontendUrl illisible";
        if (ProductionHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            return $"App:FrontendUrl désigne la production ({uri.Host})";
        if (!string.IsNullOrWhiteSpace(configuration["NotchPay:SecretKey"]))
            return "une clé secrète NotchPay est configurée";
        return null;
    }

    public static bool IsEnabled(IConfiguration configuration) => RefusalReason(configuration) == null;
}

/// <summary>Paiement simulé en mémoire (perdu au redémarrage du service : il expire alors comme un paiement abandonné).</summary>
public sealed class SimulatedPayment
{
    public required string Reference { get; init; }
    public required decimal Amount { get; init; }
    public required string CustomerEmail { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    /// <summary>pending | complete | failed.</summary>
    public string Status { get; set; } = "pending";
    public DateTime? DecidedAt { get; set; }
}

/// <summary>Registre partagé (singleton) des paiements et transferts simulés.</summary>
public sealed class SimulatedPaymentStore
{
    private readonly ConcurrentDictionary<string, SimulatedPayment> _payments = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, NotchPayTransfer> _transfers = new(StringComparer.Ordinal);

    public void Add(SimulatedPayment payment) => _payments[payment.Reference] = payment;
    public SimulatedPayment? Get(string reference) => _payments.TryGetValue(reference, out var p) ? p : null;

    public IReadOnlyList<SimulatedPayment> Pending(string? customerEmail) => _payments.Values
        .Where(p => p.Status == "pending")
        .Where(p => customerEmail == null || string.Equals(p.CustomerEmail, customerEmail, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(p => p.CreatedAt)
        .ToList();

    /// <summary>Fixe l'issue d'un paiement encore en attente ; false si inconnu ou déjà décidé.</summary>
    public bool Decide(string reference, bool success)
    {
        var p = Get(reference);
        if (p == null) return false;
        lock (p)
        {
            if (p.Status != "pending") return false;
            p.Status = success ? "complete" : "failed";
            p.DecidedAt = DateTime.UtcNow;
            return true;
        }
    }

    public void AddTransfer(NotchPayTransfer transfer) => _transfers[transfer.Reference!] = transfer;
    public NotchPayTransfer? GetTransfer(string idOrReference) =>
        _transfers.TryGetValue(idOrReference, out var t) ? t : _transfers.Values.FirstOrDefault(x => x.Id == idOrReference);
}

/// <summary>
/// Remplace <see cref="NotchPayService"/> quand <see cref="PaymentSimulationGuard"/>
/// l'autorise. La référence renvoyée est la référence marchande
/// (WP-, TBK-, SESS-, TEN-…), ce qui permet aux traitements existants
/// (webhook, relecture du statut, réconciliation) de retrouver le paiement
/// sans aucune branche propre à la simulation.
/// </summary>
public sealed class SimulatedNotchPayService : INotchPayService
{
    private readonly SimulatedPaymentStore _store;
    private readonly ILogger<SimulatedNotchPayService> _logger;

    public SimulatedNotchPayService(SimulatedPaymentStore store, ILogger<SimulatedNotchPayService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public Task<NotchPayInitiateResponse> InitiatePaymentAsync(string phone, decimal amount, int orderId, string description,
        string customerEmail, string customerName, string channel, string referencePrefix = "WP")
    {
        var reference = $"{referencePrefix}-{orderId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        _store.Add(new SimulatedPayment
        {
            Reference = reference,
            Amount = Math.Round(amount),
            CustomerEmail = customerEmail ?? string.Empty,
            Description = description ?? string.Empty,
            Channel = channel ?? string.Empty,
            Phone = NotchPayService.MaskPhone(phone),
        });
        _logger.LogWarning("PAIEMENT SIMULÉ créé {Ref} ({Amount} XAF) : aucun appel à NotchPay", reference, Math.Round(amount));
        return Task.FromResult(new NotchPayInitiateResponse
        {
            Status = "Accepted",
            Message = "Paiement simulé (intégration) : confirmez-le ou refusez-le depuis le panneau de test.",
            Transaction = new NotchPayTransaction
            {
                Reference = reference,
                Status = "pending",
                Amount = Math.Round(amount),
                Currency = "XAF",
                CreatedAt = DateTime.UtcNow,
            },
        });
    }

    public Task<NotchPayTransaction> GetTransactionStatusAsync(string reference)
    {
        var p = _store.Get(reference);
        return Task.FromResult(new NotchPayTransaction
        {
            Reference = reference,
            Status = p?.Status ?? "pending",
            Amount = p?.Amount,
            Currency = "XAF",
            Operator = p == null ? null : "simulation",
            UpdatedAt = p?.DecidedAt,
        });
    }

    /// <summary>Aucun webhook NotchPay n'est accepté en intégration : la confirmation passe par le panneau de test.</summary>
    public bool VerifyWebhookSignature(string payload, string signature) => false;

    public Task<NotchPayTransfer> CreateTransferAsync(NotchPayTransferRequest request)
    {
        var transfer = new NotchPayTransfer
        {
            Id = $"sim-{request.Reference}",
            Reference = request.Reference,
            Amount = Math.Round(request.AmountXaf),
            Currency = "XAF",
            Status = "complete",
            Channel = request.Channel,
            CompletedAt = DateTime.UtcNow,
        };
        _store.AddTransfer(transfer);
        _logger.LogWarning("TRANSFERT SIMULÉ {Ref} ({Amount} XAF) : aucun envoi Mobile Money", request.Reference, transfer.Amount);
        return Task.FromResult(transfer);
    }

    public Task<NotchPayTransfer?> GetTransferAsync(string idOrReference) => Task.FromResult(_store.GetTransfer(idOrReference));
}
