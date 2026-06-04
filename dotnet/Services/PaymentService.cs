using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Repositories;

namespace Backend.Services;

/// <summary>
/// Interface pour le service des paiements
/// </summary>
public interface IPaymentService
{
    Task<PaymentResponse> CreatePaymentAsync(int userId, CreatePaymentRequest request);
    Task<PaymentResponse> GetPaymentByIdAsync(int id);
    Task<PaymentResponse> GetPaymentByOrderIdAsync(int orderId);
    Task<PaymentListResponse> GetUserPaymentsAsync(int userId, int page = 1, int limit = 50);
    Task<PaymentListResponse> GetPaymentsByStatusAsync(string status, int page = 1, int limit = 50);
    Task<PaymentResponse> ConfirmPaymentAsync(int id, ConfirmPaymentRequest request);
    Task<PaymentResponse> RefundPaymentAsync(int id, RefundPaymentRequest request);
    Task<PaymentResponse> RetryPaymentAsync(int id, RetryPaymentRequest request);
    Task<bool> CancelPaymentAsync(int id);
    Task<List<PaymentResponse>> GetPendingPaymentsAsync();
    Task<List<PaymentResponse>> GetFailedPaymentsAsync();
}

/// <summary>
/// Service pour les paiements
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _repository;
    private readonly IOrderService _orderService;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(IPaymentRepository repository, IOrderService orderService, ILogger<PaymentService> logger)
    {
        _repository = repository;
        _orderService = orderService;
        _logger = logger;
    }

    public async Task<PaymentResponse> CreatePaymentAsync(int userId, CreatePaymentRequest request)
    {
        try
        {
            // Vérifier que la commande existe
            var order = await _orderService.GetOrderByIdAsync(request.OrderId);
            if (order == null)
                throw new ArgumentException("Commande non trouvée");

            var payment = new Payment
            {
                OrderId = request.OrderId,
                UserId = userId,
                Amount = request.Amount,
                Currency = request.Currency,
                PaymentMethod = request.PaymentMethod,
                Description = request.Description,
                Status = "pending",
                Metadata = request.Metadata,
                InitiatedAt = DateTime.UtcNow
            };

            var created = await _repository.CreateAsync(payment);
            _logger.LogInformation("Paiement créé: {PaymentId} pour la commande {OrderId}", created.Id, request.OrderId);

            return MapToResponse(created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la création du paiement");
            throw;
        }
    }

    public async Task<PaymentResponse> GetPaymentByIdAsync(int id)
    {
        var payment = await _repository.GetByIdAsync(id);
        if (payment == null)
            throw new ArgumentException("Paiement non trouvé");

        return MapToResponse(payment);
    }

    public async Task<PaymentResponse> GetPaymentByOrderIdAsync(int orderId)
    {
        var payment = await _repository.GetByOrderIdAsync(orderId);
        if (payment == null)
            throw new ArgumentException("Paiement non trouvé pour cette commande");

        return MapToResponse(payment);
    }

    public async Task<PaymentListResponse> GetUserPaymentsAsync(int userId, int page = 1, int limit = 50)
    {
        var payments = await _repository.GetByUserIdAsync(userId, page, limit);
        var total = await _repository.GetTotalCountAsync();

        return new PaymentListResponse
        {
            Payments = payments.Select(MapToResponse).ToList(),
            Total = total,
            Page = page,
            Limit = limit
        };
    }

    public async Task<PaymentListResponse> GetPaymentsByStatusAsync(string status, int page = 1, int limit = 50)
    {
        var payments = await _repository.GetByStatusAsync(status, page, limit);
        var total = await _repository.GetTotalCountAsync();

        return new PaymentListResponse
        {
            Payments = payments.Select(MapToResponse).ToList(),
            Total = total,
            Page = page,
            Limit = limit
        };
    }

    public async Task<PaymentResponse> ConfirmPaymentAsync(int id, ConfirmPaymentRequest request)
    {
        try
        {
            var payment = await _repository.GetByIdAsync(id);
            if (payment == null)
                throw new ArgumentException("Paiement non trouvé");

            if (payment.Status != "pending")
                throw new InvalidOperationException("Seuls les paiements en attente peuvent être confirmés");

            payment.Status = "completed";
            payment.TransactionId = request.TransactionId ?? payment.TransactionId;
            payment.ProcessedAt = DateTime.UtcNow;
            payment.CompletedAt = DateTime.UtcNow;

            var updated = await _repository.UpdateAsync(payment);
            _logger.LogInformation("Paiement confirmé: {PaymentId}", id);

            return MapToResponse(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la confirmation du paiement");
            throw;
        }
    }

    public async Task<PaymentResponse> RefundPaymentAsync(int id, RefundPaymentRequest request)
    {
        try
        {
            var payment = await _repository.GetByIdAsync(id);
            if (payment == null)
                throw new ArgumentException("Paiement non trouvé");

            if (payment.Status != "completed")
                throw new InvalidOperationException("Seuls les paiements complétés peuvent être remboursés");

            decimal refundAmount = request.Amount ?? payment.Amount;
            if (refundAmount > payment.Amount)
                throw new ArgumentException("Le montant du remboursement ne peut pas dépasser le montant du paiement");

            payment.Status = "refunded";
            payment.Amount = refundAmount;
            payment.UpdatedAt = DateTime.UtcNow;

            var updated = await _repository.UpdateAsync(payment);
            _logger.LogInformation("Paiement remboursé: {PaymentId} montant: {Amount}", id, refundAmount);

            return MapToResponse(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du remboursement du paiement");
            throw;
        }
    }

    public async Task<PaymentResponse> RetryPaymentAsync(int id, RetryPaymentRequest request)
    {
        try
        {
            var payment = await _repository.GetByIdAsync(id);
            if (payment == null)
                throw new ArgumentException("Paiement non trouvé");

            if (payment.Status != "failed")
                throw new InvalidOperationException("Seuls les paiements échoués peuvent être réessayés");

            payment.RetryCount = (payment.RetryCount ?? 0) + 1;
            if (payment.RetryCount > 3)
                throw new InvalidOperationException("Le nombre maximum de tentatives a été atteint");

            payment.Status = "pending";
            payment.PaymentMethod = request.PaymentMethod ?? payment.PaymentMethod;
            payment.ErrorMessage = null;
            payment.NextRetryAt = DateTime.UtcNow.AddMinutes(5 * (payment.RetryCount ?? 0)); // Délai exponentiel

            var updated = await _repository.UpdateAsync(payment);
            _logger.LogInformation("Paiement réessayé: {PaymentId} tentative: {RetryCount}", id, payment.RetryCount);

            return MapToResponse(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du réessai du paiement");
            throw;
        }
    }

    public async Task<bool> CancelPaymentAsync(int id)
    {
        try
        {
            var payment = await _repository.GetByIdAsync(id);
            if (payment == null)
                throw new ArgumentException("Paiement non trouvé");

            if (payment.Status == "completed" || payment.Status == "refunded")
                throw new InvalidOperationException("Les paiements complétés ou remboursés ne peuvent pas être annulés");

            payment.Status = "cancelled";
            await _repository.UpdateAsync(payment);
            _logger.LogInformation("Paiement annulé: {PaymentId}", id);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'annulation du paiement");
            throw;
        }
    }

    public async Task<List<PaymentResponse>> GetPendingPaymentsAsync()
    {
        var payments = await _repository.GetPendingPaymentsAsync();
        return payments.Select(MapToResponse).ToList();
    }

    public async Task<List<PaymentResponse>> GetFailedPaymentsAsync()
    {
        var payments = await _repository.GetFailedPaymentsAsync();
        return payments.Select(MapToResponse).ToList();
    }

    private PaymentResponse MapToResponse(Payment payment)
    {
        return new PaymentResponse
        {
            Id = payment.Id,
            OrderId = payment.OrderId,
            UserId = payment.UserId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Status = payment.Status,
            PaymentMethod = payment.PaymentMethod,
            TransactionId = payment.TransactionId,
            Description = payment.Description,
            FeeAmount = payment.FeeAmount,
            InitiatedAt = payment.InitiatedAt,
            ProcessedAt = payment.ProcessedAt,
            CompletedAt = payment.CompletedAt,
            ErrorMessage = payment.ErrorMessage,
            RetryCount = payment.RetryCount,
            NextRetryAt = payment.NextRetryAt,
            CreatedAt = payment.CreatedAt,
            UpdatedAt = payment.UpdatedAt
        };
    }
}
