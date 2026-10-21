using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Utils;

namespace Backend.Controllers;

/// <summary>Achat du panier par un parent pour un enfant lié (portefeuille, combiné ou Mobile Money).</summary>
public class ParentCartCheckoutRequest
{
    public int ChildId { get; set; }
    /// <summary>Code promo validé au panier ; recalculé et revérifié ici.</summary>
    public string? PromoCode { get; set; }
    /// <summary>Lot 10.3 : source d'acquisition captée par le web (facultative).</summary>
    public Backend.Services.Marketing.AttributionDto? Attribution { get; set; }
    /// <summary>Paiement combiné ou Mobile Money seulement : « mtn » ou « orange ».</summary>
    public string? Operator { get; set; }
    /// <summary>Paiement combiné ou Mobile Money seulement : numéro Mobile Money.</summary>
    public string? Phone { get; set; }
}

/// <summary>
/// « Payer avec mon portefeuille » dans la fenêtre d'achat, pour un parent :
/// tout le panier du parent est acheté pour UN enfant lié et accepté, en une
/// seule commande (marqueur bénéficiaire dans Order.Notes, comme
/// <see cref="ParentCreditsController.PurchaseForChild"/>).
///
///  * POST /api/parent/cart/pay-with-wallet : paiement intégral par le
///    portefeuille (dotation mensuelle d'abord, puis recharges). Refus 402
///    (code insufficient_balance) si le solde ne couvre pas le total : rien
///    n'est créé, le client propose alors le paiement combiné.
///  * POST /api/parent/cart/pay-with-wallet-complement : part portefeuille
///    débitée tout de suite, complément Mobile Money lancé ensuite. Si le
///    complément échoue, expire ou est annulé, la commande passe en échec et
///    la part portefeuille est restituée par contre-passation
///    (<see cref="WalletService.SyncOrderAsync"/>, clés
///    <see cref="WalletService.BalancePartKeys"/>).
///  * POST /api/parent/cart/pay-mobile-money : tout en Mobile Money, sans
///    toucher au portefeuille, toujours pour l'enfant choisi.
///
/// Mêmes garanties que le paiement par solde du professeur : transaction
/// Serializable, verrou du portefeuille, idempotence dérivée côté serveur
/// (même parent, même enfant, mêmes contenus, fenêtre de 60 s), montants TTC
/// relus en base, code promo recalculé. Les contenus déjà possédés par
/// l'enfant sont refusés avant tout débit. Messages dans la langue du compte
/// (User.Locale, lot 10.6).
/// </summary>
[ApiController]
[Route("api/parent/cart")]
[Authorize(Policy = "ParentOnly")]
public class ParentCartCheckoutController : ControllerBase
{
    private static readonly TimeSpan IdempotencyWindow = TimeSpan.FromSeconds(60);

    private readonly ApplicationDbContext _db;
    private readonly IWalletService _wallet;
    private readonly IPaymentService _payments;
    private readonly IPromoCodeService _promoCodes;
    private readonly INtfyService _ntfy;
    private readonly Backend.Services.Entitlements.IEntitlementService _entitlements;
    private readonly ILogger<ParentCartCheckoutController> _logger;
    private readonly Backend.Services.Marketing.IAttributionService? _attribution;
    private readonly Backend.Services.Marketing.IConversionTracker? _conversions;

    public ParentCartCheckoutController(
        ApplicationDbContext db,
        IWalletService wallet,
        IPaymentService payments,
        IPromoCodeService promoCodes,
        INtfyService ntfy,
        Backend.Services.Entitlements.IEntitlementService entitlements,
        ILogger<ParentCartCheckoutController> logger,
        Backend.Services.Marketing.IAttributionService? attribution = null,
        Backend.Services.Marketing.IConversionTracker? conversions = null)
    {
        _db = db;
        _wallet = wallet;
        _payments = payments;
        _promoCodes = promoCodes;
        _ntfy = ntfy;
        _entitlements = entitlements;
        _logger = logger;
        _attribution = attribution;
        _conversions = conversions;
    }

    // ── Préparation commune ─────────────────────────────────────────────

    private sealed record Line(int? SubjectId, int? CourseId, string Title, decimal PriceTtc);

    private sealed record Prepared(List<Line> Lines, decimal Total, CartPromo.Result Promo, string CartKey, string ChildMarker, string ChildName);

    private bool _english;
    private string L(string fr, string en) => _english ? en : fr;

    private IActionResult Error(int status, string code, string fr, string en, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["success"] = false, ["code"] = code, ["error"] = L(fr, en) };
        if (extra != null)
            foreach (var p in extra.GetType().GetProperties())
                body[p.Name] = p.GetValue(extra);
        return StatusCode(status, body);
    }

    /// <summary>
    /// Transaction Serializable sur PostgreSQL. Le fournisseur InMemory des
    /// tests n'a pas de transaction : le verrou du portefeuille y est alors un
    /// verrou de processus (<see cref="WalletService.RunLockedAsync{T}"/>).
    /// </summary>
    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync() =>
        _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null;

    /// <summary>
    /// Lien parent-enfant, panier valorisé au prix TTC en base, remise. Renvoie
    /// une réponse d'erreur, ou le panier préparé. Les contrôles « déjà
    /// possédé » sont faits à part (après la détection de doublon, qu'ils
    /// masqueraient sinon : un rejeu porte sur des contenus déjà acquis).
    /// </summary>
    private async Task<(IActionResult? Error, Prepared? Cart)> PrepareAsync(int parentId, ParentCartCheckoutRequest request)
    {
        var child = await _db.ParentStudentLinks.AsNoTracking()
            .Where(l => l.ParentId == parentId && l.StudentId == request.ChildId && l.Status == "accepted")
            .Select(l => new { l.Student!.FirstName, l.Student.LastName })
            .FirstOrDefaultAsync();
        if (child == null)
            return (Error(403, "child_not_linked",
                "Cet enfant n'est pas lié à votre compte (ou n'a pas encore accepté le lien).",
                "This child is not linked to your account (or has not accepted the link yet)."), null);

        var cart = await _db.CartItems.AsNoTracking()
            .Where(c => c.UserId == parentId)
            .OrderBy(c => c.Id)
            .Select(c => new { c.SubjectId, c.CourseId })
            .ToListAsync();
        if (cart.Count == 0)
            return (Error(400, "cart_empty", "Votre panier est vide.", "Your cart is empty."), null);

        var lines = new List<Line>();
        foreach (var item in cart)
        {
            if (item.CourseId is int courseId)
            {
                var course = await _db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId);
                if (course == null || course.Status != "published")
                    return (Error(400, "content_unavailable",
                        $"La formation « {course?.Title ?? courseId.ToString()} » n'est plus disponible à la vente. Retirez-la du panier pour continuer.",
                        $"The course \"{course?.Title ?? courseId.ToString()}\" is no longer for sale. Remove it from your cart to continue.",
                        new { courseId }), null);
                lines.Add(new Line(null, course.Id, course.Title, decimal.Round(course.Price, 0, MidpointRounding.AwayFromZero)));
            }
            else if (item.SubjectId is int subjectId)
            {
                var subject = await _db.Subjects.AsNoTracking()
                    .Where(s => s.Id == subjectId)
                    .Select(s => new { s.Id, s.Title, s.Price, s.IsDeleted, s.IsPublished })
                    .FirstOrDefaultAsync();
                if (subject == null || subject.IsDeleted || !subject.IsPublished)
                    return (Error(400, "content_unavailable",
                        $"Le contenu « {subject?.Title ?? subjectId.ToString()} » n'est plus disponible à la vente. Retirez-le du panier pour continuer.",
                        $"The content \"{subject?.Title ?? subjectId.ToString()}\" is no longer for sale. Remove it from your cart to continue.",
                        new { subjectId }), null);
                lines.Add(new Line(subject.Id, null, subject.Title, decimal.Round(subject.Price, 0, MidpointRounding.AwayFromZero)));
            }
        }

        // Jamais deux fois le même contenu dans une commande (F4).
        lines = lines.GroupBy(l => CourseSales.LineKey(l.SubjectId, l.CourseId)).Select(g => g.First()).ToList();

        var priced = lines.Select(l => new PricedCartItem(l.SubjectId, l.CourseId, l.Title, l.PriceTtc, l.PriceTtc)).ToList();
        var promo = await CartPromo.ComputeAsync(_promoCodes, parentId, priced, request.PromoCode);
        if (promo.Invalid)
            return (Error(400, "promo_invalid",
                "Le code promo n'est plus valable : retirez-le du panier pour voir le nouveau total.",
                "The promo code is no longer valid: remove it from your cart to see the new total."), null);

        var total = Math.Max(0m, lines.Sum(l => l.PriceTtc) - promo.Discount);
        var name = $"{child.FirstName} {child.LastName}".Trim();
        return (null, new Prepared(lines, total, promo,
            CourseSales.LinesKey(lines.Select(l => (l.SubjectId, l.CourseId))),
            ParentCreditsController.ChildPurchaseMarker(request.ChildId), name));
    }

    /// <summary>Contenus du panier que l'enfant possède déjà (épreuve inscrite ou achetée, formation suivie ou achetée).</summary>
    private async Task<IActionResult?> CheckNotOwnedAsync(int parentId, int childId, Prepared cart)
    {
        var owned = new List<Line>();
        foreach (var line in cart.Lines)
        {
            if (line.SubjectId is int subjectId)
            {
                var has = await _db.Enrollments.AnyAsync(e => e.UserId == childId && e.SubjectId == subjectId)
                    || await _db.OrderItems.AnyAsync(oi => oi.SubjectId == subjectId && oi.Order.UserId == childId
                                                        && PaidOrderStatus.All.Contains(oi.Order.Status.ToLower()));
                if (has) owned.Add(line);
            }
            else if (line.CourseId is int courseId)
            {
                var course = await _db.Courses.AsNoTracking().FirstAsync(c => c.Id == courseId);
                var block = await CourseSales.PurchaseBlockAsync(_db, parentId, childId, course);
                if (block != null)
                {
                    if (block.Value.Code == "free_course")
                        return Error(409, "free_course",
                            $"La formation « {course.Title} » est gratuite : votre enfant peut s'y inscrire directement. Retirez-la du panier.",
                            $"The course \"{course.Title}\" is free: your child can enrol directly. Remove it from your cart.",
                            new { courseId });
                    if (block.Value.Code == "own_course")
                        return Error(409, "own_course",
                            $"La formation « {course.Title} » appartient à votre compte ou à celui de votre enfant : elle ne s'achète pas.",
                            $"The course \"{course.Title}\" belongs to your account or your child's: it cannot be bought.",
                            new { courseId });
                    owned.Add(line);
                }
            }
        }
        if (owned.Count == 0) return null;

        var titles = string.Join(", ", owned.Select(o => $"« {o.Title} »"));
        var titlesEn = string.Join(", ", owned.Select(o => $"\"{o.Title}\""));
        return Error(409, "already_owned",
            $"Votre enfant a déjà accès à : {titles}. Retirez ce contenu du panier pour continuer, rien n'a été débité.",
            $"Your child already has access to: {titlesEn}. Remove it from your cart to continue; nothing was charged.",
            new
            {
                subjectIds = owned.Where(o => o.SubjectId != null).Select(o => o.SubjectId!.Value).ToList(),
                courseIds = owned.Where(o => o.CourseId != null).Select(o => o.CourseId!.Value).ToList(),
            });
    }

    private Order NewOrder(int parentId, Prepared cart, string status, string paymentMethod)
    {
        var order = new Order
        {
            UserId         = parentId,
            OrderNumber    = $"WP-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            TotalAmount    = cart.Total,
            DiscountAmount = cart.Promo.Discount,
            PromoCode      = cart.Promo.Code,
            Status         = status,
            PaymentMethod  = paymentMethod,
            OrderDate      = DateTime.UtcNow,
            CompletedDate  = status == "completed" ? DateTime.UtcNow : null,
            // Bénéficiaire, relu par l'accès (ContentAccessService), les
            // inscriptions aux formations (CourseSales) et aux épreuves
            // (ChildSubjectAccess) et la détection de doublon.
            Notes          = cart.ChildMarker,
        };
        foreach (var line in cart.Lines)
            order.Items.Add(new OrderItem
            {
                SubjectId = line.SubjectId,
                CourseId = line.CourseId,
                // 18.21 : prix TTC payé, la ligne porte le HT déduit, base du
                // revenu de l'auteur (la remise reste à la charge de la plateforme).
                PriceAtPurchase = VatPolicy.ExclTaxFromInclTax(line.PriceTtc),
                Order = order,
            });
        return order;
    }

    private async Task ClearCartAsync(int parentId)
    {
        var items = await _db.CartItems.Where(c => c.UserId == parentId).ToListAsync();
        _db.CartItems.RemoveRange(items);
        await _db.SaveChangesAsync();
    }

    private string PurchaseLabel(Prepared cart)
    {
        var first = cart.Lines[0].Title;
        var more = cart.Lines.Count > 1 ? $" (+{cart.Lines.Count - 1})" : "";
        var who = string.IsNullOrWhiteSpace(cart.ChildName) ? "" : L($" pour {cart.ChildName}", $" for {cart.ChildName}");
        return L($"Achat{who} : {first}{more}", $"Purchase{who}: {first}{more}");
    }

    /// <summary>Commande du même parent, pour le même enfant et les mêmes contenus, créée dans la fenêtre d'idempotence.</summary>
    private async Task<Order?> RecentSameCartAsync(int parentId, Prepared cart, Func<Order, bool> statusFilter)
    {
        var since = DateTime.UtcNow.Subtract(IdempotencyWindow);
        var candidates = await _db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.UserId == parentId && o.Notes == cart.ChildMarker && o.CreatedAt >= since)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
        return candidates.FirstOrDefault(o => statusFilter(o)
            && CourseSales.LinesKey(o.Items.Select(i => (i.SubjectId, i.CourseId))) == cart.CartKey);
    }

    // ── Paiement intégral par le portefeuille ──────────────────────────

    [HttpPost("pay-with-wallet")]
    public async Task<IActionResult> PayWithWallet([FromBody] ParentCartCheckoutRequest request)
    {
        var parentId = User.GetUserId();
        _english = NotificationLocales.IsEnglish(await NotificationLocales.ForUserAsync(_db, parentId));
        Order order;
        Prepared cart;
        decimal available;
        try
        {
            await using var tx = await BeginAsync();
            // Verrou pris dès l'ouverture : la détection de doublon, la
            // lecture du solde et le débit voient le même état.
            await _wallet.RunLockedAsync(parentId, () => Task.FromResult(true));
            await Backend.Services.Entitlements.ParentAllowance.EnsureCurrentAsync(_entitlements, _wallet, parentId, DateTime.UtcNow);

            var (error, prepared) = await PrepareAsync(parentId, request);
            if (error != null) return error;
            cart = prepared!;

            // Double soumission : la commande déjà payée est renvoyée, le
            // panier (repoussé par le client) est vidé, rien n'est redébité.
            var duplicate = await RecentSameCartAsync(parentId, cart, o =>
                o.PaymentMethod == "parent_wallet" && PaidOrderStatus.IsPaid(o.Status) && o.TotalAmount == cart.Total);
            if (duplicate != null)
            {
                await ClearCartAsync(parentId);
                if (tx != null) await tx.CommitAsync();
                _logger.LogWarning("Double soumission du paiement du panier pour l'enfant : parent {ParentId}, commande {OrderId} renvoyée", parentId, duplicate.Id);
                return Ok(new
                {
                    success = true,
                    duplicate = true,
                    message = L($"Cet achat a déjà été payé (commande {duplicate.OrderNumber}). Aucun second débit n'a été effectué.",
                                $"This purchase has already been paid (order {duplicate.OrderNumber}). You were not charged twice."),
                    data = new { orderId = duplicate.Id, orderNumber = duplicate.OrderNumber, status = duplicate.Status, paidWithWallet = true, amount = duplicate.TotalAmount, childId = request.ChildId },
                });
            }

            var ownedError = await CheckNotOwnedAsync(parentId, request.ChildId, cart);
            if (ownedError != null) return ownedError;

            available = Math.Max(0, await _wallet.GetAvailableAsync(parentId));
            if (available < cart.Total)
                return Error(402, "insufficient_balance",
                    $"Solde du portefeuille insuffisant : {available:0} XAF disponibles, {cart.Total:0} XAF requis. Choisissez le paiement combiné (portefeuille + Mobile Money) ou rechargez votre portefeuille.",
                    $"Not enough wallet balance: {available:0} XAF available, {cart.Total:0} XAF needed. Choose the combined payment (wallet + Mobile Money) or top up your wallet.",
                    new { walletAvailable = available, requiredXaf = cart.Total, missingXaf = cart.Total - available });

            order = NewOrder(parentId, cart, "completed", "parent_wallet");
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            if (cart.Total > 0)
                await _wallet.DebitParentWalletAsync(parentId, cart.Total, $"purchase_for_child:order:{order.Id}",
                    PurchaseLabel(cart), "Order", order.Id);

            await ClearCartAsync(parentId);
            // Inscriptions de l'enfant aux épreuves, dans la même transaction.
            await ChildSubjectAccess.SyncForOrderAsync(_db, order.Id);
            if (tx != null) await tx.CommitAsync();
        }
        catch (InsufficientWalletBalanceException ex)
        {
            return Error(402, "insufficient_balance",
                $"Solde du portefeuille insuffisant : {ex.AvailableXaf:0} XAF disponibles, {ex.RequiredXaf:0} XAF requis.",
                $"Not enough wallet balance: {ex.AvailableXaf:0} XAF available, {ex.RequiredXaf:0} XAF needed.",
                new { walletAvailable = ex.AvailableXaf, requiredXaf = ex.RequiredXaf, missingXaf = ex.RequiredXaf - ex.AvailableXaf });
        }
        catch (Exception ex) when (DbConcurrency.IsSerializationFailure(ex))
        {
            _logger.LogWarning(ex, "Collision de sérialisation sur le paiement du panier parent");
            return Error(409, "busy",
                "Un autre paiement sur votre portefeuille est en cours de traitement. Réessayez dans un instant.",
                "Another payment from your wallet is being processed. Please try again in a moment.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du paiement du panier parent par le portefeuille");
            return Error(500, "server_error", "Erreur serveur : rien n'a été débité. Réessayez.", "Server error: nothing was charged. Please try again.");
        }

        await AfterPaidAsync(parentId, order, request);
        await NotifyChildAsync(request.ChildId, cart);

        return Ok(new
        {
            success = true,
            message = L("Achat effectué : le contenu est disponible dans l'espace de votre enfant.",
                        "Purchase complete: the content is available in your child's space."),
            data = new
            {
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                status = order.Status,
                paidWithWallet = true,
                amount = order.TotalAmount,
                walletLeft = available - order.TotalAmount,
                childId = request.ChildId,
            },
        });
    }

    /// <summary>Hors transaction, idempotent : ventes des auteurs, inscriptions aux formations, code promo, attribution, conversion.</summary>
    private async Task AfterPaidAsync(int parentId, Order order, ParentCartCheckoutRequest request)
    {
        try { await _wallet.SyncOrderAsync(order.Id); }
        catch (Exception ex) { _logger.LogError(ex, "Écritures de la commande {OrderId} non posées (réconciliation à venir)", order.Id); }

        if (!string.IsNullOrEmpty(order.PromoCode))
        {
            try { await _promoCodes.ApplyPromoCodeAsync(parentId, order.Id, order.PromoCode); }
            catch (Exception ex) { _logger.LogError(ex, "Code promo de la commande {OrderId} non décompté", order.Id); }
        }

        if (_attribution != null)
            await _attribution.ApplyToOrderAsync(order.Id, parentId, request.Attribution, HttpContext?.RequestAborted ?? default);
        if (_conversions != null)
            await _conversions.RecordOrderPurchaseAsync(order.Id);
    }

    private async Task NotifyChildAsync(int childId, Prepared cart)
    {
        try
        {
            var first = cart.Lines[0];
            var message = cart.Lines.Count == 1
                ? new LocalizedText($"Un parent vient de vous offrir « {first.Title} ».", $"A parent has just given you \"{first.Title}\".")
                : new LocalizedText($"Un parent vient de vous offrir {cart.Lines.Count} contenus, dont « {first.Title} ».",
                                    $"A parent has just given you {cart.Lines.Count} items, including \"{first.Title}\".");
            await _ntfy.PublishLocalizedAsync(_db,
                topic: $"winplus-user-{childId}",
                title: new("Nouveau contenu disponible", "New content available"),
                message: message,
                tags: new[] { "gift" },
                userId: childId,
                type: "content",
                relatedEntityType: first.CourseId != null ? "course" : "subject",
                relatedEntityId: first.CourseId ?? first.SubjectId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification de l'enfant {ChildId} non envoyée", childId);
        }
    }

    // ── Paiement combiné et paiement Mobile Money pour l'enfant ────────

    [HttpPost("pay-with-wallet-complement")]
    public Task<IActionResult> PayWithWalletComplement([FromBody] ParentCartCheckoutRequest request) =>
        PayWithMobileMoneyAsync(request, useWallet: true);

    [HttpPost("pay-mobile-money")]
    public Task<IActionResult> PayMobileMoney([FromBody] ParentCartCheckoutRequest request) =>
        PayWithMobileMoneyAsync(request, useWallet: false);

    private async Task<IActionResult> PayWithMobileMoneyAsync(ParentCartCheckoutRequest request, bool useWallet)
    {
        var parentId = User.GetUserId();
        _english = NotificationLocales.IsEnglish(await NotificationLocales.ForUserAsync(_db, parentId));

        var op = (request.Operator ?? string.Empty).Trim().ToLowerInvariant();
        if (op is not ("mtn" or "orange"))
            return Error(400, "invalid_operator", "Opérateur invalide : MTN MoMo ou Orange Money.", "Invalid operator: MTN MoMo or Orange Money.");
        var phone = PhoneNumbers.CameroonMobileOrNull(request.Phone);
        if (phone == null)
            return Error(400, "invalid_phone", "Numéro Mobile Money invalide : 9 chiffres commençant par 6.", "Invalid Mobile Money number: 9 digits starting with 6.");

        Order order;
        decimal walletPart, momoPart;
        try
        {
            await using var tx = await BeginAsync();
            await _wallet.RunLockedAsync(parentId, () => Task.FromResult(true));
            await Backend.Services.Entitlements.ParentAllowance.EnsureCurrentAsync(_entitlements, _wallet, parentId, DateTime.UtcNow);

            var (error, prepared) = await PrepareAsync(parentId, request);
            if (error != null) return error;
            var cart = prepared!;

            // Double soumission : un paiement identique vient d'être lancé et
            // attend la confirmation de l'opérateur. Le panier repoussé est
            // vidé, aucun second débit ni second paiement n'est lancé.
            var inFlight = await RecentSameCartAsync(parentId, cart, o =>
                o.Status.ToLower() is "pending" or "processing" || PaidOrderStatus.IsPaid(o.Status));
            if (inFlight != null)
            {
                await ClearCartAsync(parentId);
                if (tx != null) await tx.CommitAsync();
                return Error(409, "payment_in_progress",
                    $"Un paiement identique est déjà en cours (commande {inFlight.OrderNumber}). Validez-le sur votre téléphone ; rien n'a été débité une seconde fois.",
                    $"An identical payment is already in progress (order {inFlight.OrderNumber}). Confirm it on your phone; you were not charged twice.",
                    new { orderId = inFlight.Id, orderNumber = inFlight.OrderNumber });
            }

            var ownedError = await CheckNotOwnedAsync(parentId, request.ChildId, cart);
            if (ownedError != null) return ownedError;
            if (cart.Total < OrdersController.MinimumMobileMoneyXaf)
                return Error(400, "amount_too_small",
                    $"Le paiement Mobile Money doit être d'au moins {OrdersController.MinimumMobileMoneyXaf:0} XAF : payez avec votre portefeuille.",
                    $"A Mobile Money payment must be at least {OrdersController.MinimumMobileMoneyXaf:0} XAF: pay with your wallet.");

            walletPart = 0m;
            if (useWallet)
            {
                var available = Math.Max(0, await _wallet.GetAvailableAsync(parentId));
                if (available >= cart.Total)
                    return Error(409, "balance_sufficient",
                        "Votre portefeuille couvre tout le panier : payez avec le portefeuille seul.",
                        "Your wallet covers the whole cart: pay with the wallet only.",
                        new { walletAvailable = available });
                if (available <= 0)
                    return Error(402, "no_balance",
                        "Aucun solde disponible à combiner : payez en Mobile Money ou rechargez votre portefeuille.",
                        "No balance available to combine: pay with Mobile Money or top up your wallet.",
                        new { walletAvailable = 0 });
                // La part Mobile Money ne descend jamais sous le minimum NotchPay.
                walletPart = RevenueSplit.Xaf(Math.Floor(Math.Min(available, cart.Total - OrdersController.MinimumMobileMoneyXaf)));
                if (walletPart <= 0)
                    return Error(400, "complement_too_small",
                        $"Le complément Mobile Money doit être d'au moins {OrdersController.MinimumMobileMoneyXaf:0} XAF : payez ce panier entièrement en Mobile Money.",
                        $"The Mobile Money top-up must be at least {OrdersController.MinimumMobileMoneyXaf:0} XAF: pay this cart fully with Mobile Money.");
            }
            momoPart = cart.Total - walletPart;

            order = NewOrder(parentId, cart, "pending", op);
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            if (walletPart > 0)
                await _wallet.DebitParentWalletAsync(parentId, walletPart, WalletService.ParentComplementKeyBase(order.Id),
                    PurchaseLabel(cart) + L(" (part portefeuille)", " (wallet part)"), "Order", order.Id);

            await ClearCartAsync(parentId);
            if (tx != null) await tx.CommitAsync();
        }
        catch (InsufficientWalletBalanceException ex)
        {
            return Error(402, "insufficient_balance",
                $"Solde du portefeuille insuffisant : {ex.AvailableXaf:0} XAF disponibles.",
                $"Not enough wallet balance: {ex.AvailableXaf:0} XAF available.",
                new { walletAvailable = ex.AvailableXaf });
        }
        catch (Exception ex) when (DbConcurrency.IsSerializationFailure(ex))
        {
            return Error(409, "busy",
                "Un autre paiement sur votre portefeuille est en cours. Réessayez dans un instant.",
                "Another payment from your wallet is in progress. Please try again in a moment.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la préparation du paiement Mobile Money du panier parent");
            return Error(500, "server_error", "Erreur serveur : rien n'a été débité. Réessayez.", "Server error: nothing was charged. Please try again.");
        }

        if (_attribution != null)
            await _attribution.ApplyToOrderAsync(order.Id, parentId, request.Attribution, HttpContext?.RequestAborted ?? default);

        try
        {
            var payment = await _payments.InitiateNotchPayAsync(parentId, new InitiatePaymentRequest
            {
                OrderId = order.Id,
                Phone = phone,
                Amount = momoPart,
                Description = walletPart > 0
                    ? $"WinPlus : complément de la commande {order.OrderNumber}"
                    : $"WinPlus : commande {order.OrderNumber}",
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
                    balancePartXaf = walletPart,
                    momoPartXaf = momoPart,
                    childId = request.ChildId,
                },
            });
        }
        catch (Exception ex)
        {
            // Paiement impossible à lancer : la commande passe en échec et la
            // part portefeuille est restituée tout de suite (contre-passation).
            _logger.LogWarning(ex, "Paiement Mobile Money de la commande parent {OrderId} non lancé : part portefeuille restituée", order.Id);
            try
            {
                var tracked = await _db.Orders.FirstAsync(o => o.Id == order.Id);
                tracked.Status = "failed";
                await _db.SaveChangesAsync();
                await _wallet.SyncOrderAsync(order.Id);
            }
            catch (Exception inner)
            {
                _logger.LogError(inner, "Restitution de la part portefeuille de la commande {OrderId} à reprendre (réconciliation)", order.Id);
            }
            var unavailable = ex is HttpRequestException;
            return Error(unavailable ? 503 : 400, unavailable ? "payment_unavailable" : "payment_not_started",
                unavailable
                    ? "Service de paiement temporairement indisponible. Votre portefeuille n'a pas été débité."
                    : "Le paiement Mobile Money n'a pas pu être lancé. Votre portefeuille n'a pas été débité.",
                unavailable
                    ? "The payment service is temporarily unavailable. Your wallet was not charged."
                    : "The Mobile Money payment could not be started. Your wallet was not charged.");
        }
    }
}
