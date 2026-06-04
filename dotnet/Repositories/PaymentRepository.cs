using Backend.Models.Entities;
using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(int id);
    Task<Payment?> GetByOrderIdAsync(int orderId);
    Task<Payment?> GetByTransactionIdAsync(string transactionId);
    Task<Payment?> GetByNotchpayReferenceAsync(string reference);
    Task<List<Payment>> GetByUserIdAsync(int userId, int page = 1, int limit = 50);
    Task<int> GetCountByUserIdAsync(int userId);
    Task<List<Payment>> GetAllAsync(int page = 1, int limit = 50);
    Task<List<Payment>> GetByStatusAsync(string status, int page = 1, int limit = 50);
    Task<int> GetTotalCountAsync();
    Task<Payment> CreateAsync(Payment payment);
    Task<Payment> UpdateAsync(Payment payment);
    Task<bool> DeleteAsync(int id);
    Task<List<Payment>> GetPendingPaymentsAsync();
    Task<List<Payment>> GetFailedPaymentsAsync();
    Task<List<Payment>> GetRetryablePaymentsAsync();
    Task<List<Payment>> GetExpiredPendingPaymentsAsync(DateTime expiryThreshold);
    Task<bool> IsWebhookEventProcessedAsync(string eventId, string provider);
    Task MarkWebhookEventProcessedAsync(string eventId, string provider, string? eventType);
}

public class PaymentRepository : IPaymentRepository
{
    private readonly ApplicationDbContext _context;

    public PaymentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Payment?> GetByIdAsync(int id)
        => await _context.Payments.FindAsync(id);

    public async Task<Payment?> GetByOrderIdAsync(int orderId)
        => await _context.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId);

    public async Task<Payment?> GetByTransactionIdAsync(string transactionId)
        => await _context.Payments.FirstOrDefaultAsync(p => p.TransactionId == transactionId);

    public async Task<Payment?> GetByNotchpayReferenceAsync(string reference)
        => await _context.Payments.FirstOrDefaultAsync(p => p.NotchpayReference == reference);

    public async Task<List<Payment>> GetByUserIdAsync(int userId, int page = 1, int limit = 50)
        => await _context.Payments
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

    public async Task<int> GetCountByUserIdAsync(int userId)
        => await _context.Payments.CountAsync(p => p.UserId == userId);

    public async Task<List<Payment>> GetAllAsync(int page = 1, int limit = 50)
        => await _context.Payments
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

    public async Task<List<Payment>> GetByStatusAsync(string status, int page = 1, int limit = 50)
        => await _context.Payments
            .Where(p => p.Status == status)
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

    public async Task<int> GetTotalCountAsync()
        => await _context.Payments.CountAsync();

    public async Task<Payment> CreateAsync(Payment payment)
    {
        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();
        return payment;
    }

    public async Task<Payment> UpdateAsync(Payment payment)
    {
        payment.UpdatedAt = DateTime.UtcNow;
        _context.Payments.Update(payment);
        await _context.SaveChangesAsync();
        return payment;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var payment = await GetByIdAsync(id);
        if (payment == null) return false;
        _context.Payments.Remove(payment);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<List<Payment>> GetPendingPaymentsAsync()
        => await _context.Payments
            .Where(p => p.Status == "pending")
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

    public async Task<List<Payment>> GetFailedPaymentsAsync()
        => await _context.Payments
            .Where(p => p.Status == "failed")
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

    public async Task<List<Payment>> GetRetryablePaymentsAsync()
        => await _context.Payments
            .Where(p => p.Status == "failed"
                && p.RetryCount < 3
                && (p.NextRetryAt == null || p.NextRetryAt <= DateTime.UtcNow))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

    public async Task<List<Payment>> GetExpiredPendingPaymentsAsync(DateTime expiryThreshold)
        => await _context.Payments
            .Where(p => p.Status == "pending" && p.InitiatedAt < expiryThreshold)
            .ToListAsync();

    public async Task<bool> IsWebhookEventProcessedAsync(string eventId, string provider)
        => await _context.WebhookIdempotencyKeys
            .AnyAsync(k => k.EventId == eventId && k.Provider == provider);

    public async Task MarkWebhookEventProcessedAsync(string eventId, string provider, string? eventType)
    {
        _context.WebhookIdempotencyKeys.Add(new Models.Entities.WebhookIdempotencyKey
        {
            EventId = eventId,
            Provider = provider,
            EventType = eventType,
            ProcessedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }
}
