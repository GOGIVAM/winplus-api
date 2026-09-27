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
    Task<PaymentStatusResponse> GetPaymentStatusAsync(int paymentId, int? requestingUserId, bool isAdmin);
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
    private readonly IAffiliateService _affiliate;
    private readonly ISubscriptionActivationService _subscriptionActivation;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IPaymentRepository repository,
        IOrderService orderService,
        INotchPayService notchPay,
        IUserService userService,
        INtfyService ntfy,
        IEmailService email,
        IAffiliateService affiliate,
        ISubscriptionActivationService subscriptionActivation,
        ApplicationDbContext db,
        ILogger<PaymentService> logger)
    {
        _subscriptionActivation = subscriptionActivation;
        _repository = repository;
        _orderService = orderService;
        _notchPay = notchPay;
        _userService = userService;
        _ntfy = ntfy;
        _email = email;
        _affiliate = affiliate;
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

        // Résoudre l'email et le nom : depuis le compte si connecté, depuis la requête sinon
        string email;
        string customerName;
        if (userId.HasValue)
        {
            var user = await _userService.GetUserByIdAsync(userId.Value);
            email = user?.Email ?? request.Email ?? $"user{userId}@winplus.cm";
            var fullName = $"{user?.FirstName} {user?.LastName}".Trim();
            customerName = !string.IsNullOrWhiteSpace(fullName) ? fullName : email.Split('@')[0];
        }
        else
        {
            email = request.Email ?? order.GuestEmail ?? "guest@winplus.cm";
            customerName = !string.IsNullOrWhiteSpace(order.GuestName) ? order.GuestName : email.Split('@')[0];
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
            Description   = request.Description ?? $"WinPlus  Épreuves scolaires",
            Status      = "pending",
            InitiatedAt = DateTime.UtcNow,
            ExpiresAt   = DateTime.UtcNow.AddHours(1)
        });

        try
        {
            var channel = MapToNotchPayChannel(order.PaymentMethod);
            var result = await _notchPay.InitiatePaymentAsync(
                request.Phone, request.Amount, orderId,
                payment.Description!, email, customerName, channel);

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

        // Module 17, défense en profondeur : le montant annoncé par le
        // provider n'était jamais comparé ni au montant du paiement initié,
        // ni au montant de la commande. Un écart signale soit une commande
        // dont le montant a été forgé côté client, soit une notification
        // falsifiée : dans les deux cas la commande ne doit pas être
        // confirmée, et un administrateur doit le savoir.
        var mismatch = await DetectAmountMismatchAsync(payment, transaction);
        if (mismatch != null)
        {
            _logger.LogError(
                "Webhook NotchPay {EventId} rejeté : {Reason} (paiement {PaymentId}, commande {OrderId})",
                eventId, mismatch, payment.Id, payment.OrderId);

            await _repository.MarkWebhookEventProcessedAsync(eventId, "notchpay", eventType);

            try
            {
                await _ntfy.PublishAdminAsync(
                    title: "Écart de montant sur un paiement",
                    message: $"Paiement #{payment.Id} (commande #{payment.OrderId}) : {mismatch}. " +
                             "La commande n'a pas été confirmée, vérification manuelle requise.",
                    priority: "urgent",
                    tags: new[] { "rotating_light", "moneybag" });
            }
            catch (Exception ex)
            {
                // L'alerte ne doit jamais masquer le rejet lui-même.
                _logger.LogError(ex, "Échec de l'alerte administrateur pour l'écart de montant du paiement {PaymentId}", payment.Id);
            }

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

        // Module 19 : seul le cas confirmé était propagé à la commande, si
        // bien qu'un paiement échoué, annulé ou expiré laissait sa commande
        // "pending" à vie. Tous les cas terminaux sont désormais propagés.
        await PropagatePaymentStatusToOrderAsync(payment);

        if (payment.Status == "completed")
        {

            // Programme d'affiliation : attribue une commission si la commande
            // porte un code de parrainage (voir Order.ReferralCode). Ne doit
            // jamais faire échouer la confirmation de paiement elle-même 
            // erreurs déjà avalées à l'intérieur de RecordCommissionForOrderAsync.
            await _affiliate.RecordCommissionForOrderAsync(payment.OrderId);

            // Notification push  awaité : en fire-and-forget, le DbContext (scope
            // requête) peut être détruit avant la fin de l'appel HTTP vers ntfy,
            // faisant échouer silencieusement PublishAsync (ObjectDisposedException
            // avalée par son propre try/catch) et perdant la notification la plus
            // critique commercialement, de façon intermittente et non reproductible.
            await _ntfy.PublishAsync(
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
            await _ntfy.PublishAsync(
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

    /// <summary>
    /// Statut de commande correspondant à un statut de paiement terminal
    /// (Module 19), ou null si le paiement n'est pas encore terminal.
    ///
    /// Un paiement expiré n'a pas de statut de commande dédié dans la liste
    /// blanche de OrderService : il partage l'état final « échoué », la
    /// distinction restant portée par le paiement lui-même. Une commande dans
    /// cet état n'est pas un cul-de-sac : la relance de paiement la ramène en
    /// attente (voir RetryPaymentAsync).
    /// </summary>
    private static string? MapPaymentStatusToOrderStatus(string paymentStatus) => paymentStatus switch
    {
        "completed" => "completed",
        "failed" => "failed",
        "expired" => "failed",
        "cancelled" => "cancelled",
        _ => null,
    };

    /// <summary>
    /// Propage l'état terminal d'un paiement à sa commande, par la voie
    /// contrôlée à liste blanche plutôt que par une écriture directe.
    /// N'échoue jamais l'opération appelante : la commande peut être
    /// rattrapée, perdre la confirmation du paiement non.
    /// </summary>
    private async Task PropagatePaymentStatusToOrderAsync(Payment payment)
    {
        var orderStatus = MapPaymentStatusToOrderStatus(payment.Status);
        if (orderStatus == null) return;

        try
        {
            // Cas de course signalé plutôt qu'écrasé silencieusement : un
            // paiement confirmé après que sa commande a déjà été marquée
            // échouée doit laisser une trace exploitable.
            var current = await _db.Orders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == payment.OrderId);

            if (current != null &&
                string.Equals(current.Status, "failed", StringComparison.OrdinalIgnoreCase) &&
                orderStatus == "completed")
            {
                _logger.LogWarning(
                    "Commande {OrderId} déjà marquée échouée alors que le paiement {PaymentId} est confirmé : " +
                    "cas de course, la commande est repassée à confirmée et doit être vérifiée.",
                    payment.OrderId, payment.Id);
            }

            await _orderService.UpdateOrderStatusAsync(payment.OrderId, orderStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossible de mettre à jour le statut de la commande {OrderId} vers {Status}",
                payment.OrderId, orderStatus);
            return;
        }

        if (orderStatus != "completed") return;

        // Module 18 (correction §7.3 du suivi) : une commande d'abonnement
        // confirmée doit produire une vraie ligne Subscriptions. C'était le
        // chaînon manquant du point 3.1.11 — le paiement aboutissait, et
        // l'abonnement n'existait nulle part, ce qui laissait le mur payant du
        // Module 17 bloquer un client qui venait de payer.
        //
        // Placé ici plutôt que dans la seule branche du webhook : les deux
        // chemins de confirmation (notification NotchPay et synchronisation
        // par consultation de statut) passent par cette méthode, et le second
        // est le filet de secours quand la notification ne parvient pas.
        // L'activation est idempotente, un rejeu n'empile pas les abonnements.
        try
        {
            await _subscriptionActivation.ActivateFromOrderAsync(payment.OrderId);
        }
        catch (Exception ex)
        {
            // Ne fait jamais échouer la confirmation du paiement : l'argent est
            // encaissé, et un abonnement manquant est rattrapable, alors que
            // rejeter la notification ferait reperdre la confirmation.
            _logger.LogError(ex,
                "Paiement {PaymentId} confirmé mais activation de l'abonnement de la commande {OrderId} en échec",
                payment.Id, payment.OrderId);
        }
    }

    /// <summary>
    /// Décrit l'écart de montant constaté sur une notification de paiement,
    /// ou null si tout concorde (Module 17, point 4).
    ///
    /// Deux comparaisons distinctes, car elles détectent deux fraudes
    /// différentes : le montant notifié face au montant réellement initié
    /// (notification falsifiée ou paiement partiel accepté par le provider),
    /// et le montant du paiement face au total de la commande (montant forgé
    /// côté client au moment de la commande).
    ///
    /// Le contrôle ne s'applique qu'aux notifications qui confirment un
    /// encaissement : un échec, une annulation ou une expiration doivent
    /// continuer d'être propagés même si le provider ne renvoie pas de
    /// montant, sans quoi la correction figerait à nouveau des commandes en
    /// attente. XAF étant sans sous-unité, la comparaison se fait à
    /// l'unité près.
    /// </summary>
    private async Task<string?> DetectAmountMismatchAsync(Payment payment, NotchPayWebhookTransaction transaction)
    {
        if (MapNotchPayStatus(transaction.Status) != "completed")
            return null;

        static decimal Xaf(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);

        if (transaction.Amount.HasValue && Xaf(transaction.Amount.Value) != Xaf(payment.Amount))
        {
            return $"montant notifié {Xaf(transaction.Amount.Value)} XAF ≠ montant du paiement {Xaf(payment.Amount)} XAF";
        }

        if (!string.IsNullOrWhiteSpace(transaction.Currency) &&
            !string.Equals(transaction.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return $"devise notifiée {transaction.Currency} ≠ devise du paiement {payment.Currency}";
        }

        var order = await _db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == payment.OrderId);

        if (order == null)
            return $"commande {payment.OrderId} introuvable";

        // Le total de la commande est le montant net attendu : la remise est
        // déjà déduite de TotalAmount au moment de la création (voir
        // OrderService). Aucune tolérance n'est acceptée, le XAF n'ayant pas
        // de sous-unité.
        if (Xaf(payment.Amount) != Xaf(order.TotalAmount))
        {
            return $"montant du paiement {Xaf(payment.Amount)} XAF ≠ total de la commande {Xaf(order.TotalAmount)} XAF";
        }

        return null;
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

    public async Task<PaymentStatusResponse> GetPaymentStatusAsync(int paymentId, int? requestingUserId, bool isAdmin)
    {
        var payment = await _repository.GetByIdAsync(paymentId)
            ?? throw new ArgumentException("Paiement introuvable");

        // Anonymous can poll guest payments (UserId == null); logged-in users can poll their own
        if (!isAdmin && payment.UserId != null && payment.UserId != requestingUserId)
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

                    // Module 19 : ce chemin de synchronisation (consultation
                    // du statut, utilisé quand le webhook ne parvient pas)
                    // ne touchait pas non plus la commande, laissant la même
                    // commande bloquée en attente.
                    await PropagatePaymentStatusToOrderAsync(payment);
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

        // Module 19 : la condition n'acceptait que "failed", ce qui faisait
        // d'"expired" un cul-de-sac : le service d'expiration passe un
        // paiement en attente à expiré au bout d'une heure, et plus rien ne
        // pouvait alors le relancer, laissant la commande bloquée. Les deux
        // états terminaux non confirmés sont désormais relançables ; un
        // paiement annulé reste volontairement exclu, c'est une décision
        // explicite de l'utilisateur.
        if (payment.Status != "failed" && payment.Status != "expired")
            throw new InvalidOperationException("Seuls les paiements échoués ou expirés peuvent être réessayés");

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
            var fullName = $"{user.FirstName} {user.LastName}".Trim();
            var customerName = !string.IsNullOrWhiteSpace(fullName) ? fullName : email.Split('@')[0];
            var channel = DetectChannelFromPhone(payment.PhoneNumber ?? "");
            var result = await _notchPay.InitiatePaymentAsync(
                payment.PhoneNumber!, payment.Amount, payment.OrderId,
                payment.Description ?? $"WinPlus  Épreuves scolaires", email, customerName, channel);

            payment.NotchpayReference = result.Transaction?.Reference;
            payment.Status = MapNotchPayStatus(result.Transaction?.Status) ?? "pending";
            await _repository.UpdateAsync(payment);

            // La commande avait été marquée échouée par la propagation de
            // l'échec ou de l'expiration : une relance en cours la ramène en
            // attente, sans quoi elle resterait échouée même après une
            // confirmation réussie (Module 19).
            if (payment.Status == "pending")
            {
                try { await _orderService.UpdateOrderStatusAsync(payment.OrderId, "pending"); }
                catch (Exception ex) { _logger.LogError(ex, "Impossible de rouvrir la commande {OrderId} lors de la relance", payment.OrderId); }
            }
            else
            {
                await PropagatePaymentStatusToOrderAsync(payment);
            }

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

    private static string MapToNotchPayChannel(string? paymentMethod) => paymentMethod switch
    {
        "orange" => "cm.orange",
        _        => "cm.mtn",
    };

    private static string DetectChannelFromPhone(string phone)
    {
        // Strip country code if present ("237XXXXXXXXX" or "+237XXXXXXXXX")
        var s = phone.TrimStart('+');
        if (s.StartsWith("237") && s.Length > 9) s = s[3..];
        if (s.Length < 3) return "cm.mtn";

        var pfx = s[..3];
        string[] orangePrefixes = ["655","656","657","658","659","690","691","692","693","694","695","696","697","698","699"];
        return Array.IndexOf(orangePrefixes, pfx) >= 0 ? "cm.orange" : "cm.mtn";
    }

    private static string? MapNotchPayStatus(string? notchpayStatus) => notchpayStatus?.ToLowerInvariant() switch
    {
        "complete" or "completed" or "success" or "successful" => "completed",
        "failed" or "failure" or "canceled" or "cancelled" or "rejected" => "failed",
        "pending" or "processing" or "initiated" => "pending",
        "expired" or "timeout" => "expired",
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
