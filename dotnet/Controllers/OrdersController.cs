using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Services;
using Backend.Models.Entities;
using Backend.Extensions;
using Backend.Models.DTOs;

namespace Backend.Controllers;

/// <summary>
/// Corps de création de commande. Les anciens champs du parcours invité
/// (guestEmail, guestName, items) ne sont plus lus : décision 9.2 du suivi,
/// un compte est obligatoire pour commander. Un client qui les envoie encore
/// n'est pas rejeté, ils sont simplement ignorés à la désérialisation.
/// </summary>
public record CreateOrderRequest(
    string PaymentMethod,
    string? ReferralCode = null
);

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ILogger<OrdersController> _logger;
    private readonly ApplicationDbContext _db;
    private readonly IPdfService _pdfService;
    private readonly ITeacherService _teacherService;
    private readonly IAffiliateService _affiliate;

    public OrdersController(
        IOrderService orderService,
        ILogger<OrdersController> logger,
        ApplicationDbContext db,
        IPdfService pdfService,
        ITeacherService teacherService,
        IAffiliateService affiliate)
    {
        _orderService = orderService;
        _logger = logger;
        _db = db;
        _pdfService = pdfService;
        _teacherService = teacherService;
        _affiliate = affiliate;
    }

    /// <summary>
    /// "Payer avec mon solde WinPlus" (Module 2, US-CAT-06) : le professeur
    /// règle son panier avec ses revenus de vente catalogue au lieu d'un
    /// paiement Mobile Money. Complété immédiatement (débit interne, pas
    /// d'attente de webhook)  contrairement au flux Mobile Money classique
    /// qui crée la commande "pending" en attendant confirmation.
    ///
    /// ⚠ Pas de split "solde partiel + Mobile Money pour le différentiel" :
    /// si le solde ne couvre pas tout le panier, on refuse plutôt que de
    /// facturer partiellement sans intégration de paiement complémentaire.
    /// </summary>
    /// <summary>
    /// Fenêtre pendant laquelle deux appels identiques de paiement par solde
    /// sont considérés comme une double soumission réseau du même achat, et
    /// non comme deux achats distincts (Module 19).
    ///
    /// Le jeton d'idempotence est dérivé côté serveur et non demandé au
    /// client : ni le web ni le mobile n'envoient aujourd'hui d'en-tête
    /// d'idempotence, et exiger ce jeton casserait les deux clients déjà
    /// déployés.
    ///
    /// La clé a d'abord été (utilisateur, méthode « solde », montant total,
    /// fenêtre de temps), ce qui avalait un second achat légitime : deux
    /// contenus différents au même prix, à moins d'une minute d'intervalle,
    /// étaient pris pour le même achat, l'argent n'était pas débité, et le
    /// panier n'était même pas vidé l'utilisateur repartait sans rien, sans
    /// message. La clé compare désormais aussi le contenu du panier à celui
    /// de la commande candidate : un rejeu porte exactement les mêmes
    /// contenus, deux achats distincts non.
    /// </summary>
    private static readonly TimeSpan BalancePaymentIdempotencyWindow = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Voir <see cref="DbConcurrency.IsSerializationFailure"/> (aide partagée
    /// avec l'achat parent pour un enfant).
    /// </summary>
    private static bool IsSerializationFailure(Exception exception) =>
        DbConcurrency.IsSerializationFailure(exception);

    [HttpPost("pay-with-balance")]
    [Authorize]
    public async Task<IActionResult> PayWithBalance()
    {
        try
        {
            var userId = User.GetUserId();
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value;
            if (!string.Equals(role, "teacher", StringComparison.OrdinalIgnoreCase))
                return StatusCode(403, new { success = false, error = "Seul un compte professeur dispose d'un solde WinPlus." });

            // Module 19, double dépense : le solde était lu puis la commande
            // créée sans transaction, sans verrou et sans idempotence. Le solde
            // étant recalculé par sommation à chaque appel, deux requêtes
            // concurrentes passaient toutes deux le test et créaient deux
            // commandes confirmées, dépensant deux fois le même argent.
            //
            // Le modèle reproduit ici est celui de la réservation de tutorat
            // (TutorBookingService) : une transaction Serializable, dont
            // Postgres détecte la collision au COMMIT (erreur 40001) plutôt
            // que d'écrire silencieusement les deux débits. L'isolation est
            // volontairement limitée à ce chemin, elle n'est pas globale.
            // La transaction est annulée automatiquement à la sortie du bloc
            // si elle n'a pas été validée (`await using`) : aucun chemin de
            // retour anticipé ne peut laisser un débit à moitié appliqué.
            await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            {
                // Passe de clôture du lot 0 : le contrôle de solde et la clé de
                // doublon portaient sur le prix stocké au panier, alors que la
                // commande est créée au prix relu en base. Si le prix avait
                // monté, la commande dépassait le solde. Le total est désormais
                // calculé par la même règle que la création de commande
                // (OrderService.PriceUserCartAsync), sans la dupliquer.
                var pricing = await _orderService.PriceUserCartAsync(userId);
                // Décision 10.1 : total TTC, identique à Order.TotalAmount posé
                // par CreateOrderAsync. Le contrôle de solde, la clé de doublon
                // et le débit portent donc tous sur le même montant.
                var serverTotal = pricing.Total;
                if (pricing.IsEmpty || serverTotal <= 0)
                    return BadRequest(new { success = false, error = "Panier vide." });

                // Idempotence : une commande identique tout juste payée est
                // renvoyée telle quelle, au lieu d'en créer une seconde. La
                // comparaison porte sur le montant serveur ET sur l'ensemble
                // des contenus, pour ne pas confondre deux achats distincts de
                // même prix (voir le commentaire de la fenêtre ci-dessus).
                // Seule une commande réellement payée compte : une commande
                // restée en attente n'a rien débité et ne doit pas masquer un
                // nouvel achat.
                var since = DateTime.UtcNow.Subtract(BalancePaymentIdempotencyWindow);
                var cartSubjectIds = pricing.Items.Select(i => i.SubjectId).OrderBy(id => id).ToList();

                var candidates = await _db.Orders.AsNoTracking()
                    .Include(o => o.Items)
                    .Where(o => o.UserId == userId
                             && o.PaymentMethod == "balance"
                             && PaidOrderStatus.All.Contains(o.Status.ToLower())
                             && o.TotalAmount == serverTotal
                             && o.CreatedAt >= since)
                    .OrderByDescending(o => o.CreatedAt)
                    .ToListAsync();

                var duplicate = candidates.FirstOrDefault(o =>
                    o.Items.Select(i => i.SubjectId).OrderBy(id => id).SequenceEqual(cartSubjectIds));

                if (duplicate != null)
                {
                    // Comportement aligné sur le chemin normal, où
                    // CreateOrderAsync vide le panier : un rejeu laissait
                    // sinon le panier plein alors que la commande était déjà
                    // passée, et le client réessayait indéfiniment.
                    var cartItems = await _db.CartItems.Where(c => c.UserId == userId).ToListAsync();
                    _db.CartItems.RemoveRange(cartItems);
                    await _db.SaveChangesAsync();

                    await tx.CommitAsync();
                    _logger.LogWarning(
                        "Double soumission détectée sur le paiement par solde du professeur {UserId} : commande {OrderId} renvoyée",
                        userId, duplicate.Id);

                    return Ok(new
                    {
                        data = duplicate,
                        success = true,
                        duplicate = true,
                        // Permet au client de distinguer « déjà payé » d'un
                        // nouvel achat : sans ce message, les deux cas
                        // arrivaient sous une réponse identique.
                        message = $"Cette commande a déjà été payée avec votre solde "
                                + $"(commande {duplicate.OrderNumber}). Aucun second débit n'a été effectué.",
                    });
                }

                var balance = await _teacherService.GetSpendableBalanceAsync(userId);
                if (balance < serverTotal)
                    return StatusCode(402, new
                    {
                        success = false,
                        error = $"Solde insuffisant : {balance:0} XAF disponibles, {serverTotal:0} XAF requis.",
                        balanceXaf = balance,
                        requiredXaf = serverTotal,
                        priceAdjustments = pricing.Adjustments,
                    });

                var order = await _orderService.CreateOrderAsync(userId, "balance");

                // Le statut passe par la voie contrôlée à liste blanche plutôt
                // que par une écriture directe sur l'entité (Module 19) : elle
                // valide la valeur, la normalise en minuscules et pose la date
                // de complétion.
                await _orderService.UpdateOrderStatusAsync(order.Id, "completed");

                await tx.CommitAsync();

                // Hors transaction : la commission d'affiliation ne doit pas
                // pouvoir faire échouer ni rejouer le débit déjà validé.
                await _affiliate.RecordCommissionForOrderAsync(order.Id);

                _logger.LogInformation("Professeur {UserId} a payé sa commande {OrderId} avec son solde WinPlus ({Amount} XAF)",
                    userId, order.Id, order.TotalAmount);

                return Ok(new { data = order, success = true });
            }
        }
        catch (ContentNotPurchasableException ex)
        {
            return BadRequest(new { success = false, error = ex.Message, subjectId = ex.SubjectId });
        }
        catch (Exception ex) when (IsSerializationFailure(ex))
        {
            // Collision de sérialisation Postgres (40001) ou interblocage
            // (40P01) : une autre requête du même professeur a dépensé le
            // même solde en parallèle.
            //
            // La capture ne portait que sur DbUpdateException, alors que le
            // conflit d'une transaction Serializable est levé par Postgres au
            // moment du COMMIT : ce n'est pas une écriture EF, donc
            // `tx.CommitAsync()` remonte une Npgsql.PostgresException nue, qui
            // passait à travers et retombait en 500 générique. La détection
            // porte donc sur le code SQL, où qu'il se trouve dans la chaîne
            // d'exceptions internes.
            _logger.LogWarning(ex, "Collision de sérialisation sur le paiement par solde");
            return StatusCode(409, new
            {
                success = false,
                error = "Un autre paiement sur votre solde est en cours de traitement. Réessayez dans un instant."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du paiement par solde");
            return StatusCode(500, new { success = false, error = "Erreur serveur" });
        }
    }

    /// <summary>
    /// Création de commande depuis le panier serveur de l'utilisateur.
    ///
    /// Décision 9.2 du suivi : le parcours de commande invité est supprimé, un
    /// compte est obligatoire. Il produisait une commande sans utilisateur
    /// (UserId nul), donc un achat payé qui n'ouvrait aucun accès. Le panier
    /// anonyme reste (il ne crée ni commande ni paiement) et est fusionné à la
    /// connexion. Les commandes invité historiques ne sont pas migrées.
    ///
    /// La réponse porte <c>priceAdjustments</c> : la liste des contenus dont
    /// le prix serveur diffère du prix vu au panier (vide sinon).
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            var order = await _orderService.CreateOrderAsync(userId, request.PaymentMethod, request.ReferralCode);
            return Ok(order);
        }
        // Contenu supprimé, retiré de la vente ou non publié : erreur de
        // l'utilisateur, pas une panne (auparavant un 500 générique).
        catch (ContentNotPurchasableException ex)
        {
            return BadRequest(new { error = ex.Message, subjectId = ex.SubjectId });
        }
        // Panier vide côté serveur au moment de payer : arrive typiquement quand
        // le panier n'a été rempli qu'en local (deviceId) avant une connexion
        // dont la fusion a échoué ou n'a pas encore eu lieu. C'est un état
        // utilisateur normal, pas une panne  un 500 générique masquait la vraie
        // cause et empêchait le frontend d'afficher un message actionnable.
        catch (InvalidOperationException ex) when (ex.Message == "Cart is empty")
        {
            _logger.LogWarning("Tentative de création de commande avec un panier vide pour l'utilisateur {UserId}", User.GetUserId());
            return BadRequest(new { error = "Votre panier est vide. Ajoutez des articles avant de commander." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la création de la commande");
            return StatusCode(500, "Erreur serveur");
        }
    }

    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(PaginationResponse<Order>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrders([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var userId = User.GetUserId();
            var orders = await _orderService.GetUserOrdersAsync(userId, page, pageSize);
            var allOrders = await _orderService.GetUserOrdersAsync(userId);
            var totalCount = allOrders.Count();

            var response = new PaginationResponse<Order>(orders, totalCount, page, pageSize);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des commandes");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Vrai si l'appelant a le droit d'agir sur cette commande (Module 20).
    ///
    /// Ces endpoints exigeaient une authentification mais ne filtraient
    /// jamais sur le propriétaire : n'importe quel compte authentifié pouvait
    /// lire, annuler et facturer la commande d'un autre en devinant son
    /// identifiant. L'administrateur conserve l'accès complet, explicitement.
    ///
    /// Une commande invité (UserId nul) n'appartient à aucun compte : elle
    /// n'est accessible qu'à l'administrateur par ces routes, le parcours
    /// invité passant par la consultation de statut de paiement.
    /// </summary>
    private bool CanAccessOrder(int? orderUserId) =>
        User.IsAdmin() || (orderUserId.HasValue && orderUserId.Value == User.GetUserId());

    [HttpGet("{id}")]
    [Authorize]
    public async Task<IActionResult> GetOrderById(int id)
    {
        try
        {
            var order = await _orderService.GetOrderByIdAsync(id);
            if (order == null)
                return NotFound();
            if (!CanAccessOrder(order.UserId))
                return StatusCode(403, new { success = false, error = "Accès refusé." });
            return Ok(order);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération de la commande {OrderId}", id);
            return StatusCode(500, "Erreur serveur");
        }
    }

    [HttpPost("{id}/cancel")]
    [Authorize]
    public async Task<IActionResult> CancelOrder(int id)
    {
        try
        {
            var order = await _orderService.GetOrderByIdAsync(id);
            if (order == null)
                return NotFound(new { success = false, error = "Commande introuvable" });

            if (!CanAccessOrder(order.UserId))
                return StatusCode(403, new { success = false, error = "Accès refusé." });

            // Décisions 10.3 et 10.6 : une commande complétée, réglée en crédits
            // (paid) ou en demande de remboursement n'est pas annulable par le
            // client. POST /refund puis POST /cancel permettait d'obtenir un
            // remboursement sans décision administrateur (décision 4.D), et
            // annuler une commande paid retirait l'accès de l'enfant et le
            // revenu du prof sans restituer les crédits. Règle partagée avec
            // OrderService.CancelOrderAsync et les chemins de paiement.
            var blockReason = OrderStatusRules.CancellationBlockReason(order.Status);
            if (blockReason != null)
                return BadRequest(new { success = false, error = blockReason });

            await _orderService.CancelOrderAsync(id);
            return Ok(new { data = new { id, status = "cancelled" }, success = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling order {OrderId}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("statistics")]
    [Authorize]
    public async Task<IActionResult> GetOrderStatistics()
    {
        try
        {
            var userId = User.GetUserId();
            var orders = (await _orderService.GetUserOrdersAsync(userId)).ToList();

            var data = new
            {
                total      = orders.Count,
                completed  = orders.Count(o => o.Status.Equals("completed",  StringComparison.OrdinalIgnoreCase)),
                pending    = orders.Count(o => o.Status.Equals("pending",    StringComparison.OrdinalIgnoreCase)),
                cancelled  = orders.Count(o => o.Status.Equals("cancelled",  StringComparison.OrdinalIgnoreCase)),
                totalSpent = orders
                    .Where(o => o.Status.Equals("completed", StringComparison.OrdinalIgnoreCase))
                    .Sum(o => o.TotalAmount),
            };

            return Ok(new { data, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order statistics");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("{id}/invoice")]
    [Authorize]
    public async Task<IActionResult> GetOrderInvoice(int id)
    {
        try
        {
            var order = await _db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Subject)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
                return NotFound(new { success = false, error = "Commande introuvable" });

            if (!CanAccessOrder(order.UserId))
                return StatusCode(403, new { success = false, error = "Accès refusé." });

            User? user = null;
            if (order.UserId.HasValue)
                user = await _db.Users.FindAsync(order.UserId.Value);

            var pdfBytes = _pdfService.GenerateInvoice(order, order.Items, user);
            var fileName = $"facture-{order.OrderNumber}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating invoice for order {OrderId}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("{id}/status")]
    [Authorize]
    public async Task<IActionResult> GetOrderStatus(int id)
    {
        try
        {
            var order = await _orderService.GetOrderByIdAsync(id);
            if (order == null)
                return NotFound(new { success = false, error = "Commande introuvable" });

            if (!CanAccessOrder(order.UserId))
                return StatusCode(403, new { success = false, error = "Accès refusé." });

            return Ok(new { data = new { order.Id, order.Status, order.CreatedAt }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order status {OrderId}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("search")]
    [Authorize]
    public async Task<IActionResult> SearchOrders(
        [FromQuery] string? q = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var userId = User.GetUserId();
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var query = _db.Orders.Where(o => o.UserId == userId);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(o => o.Status == status);

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(o =>
                    (o.OrderNumber != null && o.OrderNumber.Contains(q)) ||
                    (o.GuestEmail  != null && o.GuestEmail.Contains(q)));

            var total  = await query.CountAsync();
            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(o => new { o.Id, o.OrderNumber, o.Status, o.TotalAmount, o.CreatedAt })
                .ToListAsync();

            return Ok(new { data = orders, total, page, pageSize, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching orders");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPost("{id}/refund")]
    [Authorize]
    public async Task<IActionResult> RequestRefund(int id)
    {
        try
        {
            var userId = User.GetUserId();
            var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);
            if (order == null)
                return NotFound(new { success = false, error = "Commande introuvable" });

            if (!order.Status.Equals("completed", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, error = "Seules les commandes complétées peuvent faire l'objet d'un remboursement" });

            order.Status = "refund_requested";
            order.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new { data = new { id, status = "refund_requested" }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting refund for order {OrderId}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPost("summary")]
    public async Task<IActionResult> GetOrderSummary([FromBody] List<int> subjectIds)
    {
        try
        {
            if (subjectIds == null || subjectIds.Count == 0)
                return BadRequest(new { success = false, error = "Aucun sujet fourni" });

            var subjects = await _db.Subjects
                .Where(s => subjectIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Title, s.Price })
                .ToListAsync();

            // Même règle de TVA que la création de commande (décision 10.1) :
            // ce résumé annonçait 20 % alors que la commande est facturée à
            // 19,25 %, arrondie à l'unité (XAF).
            var subtotal = subjects.Sum(s => decimal.Round(s.Price, 0, MidpointRounding.AwayFromZero));
            var tax   = VatPolicy.TaxOn(subtotal);
            var total = subtotal + tax;

            return Ok(new
            {
                data = new
                {
                    items    = subjects,
                    subtotal = Math.Round(subtotal, 2),
                    tax,
                    total,
                    currency = "XAF"
                },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order summary");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
