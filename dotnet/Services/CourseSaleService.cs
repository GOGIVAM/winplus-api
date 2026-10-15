using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Lot 9.7b, chantier B (suivi 18.16 : formations vendues seules, jamais
/// incluses dans un abonnement). Règles de vente d'une formation, en un seul
/// endroit :
///
///  * F1/F8 : l'inscription est créée par le serveur dès que la commande est
///    payée (Mobile Money confirmé, solde, portefeuille parent), avec
///    <c>AccessType = "purchase"</c> et <c>OrderId</c> renseigné ; une
///    inscription inactive est réactivée. Appelée depuis
///    <see cref="WalletService.SyncOrderAsync"/>, point de passage de toutes
///    les confirmations (webhook, consultation de statut, solde, achat parent,
///    remboursement, annulation) et rejoué par la réconciliation : un acheteur
///    dont l'inscription manque (achats d'avant le 9.7b) est rattrapé.
///  * Commande qui n'est plus payée (remboursée, annulée, échouée) :
///    l'inscription rattachée à cette commande est désactivée, sauf si une
///    autre commande payée couvre la formation (elle y est rattachée) ou si la
///    formation est devenue gratuite (accès « free »).
///  * F3/F10 : une formation déjà achetée, déjà suivie, appartenant à
///    l'acheteur (ou au bénéficiaire) ou gratuite ne s'achète pas.
///  * F4 : clé de doublon d'une ligne de commande = (type, identifiant).
/// </summary>
public static class CourseSales
{
    /// <summary>Préfixe du bénéficiaire d'un achat parent, écrit dans Order.Notes.</summary>
    public const string ChildMarkerPrefix = "purchase-for-child:";

    public const string AccessPurchase = "purchase";
    public const string AccessFree = "free";

    /// <summary>Clé (type, identifiant) d'une ligne de panier ou de commande (F4).</summary>
    public static string LineKey(int? subjectId, int? courseId) =>
        courseId != null ? $"course:{courseId}" : $"subject:{subjectId}";

    /// <summary>Clé triée de l'ensemble des lignes, pour comparer deux paniers ou commandes (F4).</summary>
    public static string LinesKey(IEnumerable<(int? SubjectId, int? CourseId)> lines) =>
        string.Join("|", lines.Select(l => LineKey(l.SubjectId, l.CourseId)).Distinct().OrderBy(k => k, StringComparer.Ordinal));

    /// <summary>Formation gratuite : drapeau ou prix nul (F10, les deux sont réconciliés à l'écriture).</summary>
    public static bool IsFreeCourse(Course course) => course.IsFree || course.Price <= 0;

    /// <summary>
    /// F10 : réconcilie <c>IsFree</c> et <c>Price</c> à chaque écriture d'une
    /// formation. Gratuite ⇒ prix 0 ; prix nul ou négatif ⇒ gratuite. Prix TTC
    /// arrondi à l'XAF.
    /// </summary>
    public static void ReconcileFreeAndPrice(Course course)
    {
        course.Price = decimal.Round(Math.Max(0m, course.Price), 0, MidpointRounding.AwayFromZero);
        if (course.IsFree) course.Price = 0m;
        else if (course.Price <= 0) course.IsFree = true;
    }

    /// <summary>Bénéficiaire d'une commande : l'enfant d'un achat parent, sinon l'acheteur.</summary>
    public static int BeneficiaryOf(int buyerUserId, string? notes)
    {
        if (!string.IsNullOrEmpty(notes) && notes.StartsWith(ChildMarkerPrefix, StringComparison.Ordinal)
            && int.TryParse(notes.AsSpan(ChildMarkerPrefix.Length), out var childId) && childId > 0)
            return childId;
        return buyerUserId;
    }

    /// <summary>Commandes payées dont <paramref name="userId"/> est le bénéficiaire.</summary>
    private static IQueryable<OrderItem> PaidCourseItemsFor(ApplicationDbContext db, int userId, int courseId)
    {
        var marker = ChildMarkerPrefix + userId;
        return db.OrderItems.Where(oi => oi.CourseId == courseId
            && oi.Order.UserId != null
            && PaidOrderStatus.All.Contains(oi.Order.Status.ToLower())
            && ((oi.Order.UserId == userId && (oi.Order.Notes == null || !oi.Order.Notes.StartsWith(ChildMarkerPrefix)))
                || oi.Order.Notes == marker));
    }

    /// <summary>Vrai si une commande payée donne cette formation à cet utilisateur.</summary>
    public static Task<bool> IsPurchasedAsync(ApplicationDbContext db, int userId, int courseId) =>
        PaidCourseItemsFor(db, userId, courseId).AnyAsync();

    /// <summary>Plus récente commande payée (autre que <paramref name="excludedOrderId"/>) qui couvre la formation.</summary>
    public static Task<int?> OtherPaidOrderIdAsync(ApplicationDbContext db, int userId, int courseId, int excludedOrderId) =>
        PaidCourseItemsFor(db, userId, courseId)
            .Where(oi => oi.OrderId != excludedOrderId)
            .OrderByDescending(oi => oi.OrderId)
            .Select(oi => (int?)oi.OrderId)
            .FirstOrDefaultAsync();

    /// <summary>
    /// Raison pour laquelle <paramref name="buyerUserId"/> ne peut pas acheter
    /// la formation pour <paramref name="beneficiaryUserId"/>, ou null
    /// (F3, F10). Codes stables : free_course, own_course, already_enrolled,
    /// already_purchased.
    /// </summary>
    public static async Task<(string Code, string Message)?> PurchaseBlockAsync(
        ApplicationDbContext db, int buyerUserId, int beneficiaryUserId, Course course)
    {
        if (IsFreeCourse(course))
            return ("free_course", $"La formation « {course.Title} » est gratuite : inscris-toi directement, sans achat.");
        if (course.InstructorId == buyerUserId || course.InstructorId == beneficiaryUserId)
            return ("own_course", "Cette formation est la tienne : elle ne peut pas être achetée par son auteur.");
        if (await db.CourseEnrollments.AnyAsync(e => e.UserId == beneficiaryUserId && e.CourseId == course.Id && e.IsActive))
            return ("already_enrolled", beneficiaryUserId == buyerUserId
                ? $"Tu suis déjà la formation « {course.Title} »."
                : $"Ton enfant suit déjà la formation « {course.Title} ».");
        if (await IsPurchasedAsync(db, beneficiaryUserId, course.Id))
            return ("already_purchased", beneficiaryUserId == buyerUserId
                ? $"La formation « {course.Title} » est déjà achetée."
                : $"La formation « {course.Title} » est déjà achetée pour ton enfant.");
        return null;
    }

    /// <summary>Lève <see cref="CoursePurchaseRefusedException"/> si l'achat est refusé.</summary>
    public static async Task EnsurePurchasableAsync(ApplicationDbContext db, int buyerUserId, int beneficiaryUserId, Course course)
    {
        var block = await PurchaseBlockAsync(db, buyerUserId, beneficiaryUserId, course);
        if (block != null)
            throw new CoursePurchaseRefusedException(course.Id, block.Value.Code, block.Value.Message);
    }

    /// <summary>
    /// F1/F8 : aligne les inscriptions aux formations d'une commande sur son
    /// statut. Idempotent (rejoué par la réconciliation) ; n'écrit que ce qui
    /// change. Renvoie le nombre d'inscriptions créées, réactivées,
    /// rattachées ou désactivées.
    /// </summary>
    public static async Task<int> SyncEnrollmentsForOrderAsync(ApplicationDbContext db, int orderId)
    {
        var order = await db.Orders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.Id, o.UserId, o.Status, o.Notes })
            .FirstOrDefaultAsync();
        if (order?.UserId == null) return 0;

        var courseIds = await db.OrderItems.AsNoTracking()
            .Where(oi => oi.OrderId == orderId && oi.CourseId != null)
            .Select(oi => oi.CourseId!.Value)
            .Distinct()
            .ToListAsync();
        if (courseIds.Count == 0) return 0;

        var beneficiary = BeneficiaryOf(order.UserId.Value, order.Notes);
        var status = (order.Status ?? string.Empty).Trim().ToLowerInvariant();
        var changed = 0;

        if (PaidOrderStatus.IsPaid(status))
        {
            foreach (var courseId in courseIds)
            {
                var enrollment = await db.CourseEnrollments
                    .FirstOrDefaultAsync(e => e.UserId == beneficiary && e.CourseId == courseId);
                if (enrollment == null)
                {
                    var fresh = new CourseEnrollment
                    {
                        UserId = beneficiary,
                        CourseId = courseId,
                        AccessType = AccessPurchase,
                        OrderId = orderId,
                        EnrolledAt = DateTime.UtcNow,
                        IsActive = true,
                    };
                    db.CourseEnrollments.Add(fresh);
                    changed++;
                }
                else if (!enrollment.IsActive)
                {
                    // Réactivation (rachat après remboursement) : progression conservée.
                    enrollment.IsActive = true;
                    enrollment.AccessType = AccessPurchase;
                    enrollment.OrderId = orderId;
                    changed++;
                }
                else if (enrollment.AccessType != AccessPurchase || enrollment.OrderId == null)
                {
                    // Inscription active ouverte autrement (gratuite, ancien
                    // abonnement, /enroll d'avant le 9.7b) : rattachée à
                    // l'achat, pour que le remboursement la retrouve (F8).
                    enrollment.AccessType = AccessPurchase;
                    enrollment.OrderId ??= orderId;
                    changed++;
                }
            }
        }
        else if (status is not ("pending" or "processing"))
        {
            var linked = await db.CourseEnrollments
                .Include(e => e.Course)
                .Where(e => e.OrderId == orderId && e.IsActive)
                .ToListAsync();
            foreach (var enrollment in linked)
            {
                var other = await OtherPaidOrderIdAsync(db, enrollment.UserId, enrollment.CourseId, orderId);
                if (other != null)
                {
                    enrollment.OrderId = other;
                }
                else if (enrollment.Course != null && IsFreeCourse(enrollment.Course))
                {
                    enrollment.AccessType = AccessFree;
                    enrollment.OrderId = null;
                }
                else
                {
                    enrollment.IsActive = false;
                }
                changed++;
            }
        }

        if (changed == 0) return 0;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Deux confirmations simultanées (webhook et consultation de
            // statut) : l'autre a déjà créé l'inscription (index unique
            // UserId, CourseId). On retire la nôtre du suivi pour ne pas
            // polluer le contexte de l'appelant ; la réconciliation repasse.
            foreach (var entry in db.ChangeTracker.Entries<CourseEnrollment>()
                         .Where(e => e.State is EntityState.Added or EntityState.Modified).ToList())
                entry.State = EntityState.Detached;
            throw;
        }
        return changed;
    }
}
