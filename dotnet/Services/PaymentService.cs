using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public interface IPaymentService
{
    // NotchPay operations
    Task<InitiatePaymentResponse> InitiateNotchPayAsync(int? userId, InitiatePaymentRequest request);
    Task<bool> HandleNotchPayWebhookAsync(string eventId, string eventType, NotchPayWebhookTransaction transaction);
    Task<PaymentStatusResponse> GetPaymentStatusAsync(int paymentId, int requestingUserId, bool isAdmin);
    Task<PaymentHistoryResponse> GetUserPaymentHistoryAsync(int userId, int page, int limit);
    Task<PaymentHistoryResponse> GetAllPaymentsAsync(int page, int limit, string? status);
    Task<PaymentHistoryResponse> GetPaymentsByUserAsync(int userId, int page, int limit);
    Task<InitiatePaymentResponse> RetryPaymentAsync(int paymentId, int requestingUserId);

    // Legacy operations
    Task<PaymentResponse> CreatePaymentAsync(int userId, CreatePaymentRequest request);
    Task<PaymentResponse> GetPaymentByIdAsync(int id);
    Task<PaymentResponse> GetPaymentByOrderIdAsync(int orderId);
    Task<PaymentListResponse> GetUserPaymentsAsync(int userId, int page = 1, int limit = 50);
    Task<PaymentListResponse> GetPaymentsByStatusAsync(string status, int page = 1, int limit = 50);
    Task<PaymentResponse> ConfirmPaymentAsync(int id, ConfirmPaymentRequest request);
    Task<PaymentResponse> RefundPaymentAsync(int id, RefundPaymentRequest request);
    Task<bool> CancelPaymentAsync(int id);
    Task<List<PaymentResponse>> GetPendingPaymentsAsync();
    Task<List<PaymentResponse>> GetFailedPaymentsAsync();
}

public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _repository;
    private readonly IOrderService _orderService;
    private readonly INotchPayService _notchPay;
    private readonly IUserService _userService;
    private readonly INtfyService _ntfy;
    private readonly IEmailService _email;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IPaymentRepository repository,
        IOrderService orderService,
        INotchPayService notchPay,
        IUserService userService,
        INtfyService ntfy,
        IEmailService email,
        ApplicationDbContext db,
        ILogger<PaymentService> logger)
    {
        _repository = repository;
        _orderService = orderService;
        _notchPay = notchPay;
        _userService = userService;
        _ntfy = ntfy;
        _email = email;
        _db = db;
        _logger = logger;
    }

    // ─── NotchPay operations ──────────────────────────────────────────────────

    public async Task<InitiatePaymentResponse> InitiateNotchPayAsync(int? userId, InitiatePaymentRequest request)
    {
        // [Required] + [Range(1,...)] garantissent que OrderId est non-null et valide ici
        var orderId = request.OrderId!.Value;

        var order = await _orderService.GetOrderByIdAsync(orderId)
            ?? throw new ArgumentException("Commande introuvable");

        // Résoudre l'email : depuis le compte si connecté, depuis la requête sinon
        string email;
        if (userId.HasValue)
        {
            var user = await _userService.GetUserByIdAsync(userId.Value);
            email = user?.Email ?? request.Email ?? $"user{userId}@winplus.cm";
        }
        else
        {
            email = request.Email ?? order.GuestEmail ?? "guest@winplus.cm";
        }

        var payment = await _repository.CreateAsync(new Payment
        {
            OrderId    = orderId,
            UserId     = userId,
            GuestEmail = userId.HasValue ? null : email,
            Amount     = request.Amount,
            Currency   = "XAF",
            PaymentMethod = "notchpay",
            PhoneNumber   = request.Phone,
            Description   = request.Description ?? $"Paiement commande #{orderId}",
            Status      = "pending",
            InitiatedAt = DateTime.UtcNow,
            ExpiresAt   = DateTime.UtcNow.AddHours(1)
        });

        try
        {
            var result = await _notchPay.InitiatePaymentAsync(
                request.Phone, request.Amount, orderId,
                payment.Description!, email);

            payment.NotchpayReference = result.Transaction?.Reference;
            payment.Status = MapNotchPayStatus(result.Transaction?.Status) ?? "pending";
            await _repository.UpdateAsync(payment);

            return new InitiatePaymentResponse
            {
                PaymentId = payment.Id,
                NotchpayReference = payment.NotchpayReference,
                Status = payment.Status,
                AuthorizationUrl = result.AuthorizationUrl,
                Amount = request.Amount,
                Currency = "XAF",
                Message = result.Message
            };
        }
        catch (Exception ex)
        {
            payment.Status = "failed";
            payment.ErrorMessage = ex.Message;
            await _repository.UpdateAsync(payment);
            _logger.LogError(ex, "Échec initiation NotchPay pour commande {OrderId}", orderId);
            throw;
        }
    }

    public async Task<bool> HandleNotchPayWebhookAsync(string eventId, string eventType, NotchPayWebhookTransaction transaction)
    {
        if (await _repository.IsWebhookEventProcessedAsync(eventId, "notchpay"))
        {
            _logger.LogInformation("Webhook NotchPay {EventId} déjà traité, ignoré", eventId);
            return true;
        }

        var payment = string.IsNullOrEmpty(transaction.Reference)
            ? null
            : await _repository.GetByNotchpayReferenceAsync(transaction.Reference);

        if (payment == null)
        {
            _logger.LogWarning("Paiement NotchPay introuvable pour référence {Ref}", transaction.Reference);
            await _repository.MarkWebhookEventProcessedAsync(eventId, "notchpay", eventType);
            return false;
        }

        payment.Status = MapNotchPayStatus(transaction.Status) ?? payment.Status;
        payment.Operator = transaction.Operator;

        if (payment.Status == "completed")
        {
            payment.CompletedAt = DateTime.UtcNow;
            payment.ProcessedAt = DateTime.UtcNow;
        }
        else if (payment.Status == "failed")
        {
            payment.ErrorCode = transaction.FailureCode;
            payment.ErrorMessage = transaction.FailureMessage;
        }

        await _repository.UpdateAsync(payment);
        await _repository.MarkWebhookEventProcessedAsync(eventId, "notchpay", eventType);

        if (payment.Status == "completed")
        {
            // Mettre à jour le statut de la commande
            try { await _orderService.UpdateOrderStatusAsync(payment.OrderId, "completed"); }
            catch (Exception ex) { _logger.LogError(ex, "Impossible de mettre à jour le statut de la commande {OrderId}", payment.OrderId); }

            // Notification push
            _ = _ntfy.PublishAsync(
                topic: $"winplus-user-{payment.UserId}",
                title: "Paiement reçu ✓",
                message: $"Votre paiement de {payment.Amount} XAF a été confirmé.",
                priority: "high",
                tags: new[] { "white_check_mark", "moneybag" },
                userId: payment.UserId,
                type: "payment");

            // Email de confirmation avec liste des épreuves achetées
            _ = SendConfirmationEmailAsync(payment);
        }
        else if (payment.Status == "failed")
        {
            _ = _ntfy.PublishAsync(
                topic: $"winplus-user-{payment.UserId}",
                title: "Paiement échoué",
                message: "Votre paiement n'a pas pu être traité. Veuillez réessayer.",
                priority: "high",
                tags: new[] { "x", "credit_card" },
                userId: payment.UserId,
                type: "payment");
        }

        _logger.LogInformation("Webhook NotchPay {EventId} traité → statut {Status}", eventId, payment.Status);
        return true;
    }

    private async Task SendConfirmationEmailAsync(Payment payment)
    {
        try
        {
            // Résoudre email + prénom
            string? recipientEmail = null;
            string firstName = "là";

            if (payment.UserId.HasValue)
            {
                var user = await _userService.GetUserByIdAsync(payment.UserId.Value);
                if (user != null)
                {
                    recipientEmail = user.Email;
                    firstName = user.FirstName ?? user.Email?.Split('@')[0] ?? "là";
                }
            }
            else
            {
                recipientEmail = payment.GuestEmail;
            }

            if (string.IsNullOrEmpty(recipientEmail))
            {
                _logger.LogWarning("Impossible d'envoyer l'email de confirmation : email introuvable pour paiement {PaymentId}", payment.Id);
                return;
            }

            // Charger les épreuves de la commande
            var purchasedItems = await _db.OrderItems
                .Where(oi => oi.OrderId == payment.OrderId)
                .Join(_db.Subjects, oi => oi.SubjectId, s => s.Id, (oi, s) => new { s.Title, SubjectId = oi.SubjectId })
                .ToListAsync();

            var items = purchasedItems
                .Where(i => !string.IsNullOrEmpty(i.Title))
                .Select(i => (i.Title, i.SubjectId));

            var reference = payment.NotchpayReference ?? $"WP-{payment.Id}";
            var completedAt = payment.CompletedAt ?? DateTime.UtcNow;

            await _email.SendPaymentConfirmationAsync(
                recipientEmail, firstName, payment.Amount, reference, completedAt, items);

            _logger.LogInformation("Email de confirmation envoyé à {Email} pour paiement {PaymentId}", recipientEmail, payment.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'envoi de l'email de confirmation pour paiement {PaymentId}", payment.Id);
        }
    }

    public async Task<PaymentStatusResponse> GetPaymentStatusAsync(int paymentId, int requestingUserId, bool isAdmin)
    {
        var payment = await _repository.GetByIdAsync(paymentId)
            ?? throw new ArgumentException("Paiement introuvable");

        if (!isAdmin && payment.UserId != requestingUserId)
            throw new UnauthorizedAccessException("Accès refusé");

        // Sync with NotchPay if still pending and reference exists
        if (payment.Status == "pending" && !string.IsNullOrEmpty(payment.NotchpayReference))
        {
            try
            {
                var tx = await _notchPay.GetTransactionStatusAsync(payment.NotchpayReference);
                var newStatus = MapNotchPayStatus(tx.Status);
                if (newStatus != null && newStatus != payment.Status)
                {
                    payment.Status = newStatus;
                    payment.Operator = tx.Operator ?? payment.Operator;
                    if (newStatus == "completed") payment.CompletedAt = DateTime.UtcNow;
                    if (newStatus == "failed")
                    {
                        payment.ErrorCode = tx.FailureCode;
                        payment.ErrorMessage = tx.FailureMessage;
                    }
                    await _repository.UpdateAsync(payment);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Impossible de synchroniser le statut NotchPay pour {Ref}", payment.NotchpayReference);
            }
        }

        return MapToStatusResponse(payment);
    }

    public async Task<PaymentHistoryResponse> GetUserPaymentHistoryAsync(int userId, int page, int limit)
    {
        var payments = await _repository.GetByUserIdAsync(userId, page, limit);
        var total = await _repository.GetCountByUserIdAsync(userId);
        return new PaymentHistoryResponse
        {
            Payments = payments.Select(MapToStatusResponse).ToList(),
            Total = total,
            Page = page,
            Limit = limit
        };
    }

    public async Task<PaymentHistoryResponse> GetAllPaymentsAsync(int page, int limit, string? status)
    {
        var payments = string.IsNullOrEmpty(status)
            ? await _repository.GetAllAsync(page, limit)
            : await _repository.GetByStatusAsync(status, page, limit);

        var total = await _repository.GetTotalCountAsync();

        return new PaymentHistoryResponse
        {
            Payments = payments.Select(MapToStatusResponse).ToList(),
            Total = total,
            Page = page,
            Limit = limit
        };
    }

    public async Task<PaymentHistoryResponse> GetPaymentsByUserAsync(int userId, int page, int limit)
    {
        var payments = await _repository.GetByUserIdAsync(userId, page, limit);
        var total = await _repository.GetCountByUserIdAsync(userId);
        return new PaymentHistoryResponse
        {
            Payments = payments.Select(MapToStatusResponse).ToList(),
            Total = total,
            Page = page,
            Limit = limit
        };
    }

    public async Task<InitiatePaymentResponse> RetryPaymentAsync(int paymentId, int requestingUserId)
    {
        var payment = await _repository.GetByIdAsync(paymentId)
            ?? throw new ArgumentException("Paiement introuvable");

        if (payment.UserId != requestingUserId)
            throw new UnauthorizedAccessException("Accès refusé");

        if (payment.Status != "failed")
            throw new InvalidOperationException("Seuls les paiements échoués peuvent être réessayés");

        if ((payment.RetryCount ?? 0) >= 3)
            throw new InvalidOperationException("Nombre maximum de tentatives atteint");

        var user = await _userService.GetUserByIdAsync(requestingUserId)
            ?? throw new ArgumentException("Utilisateur introuvable");

        payment.RetryCount = (payment.RetryCount ?? 0) + 1;
        payment.Status = "pending";
        payment.ErrorMessage = null;
        payment.ErrorCode = null;
        payment.ExpiresAt = DateTime.UtcNow.AddHours(1);

        try
        {
            var email = user.Email ?? $"user{requestingUserId}@winplus.cm";
            var result = await _notchPay.InitiatePaymentAsync(
                payment.PhoneNumber!, payment.Amount, payment.OrderId,
                payment.Description ?? $"Paiement commande #{payment.OrderId}", email);

            payment.NotchpayReference = result.Transaction?.Reference;
            payment.Status = MapNotchPayStatus(result.Transaction?.Status) ?? "pending";
            await _repository.UpdateAsync(payment);

            return new InitiatePaymentResponse
            {
                PaymentId = payment.Id,
                NotchpayReference = payment.NotchpayReference,
                Status = payment.Status,
                AuthorizationUrl = result.AuthorizationUrl,
                Amount = payment.Amount,
                Currency = "XAF",
                Message = result.Message
            };
        }
        catch (Exception ex)
        {
            payment.Status = "failed";
            payment.ErrorMessage = ex.Message;
            await _repository.UpdateAsync(payment);
            throw;
        }
    }

    // ─── Legacy operations ────────────────────────────────────────────────────

    public async Task<PaymentResponse> CreatePaymentAsync(int userId, CreatePaymentRequest request)
    {
        var order = await _orderService.GetOrderByIdAsync(request.OrderId)
            ?? throw new ArgumentException("Commande non trouvée");

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

    public async Task<PaymentResponse> GetPaymentByIdAsync(int id)
    {
        var payment = await _repository.GetByIdAsync(id)
            ?? throw new ArgumentException("Paiement non trouvé");
        return MapToResponse(payment);
    }

    public async Task<PaymentResponse> GetPaymentByOrderIdAsync(int orderId)
    {
        var payment = await _repository.GetByOrderIdAsync(orderId)
            ?? throw new ArgumentException("Paiement non trouvé pour cette commande");
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
        var payment = await _repository.GetByIdAsync(id)
            ?? throw new ArgumentException("Paiement non trouvé");

        if (payment.Status != "pending")
            throw new InvalidOperationException("Seuls les paiements en attente peuvent être confirmés");

        payment.Status = "completed";
        payment.TransactionId = request.TransactionId ?? payment.TransactionId;
        payment.ProcessedAt = DateTime.UtcNow;
        payment.CompletedAt = DateTime.UtcNow;

        return MapToResponse(await _repository.UpdateAsync(payment));
    }

    public async Task<PaymentResponse> RefundPaymentAsync(int id, RefundPaymentRequest request)
    {
        var payment = await _repository.GetByIdAsync(id)
            ?? throw new ArgumentException("Paiement non trouvé");

        if (payment.Status != "completed")
            throw new InvalidOperationException("Seuls les paiements complétés peuvent être remboursés");

        decimal refundAmount = request.Amount ?? payment.Amount;
        if (refundAmount > payment.Amount)
            throw new ArgumentException("Le montant du remboursement ne peut pas dépasser le montant du paiement");

        payment.Status = "refunded";
        payment.Amount = refundAmount;

        return MapToResponse(await _repository.UpdateAsync(payment));
    }

    public async Task<bool> CancelPaymentAsync(int id)
    {
        var payment = await _repository.GetByIdAsync(id)
            ?? throw new ArgumentException("Paiement non trouvé");

        if (payment.Status is "completed" or "refunded")
            throw new InvalidOperationException("Les paiements complétés ou remboursés ne peuvent pas être annulés");

        payment.Status = "cancelled";
        await _repository.UpdateAsync(payment);
        return true;
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

    // ─── Mapping helpers ─────────────────────────────────────────────────────

    private static string? MapNotchPayStatus(string? notchpayStatus) => notchpayStatus switch
    {
        "complete" or "completed" or "success" => "completed",
        "failed" or "failure" or "canceled" => "failed",
        "pending" or "processing" => "pending",
        "expired" => "expired",
        _ => null
    };

    private static PaymentStatusResponse MapToStatusResponse(Payment p) => new()
    {
        Id = p.Id,
        NotchpayReference = p.NotchpayReference,
        Status = p.Status,
        Amount = p.Amount,
        Currency = p.Currency,
        Operator = p.Operator,
        PhoneNumber = p.PhoneNumber,
        InitiatedAt = p.InitiatedAt,
        CompletedAt = p.CompletedAt,
        ErrorMessage = p.ErrorMessage,
        ErrorCode = p.ErrorCode
    };

    private static PaymentResponse MapToResponse(Payment p) => new()
    {
        Id = p.Id,
        OrderId = p.OrderId,
        UserId = p.UserId,
        Amount = p.Amount,
        Currency = p.Currency,
        Status = p.Status,
        PaymentMethod = p.PaymentMethod,
        TransactionId = p.TransactionId,
        NotchpayReference = p.NotchpayReference,
        PhoneNumber = p.PhoneNumber,
        Operator = p.Operator,
        Description = p.Description,
        FeeAmount = p.FeeAmount,
        InitiatedAt = p.InitiatedAt,
        ProcessedAt = p.ProcessedAt,
        CompletedAt = p.CompletedAt,
        ErrorMessage = p.ErrorMessage,
        ErrorCode = p.ErrorCode,
        RetryCount = p.RetryCount,
        NextRetryAt = p.NextRetryAt,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt
    };
}
