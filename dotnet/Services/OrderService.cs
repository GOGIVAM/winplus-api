using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Repositories;

namespace Backend.Services;

/// <summary>
/// Contenu qui ne peut pas être commandé : supprimé, retiré de la vente ou
/// non publié (passe de clôture du lot 0). Levée typée pour que les
/// contrôleurs la traduisent en 400 avec un message clair, au lieu d'un 500.
/// </summary>
public class ContentNotPurchasableException : InvalidOperationException
{
    public int SubjectId { get; }

    public ContentNotPurchasableException(int subjectId, string message) : base(message)
    {
        SubjectId = subjectId;
    }
}

/// <summary>Ligne de panier valorisée au prix serveur.</summary>
public record PricedCartItem(int SubjectId, string Title, decimal CartPrice, decimal ServerPrice);

/// <summary>
/// Valorisation serveur d'un panier : lignes, sous-total hors taxe au prix
/// serveur, TVA, total TTC et liste des écarts constatés avec le prix vu au
/// panier.
///
/// Décision 10.1 du suivi : <see cref="Total"/> est le montant TTC, celui qui
/// est stocké dans <c>Order.TotalAmount</c>, payé via NotchPay et débité du
/// solde professeur. <see cref="Subtotal"/> reste la somme hors taxe des
/// prix de vente (base des revenus vendeur, via OrderItem.PriceAtPurchase).
/// </summary>
public record CartPricing(IReadOnlyList<PricedCartItem> Items, decimal Subtotal, IReadOnlyList<OrderPriceAdjustment> Adjustments)
{
    public bool IsEmpty => Items.Count == 0;

    /// <summary>TVA sur le sous-total (voir <see cref="VatPolicy"/>).</summary>
    public decimal Tax => VatPolicy.TaxOn(Subtotal);

    /// <summary>Total TTC : sous-total hors taxe + TVA.</summary>
    public decimal Total => Subtotal + Tax;
}

public interface IOrderService
{
    /// <summary>
    /// Valorise le panier de l'utilisateur au prix serveur, avec exactement la
    /// règle appliquée à la création de commande (seul endroit qui la porte).
    /// Lève <see cref="ContentNotPurchasableException"/> pour un contenu qui
    /// ne peut pas être commandé.
    /// </summary>
    Task<CartPricing> PriceUserCartAsync(int userId);
    Task<Order> CreateOrderAsync(int userId, string paymentMethod, string? referralCode = null, string? promoCode = null);
    Task<IEnumerable<Order>> GetUserOrdersAsync(int userId);
    Task<IEnumerable<Order>> GetUserOrdersAsync(int userId, int page, int limit);
    Task<Order?> GetOrderByIdAsync(int orderId);
    Task<Order?> GetOrderByNumberAsync(string orderNumber);
    Task<Order> UpdateOrderStatusAsync(int orderId, string status);
    Task<bool> CancelOrderAsync(int orderId);
    Task<decimal> GetTotalRevenueAsync();
    Task<int> GetOrderCountAsync();
    Task<IEnumerable<Order>> GetOrdersByStatusAsync(string status);
}

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICartRepository _cartRepository;
    private readonly ISubjectRepository _subjectRepository;
    private readonly IPromoCodeService _promoCodeService;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository orderRepository,
        ICartRepository cartRepository,
        ISubjectRepository subjectRepository,
        IPromoCodeService promoCodeService,
        ILogger<OrderService> logger)
    {
        _orderRepository = orderRepository;
        _cartRepository = cartRepository;
        _subjectRepository = subjectRepository;
        _promoCodeService = promoCodeService;
        _logger = logger;
    }

    /// <summary>
    /// Contenu et prix de vente réel, relus en base au moment de la commande
    /// (Module 17). Ni le panier stocké ni le corps de la requête ne font foi :
    /// le prix du panier peut être périmé, celui du client peut être falsifié.
    /// FCFA, devise sans sous-unité : arrondi à zéro décimale.
    ///
    /// Passe de clôture du lot 0 : un contenu supprimé ou non publié
    /// (brouillon, retiré) est refusé par une exception typée, traduite en 400
    /// par les contrôleurs, au lieu d'une InvalidOperationException nue qui
    /// retombait en 500.
    /// </summary>
    private async Task<(Subject Subject, decimal Price)> ResolveServerPriceAsync(int subjectId)
    {
        var subject = await _subjectRepository.GetByIdAsync(subjectId);
        if (subject == null || subject.IsDeleted)
            throw new ContentNotPurchasableException(subjectId,
                $"Le contenu {subjectId} n'est plus disponible à la vente. Retirez-le de votre panier pour continuer.");

        if (!subject.IsPublished)
            throw new ContentNotPurchasableException(subjectId,
                $"Le contenu « {subject.Title} » n'est pas publié et ne peut pas être commandé. Retirez-le de votre panier pour continuer.");

        return (subject, decimal.Round(subject.Price, 0, MidpointRounding.AwayFromZero));
    }

    public async Task<CartPricing> PriceUserCartAsync(int userId)
    {
        var cartItems = await _cartRepository.GetByUserIdAsync(userId);

        var items = new List<PricedCartItem>();
        var adjustments = new List<OrderPriceAdjustment>();
        foreach (var cartItem in cartItems)
        {
            var (subject, serverPrice) = await ResolveServerPriceAsync(cartItem.SubjectId);
            items.Add(new PricedCartItem(cartItem.SubjectId, subject.Title, cartItem.Price, serverPrice));

            if (serverPrice != cartItem.Price)
            {
                _logger.LogWarning(
                    "Prix du contenu {SubjectId} différent du panier de l'utilisateur {UserId} : {CartPrice} -> {ServerPrice} XAF appliqué",
                    cartItem.SubjectId, userId, cartItem.Price, serverPrice);
                adjustments.Add(new OrderPriceAdjustment
                {
                    SubjectId = cartItem.SubjectId,
                    Title = subject.Title,
                    OldPrice = cartItem.Price,
                    NewPrice = serverPrice,
                });
            }
        }

        // Sous-total hors taxe ; CartPricing.Total (TTC) en est dérivé.
        return new CartPricing(items, items.Sum(i => i.ServerPrice), adjustments);
    }

    public async Task<Order> CreateOrderAsync(int userId, string paymentMethod, string? referralCode = null, string? promoCode = null)
    {
        try
        {
            if (string.IsNullOrEmpty(paymentMethod))
                throw new ArgumentException("Payment method is required");

            // Module 17 : chaque prix d'achat est recalculé depuis le prix en
            // base au moment de la commande, jamais lu depuis le panier
            // stocké (dont le prix peut être périmé) ni depuis le client.
            var pricing = await PriceUserCartAsync(userId);
            if (pricing.IsEmpty)
                throw new InvalidOperationException("Cart is empty");

            // Décision 10.1 : le total stocké est TTC (TVA calculée ici, côté
            // serveur). Le web l'affichait et le payait déjà TTC alors que la
            // commande restait hors taxe : le contrôle de montant du webhook
            // rejetait tout paiement du catalogue.
            var totalAmount = pricing.Total;

            // Module 34 : la remise est recalculée ici, côté serveur, à partir
            // du code et du prix en base — jamais reprise d'un montant transmis
            // par le client (même cause racine que le Module 17). Le code n'est
            // rattaché à la commande (usage décompté, quota consommé) qu'à la
            // confirmation du paiement (PaymentService), pas ici : une commande
            // jamais payée ne doit pas consommer un quota limité (décision §5.5.O).
            var normalizedPromoCode = string.IsNullOrWhiteSpace(promoCode) ? null : promoCode.Trim().ToUpperInvariant();
            decimal discount = 0;
            if (normalizedPromoCode != null)
            {
                var validation = await _promoCodeService.ValidatePromoCodeAsync(userId, new ValidatePromoCodeRequest
                {
                    Code = normalizedPromoCode,
                    CartTotal = totalAmount,
                    SubjectIds = pricing.Items.Select(i => i.SubjectId).ToList(),
                });
                if (validation.IsValid)
                {
                    discount = decimal.Round(validation.DiscountAmount, 0, MidpointRounding.AwayFromZero);
                    totalAmount = Math.Max(0, totalAmount - discount);
                }
                else
                {
                    // Code devenu invalide entre la validation au panier et la
                    // commande (expiré, quota épuisé...) : la commande se crée
                    // quand même, mais au prix plein et sans code rattaché —
                    // jamais d'erreur bloquante pour un simple code caduc.
                    _logger.LogInformation(
                        "Code promo {Code} invalide à la création de la commande pour l'utilisateur {UserId} : {Reason}",
                        normalizedPromoCode, userId, validation.ErrorMessage);
                    normalizedPromoCode = null;
                }
            }

            // Create order
            var order = new Order
            {
                UserId = userId,
                OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}",
                TotalAmount = totalAmount,
                DiscountAmount = discount,
                PromoCode = normalizedPromoCode,
                Status = "pending",
                PaymentMethod = paymentMethod,
                OrderDate = DateTime.UtcNow,
                ReferralCode = string.IsNullOrWhiteSpace(referralCode) ? null : referralCode.Trim().ToUpperInvariant(),
                Items = new List<OrderItem>()
            };

            // Add items to order
            foreach (var item in pricing.Items)
            {
                order.Items.Add(new OrderItem
                {
                    OrderId = order.Id,
                    SubjectId = item.SubjectId,
                    PriceAtPurchase = item.ServerPrice,
                    Order = order
                });
            }

            var createdOrder = await _orderRepository.CreateAsync(order);

            // Clear cart
            await _cartRepository.ClearUserCartAsync(userId);

            _logger.LogInformation("Order created {OrderNumber} for user {UserId}", createdOrder.OrderNumber, userId);

            // Passe de clôture du lot 0 : un écart entre le prix vu au panier
            // et le prix facturé est renvoyé au client (champ non persisté),
            // et plus seulement journalisé.
            createdOrder.PriceAdjustments = pricing.Adjustments.ToList();

            return createdOrder;
        }
        // Panier vide est un état utilisateur normal (voir OrdersController.CreateOrder),
        // pas une panne : le logger ici sans distinction doublait un WARN déjà loggé
        // par le contrôleur avec une ERREUR pour rien.
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating order for user {UserId}", userId);
            throw;
        }
    }

    public async Task<IEnumerable<Order>> GetUserOrdersAsync(int userId)
    {
        try
        {
            return await _orderRepository.GetByUserIdAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting orders for user {UserId}", userId);
            return Enumerable.Empty<Order>();
        }
    }

    public async Task<IEnumerable<Order>> GetUserOrdersAsync(int userId, int page, int limit)
    {
        try
        {
            if (page < 1) page = 1;
            if (limit < 1 || limit > 100) limit = 20;

            var skip = (page - 1) * limit;
            var orders = await _orderRepository.GetByUserIdAsync(userId);
            return orders.Skip(skip).Take(limit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paginated orders for user {UserId} (page {Page}, limit {Limit})", userId, page, limit);
            return Enumerable.Empty<Order>();
        }
    }

    public async Task<Order?> GetOrderByIdAsync(int orderId)
    {
        try
        {
            return await _orderRepository.GetByIdAsync(orderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order {OrderId}", orderId);
            return null;
        }
    }

    public async Task<Order?> GetOrderByNumberAsync(string orderNumber)
    {
        try
        {
            if (string.IsNullOrEmpty(orderNumber))
                throw new ArgumentException("Order number is required");

            return await _orderRepository.GetByOrderNumberAsync(orderNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order {OrderNumber}", orderNumber);
            return null;
        }
    }

    public async Task<Order> UpdateOrderStatusAsync(int orderId, string status)
    {
        try
        {
            if (string.IsNullOrEmpty(status))
                throw new ArgumentException("Status is required");

            var validStatuses = new[] { "pending", "processing", "completed", "cancelled", "failed" };
            if (!validStatuses.Contains(status.ToLower()))
                throw new ArgumentException($"Invalid status. Must be one of: {string.Join(", ", validStatuses)}");

            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
                throw new InvalidOperationException($"Order {orderId} not found");

            // Module 19 : le statut est normalisé en minuscules, seule
            // convention réellement présente en base, pour ne pas réintroduire
            // la casse mixte que les comparaisons viennent d'absorber.
            order.Status = status.ToLower();

            // Portée par la voie contrôlée plutôt que laissée à chaque
            // appelant : le paiement par solde écrivait le statut ET la date
            // de complétion directement sur l'entité.
            if (order.Status == "completed" && order.CompletedDate == null)
                order.CompletedDate = DateTime.UtcNow;

            return await _orderRepository.UpdateAsync(order);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating order status {OrderId}", orderId);
            throw;
        }
    }

    public async Task<bool> CancelOrderAsync(int orderId)
    {
        try
        {
            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
                throw new InvalidOperationException($"Order {orderId} not found");

            // Décisions 10.3 et 10.6 : ni une commande complétée, ni une
            // commande réglée en crédits (paid), ni une commande en demande de
            // remboursement ne sont annulables par le client l'accès et le
            // revenu du vendeur ne changent qu'après décision administrateur
            // explicite (décision 4.D). Règle commune : OrderStatusRules.
            var blockReason = OrderStatusRules.CancellationBlockReason(order.Status);
            if (blockReason != null)
                throw new InvalidOperationException(blockReason);

            order.Status = "cancelled";

            await _orderRepository.UpdateAsync(order);
            _logger.LogInformation("Order {OrderId} cancelled", orderId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling order {OrderId}", orderId);
            throw;
        }
    }

    public async Task<decimal> GetTotalRevenueAsync()
    {
        try
        {
            return await _orderRepository.GetTotalRevenueAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating total revenue");
            return 0;
        }
    }

    public async Task<int> GetOrderCountAsync()
    {
        try
        {
            // Get all orders and count them
            var orders = await _orderRepository.GetAllAsync();
            return orders.Count();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order count");
            return 0;
        }
    }

    public async Task<IEnumerable<Order>> GetOrdersByStatusAsync(string status)
    {
        try
        {
            if (string.IsNullOrEmpty(status))
                throw new ArgumentException("Status is required");

            return await _orderRepository.GetByStatusAsync(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting orders by status");
            return Enumerable.Empty<Order>();
        }
    }
}
