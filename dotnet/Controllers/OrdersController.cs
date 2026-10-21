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
/// <summary>Paiement combiné solde + Mobile Money (TC-CAT-11, lot 2 Module 3).</summary>
public record PayWithBalanceComplementRequest(
    string Operator,
    string Phone,
    /// <summary>Code promo validé au panier ; recalculé côté serveur.</summary>
    string? PromoCode = null,
    /// <summary>Lot 10.3 : source d'acquisition captée par le web (facultative).</summary>
    Backend.Services.Marketing.AttributionDto? Attribution = null);

/// <summary>Corps facultatif du paiement par solde (un appel sans corps reste accepté).</summary>
public record PayWithBalanceRequest(
    string? PromoCode = null,
    Backend.Services.Marketing.AttributionDto? Attribution = null);

public record CreateOrderRequest(
    string PaymentMethod,
    string? ReferralCode = null,
    /// <summary>Code promo validé au panier (Module 34) ; revalidé et recalculé côté serveur, jamais pris tel quel.</summary>
    string? PromoCode = null,
    /// <summary>Lot 10.3 : source d'acquisition captée par le web (facultative, selon le consentement).</summary>
    Backend.Services.Marketing.AttributionDto? Attribution = null
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
    private readonly IWalletService _wallet;
    private readonly IPaymentService _payments;
    private readonly Backend.Services.Marketing.IAttributionService? _attribution;
    private readonly IPromoCodeService? _promoCodes;
    private readonly Backend.Services.Marketing.IConversionTracker? _conversions;

    public OrdersController(
        IOrderService orderService,
        ILogger<OrdersController> logger,
        ApplicationDbContext db,
        IPdfService pdfService,
        ITeacherService teacherService,
        IAffiliateService affiliate,
        IWalletService wallet,
        IPaymentService payments,
        Backend.Services.Marketing.IAttributionService? attribution = null,
        IPromoCodeService? promoCodes = null,
        Backend.Services.Marketing.IConversionTracker? conversions = null)
    {
        _attribution = attribution;
        _promoCodes = promoCodes;
        _conversions = conversions;
        _wallet = wallet;
        _payments = payments;
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

    /// <summary>
    /// Comptes qui paient leur propre panier avec leur solde : le professeur
    /// (revenus, recharges) et l'élève (recharges, lot 2 Module 3). Le parent
    /// paie pour un enfant (ParentCartCheckoutController) ; l'institution a son
    /// propre portefeuille partagé, hors de ce parcours.
    /// </summary>
    private static bool CanPayOwnCartWithBalance(string? role) =>
        string.Equals(role, "teacher", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "student", StringComparison.OrdinalIgnoreCase);

    private string? CurrentRole() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value;

    /// <summary>Lot 10.6 : message dans la langue du compte (User.Locale).</summary>
    private async Task<Func<string, string, string>> MessagesForAsync(int userId)
    {
        var en = NotificationLocales.IsEnglish(await NotificationLocales.ForUserAsync(_db, userId));
        return (fr, english) => en ? english : fr;
    }

    private IActionResult BalanceRoleRefused(Func<string, string, string> L, string? role) =>
        StatusCode(403, new
        {
            success = false,
            code = "balance_not_available",
            error = string.Equals(role, "parent", StringComparison.OrdinalIgnoreCase)
                ? L("Le parent paie avec son portefeuille pour un enfant : choisissez l'enfant dans la fenêtre d'achat.",
                    "Parents pay from their wallet for a child: choose the child in the checkout window.")
                : L("Le paiement par solde est réservé aux comptes élève, parent et professeur.",
                    "Paying with your balance is available to student, parent and teacher accounts."),
        });

    /// <summary>
    /// Transaction Serializable sur PostgreSQL ; aucune sur le fournisseur
    /// InMemory des tests (sans transaction), où le verrou du portefeuille est
    /// un verrou de processus (WalletService.RunLockedAsync).
    /// </summary>
    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginSerializableAsync() =>
        _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null;

    /// <summary>Hors transaction : code promo décompté, attribution figée, conversion (lot 10.3). Ne lève jamais.</summary>
    private async Task AfterBalancePaidAsync(int userId, Order order, Backend.Services.Marketing.AttributionDto? attribution)
    {
        if (!string.IsNullOrEmpty(order.PromoCode) && _promoCodes != null)
        {
            try { await _promoCodes.ApplyPromoCodeAsync(userId, order.Id, order.PromoCode); }
            catch (Exception ex) { _logger.LogError(ex, "Code promo de la commande {OrderId} non décompté", order.Id); }
        }
        if (_attribution != null)
            await _attribution.ApplyToOrderAsync(order.Id, userId, attribution, HttpContext?.RequestAborted ?? default);
        if (_conversions != null)
            await _conversions.RecordOrderPurchaseAsync(order.Id);
    }

    [HttpPost("pay-with-balance")]
    [Authorize]
    public async Task<IActionResult> PayWithBalance(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] PayWithBalanceRequest? request = null)
    {
        try
        {
            var userId = User.GetUserId();
            var L = await MessagesForAsync(userId);
            var role = CurrentRole();
            if (!CanPayOwnCartWithBalance(role))
                return BalanceRoleRefused(L, role);

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
            await using var tx = await BeginSerializableAsync();

            // Lot 2 : verrou du portefeuille pris dès l'ouverture de la
            // transaction (verrou consultatif PostgreSQL, gardé jusqu'au COMMIT),
            // pour que la détection de doublon ci-dessous et le débit voient
            // tous deux l'état laissé par une requête concurrente déjà validée.
            await _wallet.RunLockedAsync(userId, () => Task.FromResult(true));

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
                if (pricing.IsEmpty || pricing.Total <= 0)
                    return BadRequest(new { success = false, code = "cart_empty", error = L("Panier vide.", "Your cart is empty.") });

                // Code promo recalculé par la même règle que CreateOrderAsync,
                // AVANT le débit : le solde est comparé au total remisé, celui
                // que la commande portera. Un code devenu caduc est refusé
                // plutôt que débité au prix plein sans que l'utilisateur le voie.
                var promo = await CartPromo.ComputeAsync(_promoCodes, userId, pricing.Items, request?.PromoCode);
                if (promo.Invalid)
                    return BadRequest(new { success = false, code = "promo_invalid",
                        error = L("Le code promo n'est plus valable : retire-le du panier pour voir le nouveau total.",
                                  "The promo code is no longer valid: remove it from your cart to see the new total.") });
                var serverTotal = Math.Max(0m, pricing.Total - promo.Discount);

                // Idempotence : une commande identique tout juste payée est
                // renvoyée telle quelle, au lieu d'en créer une seconde. La
                // comparaison porte sur le montant serveur ET sur l'ensemble
                // des contenus, pour ne pas confondre deux achats distincts de
                // même prix (voir le commentaire de la fenêtre ci-dessus).
                // Seule une commande réellement payée compte : une commande
                // restée en attente n'a rien débité et ne doit pas masquer un
                // nouvel achat.
                var since = DateTime.UtcNow.Subtract(BalancePaymentIdempotencyWindow);
                // Lot 9.7b (F4) : la clé porte sur les couples (type,
                // identifiant) des lignes. Comparer les seuls SubjectId (nuls
                // pour une formation) confondait deux formations différentes de
                // même prix : la seconde n'était jamais achetée.
                var cartKey = CourseSales.LinesKey(pricing.Items.Select(i => (i.SubjectId, i.CourseId)));

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
                    CourseSales.LinesKey(o.Items.Select(i => (i.SubjectId, i.CourseId))) == cartKey);

                if (duplicate != null)
                {
                    // Comportement aligné sur le chemin normal, où
                    // CreateOrderAsync vide le panier : un rejeu laissait
                    // sinon le panier plein alors que la commande était déjà
                    // passée, et le client réessayait indéfiniment.
                    var cartItems = await _db.CartItems.Where(c => c.UserId == userId).ToListAsync();
                    _db.CartItems.RemoveRange(cartItems);
                    await _db.SaveChangesAsync();

                    if (tx != null) await tx.CommitAsync();
                    _logger.LogWarning(
                        "Double soumission détectée sur le paiement par solde du compte {UserId} : commande {OrderId} renvoyée",
                        userId, duplicate.Id);

                    return Ok(new
                    {
                        data = duplicate,
                        success = true,
                        duplicate = true,
                        // Permet au client de distinguer « déjà payé » d'un
                        // nouvel achat : sans ce message, les deux cas
                        // arrivaient sous une réponse identique.
                        message = L($"Cette commande a déjà été payée avec ton solde (commande {duplicate.OrderNumber}). Aucun second débit n'a été effectué.",
                                    $"This order has already been paid with your balance (order {duplicate.OrderNumber}). You were not charged twice."),
                    });
                }

                // Lot 2, Module 1 : le solde est lu dans le journal, sous le
                // verrou du portefeuille (pris dans la transaction ouverte
                // ci-dessus), et le débit y est écrit dans la même transaction.
                // Deux paiements concurrents du même professeur sont donc
                // sérialisés : le second voit le solde déjà débité.
                var paid = await _wallet.RunLockedAsync(userId, async () =>
                {
                    var available = await _wallet.GetAvailableAsync(userId);
                    if (available < serverTotal) return (Order?)null;

                    var created = await _orderService.CreateOrderAsync(userId, "balance", null, promo.Code);

                    // Le statut passe par la voie contrôlée à liste blanche plutôt
                    // que par une écriture directe sur l'entité (Module 19).
                    await _orderService.UpdateOrderStatusAsync(created.Id, "completed");

                    await _wallet.DebitAsync(new WalletEntry(userId, WalletEntryTypes.BalancePurchase, created.TotalAmount,
                        WalletService.BalancePurchaseKey(created.Id), $"Achat panier {created.OrderNumber}", "Order", created.Id));
                    return created;
                });

                if (paid == null)
                {
                    var balance = Math.Max(0, await _wallet.GetAvailableAsync(userId));
                    return StatusCode(402, new
                    {
                        success = false,
                        code = "insufficient_balance",
                        error = L($"Solde insuffisant : {balance:0} XAF disponibles, {serverTotal:0} XAF requis. Choisis le paiement combiné (solde + Mobile Money).",
                                  $"Not enough balance: {balance:0} XAF available, {serverTotal:0} XAF needed. Choose the combined payment (balance + Mobile Money)."),
                        balanceXaf = balance,
                        requiredXaf = serverTotal,
                        priceAdjustments = pricing.Adjustments,
                    });
                }

                var order = paid;
                if (tx != null) await tx.CommitAsync();

                // Hors transaction et idempotent : crédit des auteurs des contenus.
                try { await _wallet.SyncOrderAsync(order.Id); }
                catch (Exception ex) { _logger.LogError(ex, "Écritures de vente de la commande {OrderId} non posées (réconciliation à venir)", order.Id); }

                // Hors transaction : la commission d'affiliation ne doit pas
                // pouvoir faire échouer ni rejouer le débit déjà validé.
                await _affiliate.RecordCommissionForOrderAsync(order.Id);
                await AfterBalancePaidAsync(userId, order, request?.Attribution);

                _logger.LogInformation("Compte {UserId} a payé sa commande {OrderId} avec son solde WinPlus ({Amount} XAF)",
                    userId, order.Id, order.TotalAmount);

                return Ok(new { data = order, success = true });
            }
        }
        catch (ContentNotPurchasableException ex)
        {
            return BadRequest(new { success = false, error = ex.Message, subjectId = ex.SubjectId });
        }
        catch (CoursePurchaseRefusedException ex)
        {
            return Conflict(new { success = false, code = ex.Code, error = ex.Message, courseId = ex.CourseId });
        }
        catch (InsufficientWalletBalanceException ex)
        {
            return StatusCode(402, new { success = false, error = ex.Message, balanceXaf = ex.AvailableXaf, requiredXaf = ex.RequiredXaf });
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
    /// Montant minimal d'un paiement Mobile Money accepté par NotchPay en XAF
    /// (documentation NotchPay, « minimum_amount » de la devise XAF).
    /// </summary>
    public const decimal MinimumMobileMoneyXaf = 100m;

    /// <summary>
    /// Paiement combiné : solde WinPlus + complément Mobile Money (cas de test
    /// TC-CAT-11, lot 2 Module 3), quand le solde ne couvre pas tout le panier.
    ///
    /// Sous le verrou du portefeuille, la commande est créée en attente et la
    /// part solde est débitée dans le journal (réservation), puis le complément
    /// est demandé par le parcours d'encaissement existant. À la confirmation du
    /// paiement, la commande passe en <c>completed</c> (ventes créditées aux
    /// auteurs). Si le complément échoue, expire ou est annulé, la commande
    /// passe en échec et la part solde est restituée par contre-passation : la
    /// commande ne reste jamais dans un état intermédiaire.
    ///
    /// La part Mobile Money ne descend jamais sous le minimum NotchPay (100
    /// XAF) : la part solde est réduite d'autant si nécessaire.
    /// </summary>
    [HttpPost("pay-with-balance-complement")]
    [Authorize]
    public async Task<IActionResult> PayWithBalanceComplement([FromBody] PayWithBalanceComplementRequest request)
    {
        var userId = User.GetUserId();
        var L = await MessagesForAsync(userId);
        var role = CurrentRole();
        if (!CanPayOwnCartWithBalance(role))
            return BalanceRoleRefused(L, role);

        var op = (request.Operator ?? string.Empty).Trim().ToLowerInvariant();
        if (op is not ("mtn" or "orange"))
            return BadRequest(new { success = false, code = "invalid_operator", error = L("Opérateur invalide : MTN MoMo ou Orange Money.", "Invalid operator: MTN MoMo or Orange Money.") });
        var phone = WithdrawalService.NormalizePhone(request.Phone);
        if (phone == null)
            return BadRequest(new { success = false, code = "invalid_phone", error = L("Numéro Mobile Money invalide : 9 chiffres commençant par 6.", "Invalid Mobile Money number: 9 digits starting with 6.") });

        Order order;
        decimal balancePart, momoPart;
        try
        {
            await using var tx = await BeginSerializableAsync();
            await _wallet.RunLockedAsync(userId, () => Task.FromResult(true));

            var pricing = await _orderService.PriceUserCartAsync(userId);
            if (pricing.IsEmpty || pricing.Total <= 0)
                return BadRequest(new { success = false, code = "cart_empty", error = L("Panier vide.", "Your cart is empty.") });

            // Total remisé, identique à Order.TotalAmount (CreateOrderAsync) :
            // la part Mobile Money attendue par le contrôle du montant notifié
            // est total - part solde.
            var promo = await CartPromo.ComputeAsync(_promoCodes, userId, pricing.Items, request.PromoCode);
            if (promo.Invalid)
                return BadRequest(new { success = false, code = "promo_invalid",
                    error = L("Le code promo n'est plus valable : retire-le du panier pour voir le nouveau total.",
                              "The promo code is no longer valid: remove it from your cart to see the new total.") });
            var total = Math.Max(0m, pricing.Total - promo.Discount);

            var available = Math.Max(0, await _wallet.GetAvailableAsync(userId));
            if (available >= total)
                return Conflict(new { success = false, code = "balance_sufficient", error = L("Ton solde couvre tout le panier : utilise le paiement par solde.", "Your balance covers the whole cart: pay with your balance."), balanceXaf = available });
            if (available <= 0)
                return StatusCode(402, new { success = false, code = "no_balance", error = L("Aucun solde disponible à combiner : paie en Mobile Money.", "No balance available to combine: pay with Mobile Money."), balanceXaf = 0 });

            balancePart = Math.Min(available, total - MinimumMobileMoneyXaf);
            if (balancePart <= 0)
                return BadRequest(new { success = false, code = "complement_too_small", error = L($"Le complément Mobile Money doit être d'au moins {MinimumMobileMoneyXaf:0} FCFA : paie ce panier entièrement en Mobile Money.", $"The Mobile Money top-up must be at least {MinimumMobileMoneyXaf:0} FCFA: pay this cart fully with Mobile Money.") });
            balancePart = RevenueSplit.Xaf(Math.Floor(balancePart));
            momoPart = total - balancePart;

            order = await _orderService.CreateOrderAsync(userId, op, null, promo.Code);
            await _wallet.DebitAsync(new WalletEntry(userId, WalletEntryTypes.BalancePurchase, balancePart,
                WalletService.BalancePurchaseKey(order.Id), $"Achat panier {order.OrderNumber} (part solde)", "Order", order.Id));
            if (tx != null) await tx.CommitAsync();
        }
        catch (ContentNotPurchasableException ex)
        {
            return BadRequest(new { success = false, error = ex.Message, subjectId = ex.SubjectId });
        }
        catch (CoursePurchaseRefusedException ex)
        {
            return Conflict(new { success = false, code = ex.Code, error = ex.Message, courseId = ex.CourseId });
        }
        catch (InsufficientWalletBalanceException ex)
        {
            return StatusCode(402, new { success = false, error = ex.Message, balanceXaf = ex.AvailableXaf });
        }
        catch (Exception ex) when (IsSerializationFailure(ex))
        {
            return StatusCode(409, new { success = false, code = "busy", error = L("Un autre paiement sur ton solde est en cours. Réessaie dans un instant.", "Another payment from your balance is in progress. Please try again in a moment.") });
        }

        if (_attribution != null)
            await _attribution.ApplyToOrderAsync(order.Id, userId, request.Attribution, HttpContext?.RequestAborted ?? default);

        try
        {
            var payment = await _payments.InitiateNotchPayAsync(userId, new InitiatePaymentRequest
            {
                OrderId = order.Id,
                Phone = phone,
                Amount = momoPart,
                Description = $"WinPlus : complément de la commande {order.OrderNumber}",
            });

            return Ok(new
            {
                success = true,
                data = new
                {
                    orderId = order.Id,
                    orderNumber = order.OrderNumber,
                    paymentId = payment.PaymentId,
                    status = payment.Status,
                    totalXaf = order.TotalAmount,
                    balancePartXaf = balancePart,
                    momoPartXaf = momoPart,
                },
            });
        }
        catch (Exception ex)
        {
            // Complément impossible à lancer : la commande passe en échec et la
            // part solde est restituée immédiatement.
            _logger.LogWarning(ex, "Complément Mobile Money de la commande {OrderId} non lancé : part solde restituée", order.Id);
            try
            {
                await _orderService.UpdateOrderStatusAsync(order.Id, "failed");
                await _wallet.SyncOrderAsync(order.Id);
            }
            catch (Exception inner)
            {
                _logger.LogError(inner, "Restitution de la part solde de la commande {OrderId} à reprendre (réconciliation)", order.Id);
            }
            var message = ex is HttpRequestException
                ? L("Service de paiement temporairement indisponible. Ton solde n'a pas été débité.", "The payment service is temporarily unavailable. Your balance was not charged.")
                : L("Le paiement Mobile Money n'a pas pu être lancé. Ton solde n'a pas été débité.", "The Mobile Money payment could not be started. Your balance was not charged.");
            return StatusCode(ex is HttpRequestException ? 503 : 400, new { success = false, error = message });
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
            var order = await _orderService.CreateOrderAsync(userId, request.PaymentMethod, request.ReferralCode, request.PromoCode);
            // Lot 10.3 : attribution figée sur la commande (ne lève jamais).
            if (_attribution != null)
                await _attribution.ApplyToOrderAsync(order.Id, userId, request.Attribution, HttpContext.RequestAborted);
            return Ok(order);
        }
        // Contenu supprimé, retiré de la vente ou non publié : erreur de
        // l'utilisateur, pas une panne (auparavant un 500 générique).
        catch (ContentNotPurchasableException ex)
        {
            return BadRequest(new { error = ex.Message, subjectId = ex.SubjectId });
        }
        catch (CoursePurchaseRefusedException ex)
        {
            return Conflict(new { code = ex.Code, error = ex.Message, courseId = ex.CourseId });
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

            // Lot 2 : annulation d'un paiement combiné en attente (part solde
            // restituée) ou d'une recharge en attente (close). Idempotent.
            try { await _wallet.SyncOrderAsync(id); }
            catch (Exception ex) { _logger.LogError(ex, "Écritures de l'annulation de la commande {OrderId} non posées (réconciliation à venir)", id); }

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
                .Include(o => o.Items).ThenInclude(i => i.Course)
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

            // Lot 2 : une recharge de portefeuille n'est pas un achat de
            // contenu ; son montant est déjà sur le solde, retirable.
            if (await _db.WalletTopUps.AnyAsync(t => t.OrderId == id))
                return BadRequest(new { success = false, error = "Une recharge de portefeuille ne fait pas l'objet d'un remboursement : le montant est disponible sur ton solde et peut être retiré." });

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

            // Même règle que la création de commande : lot 9.7b (18.21), prix
            // TTC saisis, TVA calculée à l'intérieur de chaque prix.
            var prices = subjects.Select(s => decimal.Round(s.Price, 0, MidpointRounding.AwayFromZero)).ToList();
            var total = prices.Sum();
            var subtotal = prices.Sum(VatPolicy.ExclTaxFromInclTax);
            var tax   = total - subtotal;

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
