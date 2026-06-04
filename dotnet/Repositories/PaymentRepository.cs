using Backend.Models.Entities;
using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

/// <summary>
/// Interface pour le repository des paiements
/// </summary>
public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(int id);
    Task<Payment?> GetByOrderIdAsync(int orderId);
    Task<Payment?> GetByTransactionIdAsync(string transactionId);
    Task<List<Payment>> GetByUserIdAsync(int userId, int page = 1, int limit = 50);
    Task<List<Payment>> GetByStatusAsync(string status, int page = 1, int limit = 50);
    Task<int> GetTotalCountAsync();
    Task<Payment> CreateAsync(Payment payment);
    Task<Payment> UpdateAsync(Payment payment);
    Task<bool> DeleteAsync(int id);
    Task<List<Payment>> GetPendingPaymentsAsync();
    Task<List<Payment>> GetFailedPaymentsAsync();
    Task<List<Payment>> GetRetryablePaymentsAsync();
}

/// <summary>
/// Repository pour les paiements
/// </summary>
public class PaymentRepository : IPaymentRepository
{
    private readonly ApplicationDbContext _context;

    public PaymentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Payment?> GetByIdAsync(int id)
    {
        return await _context.Payments.FindAsync(id);
    }

    public async Task<Payment?> GetByOrderIdAsync(int orderId)
    {
        return await _context.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId);
    }

    public async Task<Payment?> GetByTransactionIdAsync(string transactionId)
    {
        return await _context.Payments.FirstOrDefaultAsync(p => p.TransactionId == transactionId);
    }

    public async Task<List<Payment>> GetByUserIdAsync(int userId, int page = 1, int limit = 50)
    {
        return await _context.Payments
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<Payment>> GetByStatusAsync(string status, int page = 1, int limit = 50)
    {
        return await _context.Payments
            .Where(p => p.Status == status)
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> GetTotalCountAsync()
    {
        return await _context.Payments.CountAsync();
    }

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
    {
        return await _context.Payments
            .Where(p => p.Status == "pending")
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Payment>> GetFailedPaymentsAsync()
    {
        return await _context.Payments
            .Where(p => p.Status == "failed")
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Payment>> GetRetryablePaymentsAsync()
    {
        return await _context.Payments
            .Where(p => p.Status == "failed" && p.RetryCount < 3 && (p.NextRetryAt == null || p.NextRetryAt <= DateTime.UtcNow))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }
}
