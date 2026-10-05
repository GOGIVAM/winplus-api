using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public sealed record WalletTopUpStart(int TopUpId, int OrderId, int PaymentId, string Status, decimal AmountXaf, bool Duplicate);

/// <summary>
/// Recharge personnelle du portefeuille (lot 2, Module 3, décision §12).
///
/// Réutilise intégralement le parcours d'encaissement existant : une commande
/// porteuse sans ligne de contenu (montant = recharge, sans TVA : ce n'est pas
/// une vente) passe par PaymentService/NotchPay comme un achat. Seul l'effet de
/// la confirmation diffère : l'intention structurée <see cref="WalletTopUp"/>
/// est relue par <see cref="IWalletService.SyncOrderAsync"/>, qui écrit une
/// seule écriture <c>Recharge</c> par paiement confirmé.
/// </summary>
public interface IWalletTopUpService
{
    Task<WalletTopUpStart> StartAsync(int userId, decimal amount, string? operatorCode, string? phone, string? clientRequestId);
}

public class WalletTopUpService : IWalletTopUpService
{
    /// <summary>Bornes NotchPay de la devise XAF (documentation : minimum_amount 100, maximum_amount 50 000 000).</summary>
    public const decimal MinimumXaf = 100m;
    public const decimal ProviderMaximumXaf = 50_000_000m;

    private readonly ApplicationDbContext _db;
    private readonly IPaymentService _payments;
    private readonly ILogger<WalletTopUpService> _logger;

    public WalletTopUpService(ApplicationDbContext db, IPaymentService payments, ILogger<WalletTopUpService> logger)
    {
        _db = db;
        _payments = payments;
        _logger = logger;
    }

    public async Task<WalletTopUpStart> StartAsync(int userId, decimal amount, string? operatorCode, string? phone, string? clientRequestId)
    {
        var op = (operatorCode ?? string.Empty).Trim().ToLowerInvariant();
        if (op is not ("mtn" or "orange"))
            throw new WithdrawalRejectedException(400, "invalid_operator", "Opérateur invalide : choisis MTN MoMo ou Orange Money.");
        var normalizedPhone = WithdrawalService.NormalizePhone(phone)
            ?? throw new WithdrawalRejectedException(400, "invalid_phone", "Numéro Mobile Money invalide : 9 chiffres commençant par 6.");
        if (amount != decimal.Truncate(amount))
            throw new WithdrawalRejectedException(400, "invalid_amount", "Le montant doit être un nombre entier de FCFA.");
        if (amount < MinimumXaf)
            throw new WithdrawalRejectedException(400, "below_minimum", $"Le montant minimum de recharge est de {MinimumXaf:0} FCFA.");
        if (amount > ProviderMaximumXaf)
            throw new WithdrawalRejectedException(400, "above_provider_limit", $"Montant supérieur à la limite de NotchPay ({ProviderMaximumXaf:0} FCFA).");

        var user = await _db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.Role, u.IsActive, u.IsDeleted }).FirstOrDefaultAsync();
        if (user == null || !user.IsActive || user.IsDeleted)
            throw new WithdrawalRejectedException(403, "account_inactive", "Ce compte ne peut pas être rechargé (compte suspendu ou supprimé).");
        // Portefeuille professeur (lot 2). Le portefeuille parent rechargeable
        // relève du Module 14 (lot 3).
        if (!string.Equals(user.Role, "teacher", StringComparison.OrdinalIgnoreCase))
            throw new WithdrawalRejectedException(403, "not_eligible", "La recharge du portefeuille est réservée aux comptes professeur pour l'instant.");

        var requestId = string.IsNullOrWhiteSpace(clientRequestId) ? null : clientRequestId.Trim();
        if (requestId is { Length: > 64 }) requestId = requestId[..64];

        if (requestId != null)
        {
            var existing = await _db.WalletTopUps.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId && t.ClientRequestId == requestId);
            if (existing != null)
            {
                var paymentId = await _db.Payments.AsNoTracking().Where(p => p.OrderId == existing.OrderId)
                    .OrderByDescending(p => p.Id).Select(p => p.Id).FirstOrDefaultAsync();
                return new WalletTopUpStart(existing.Id, existing.OrderId, paymentId, existing.Status, existing.AmountXaf, true);
            }
        }

        var order = new Order
        {
            UserId = userId,
            OrderNumber = $"WP-RCH-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            TotalAmount = amount,
            Status = "pending",
            PaymentMethod = op,
            // Libellé lisible seulement : l'intention est portée par WalletTopUp.
            Notes = "Recharge du portefeuille WinPlus",
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var topUp = new WalletTopUp { UserId = userId, OrderId = order.Id, AmountXaf = amount, ClientRequestId = requestId };
        _db.WalletTopUps.Add(topUp);
        await _db.SaveChangesAsync();

        try
        {
            var payment = await _payments.InitiateNotchPayAsync(userId, new InitiatePaymentRequest
            {
                OrderId = order.Id,
                Phone = normalizedPhone,
                Amount = amount,
                Description = "Recharge du portefeuille WinPlus",
            });
            return new WalletTopUpStart(topUp.Id, order.Id, payment.PaymentId, payment.Status, amount, false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recharge {TopUpId} : paiement non lancé", topUp.Id);
            order.Status = "failed";
            topUp.Status = "failed";
            await _db.SaveChangesAsync();
            throw;
        }
    }
}
