using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Règle unique « commande payée » (Module 19, passe de clôture du lot 0).
///
/// Statuts qui valent encaissement acquis : <c>completed</c> (confirmation de
/// paiement), <c>paid</c> (achat parent réglé en crédits) et
/// <c>refund_requested</c> (une demande de remboursement en cours ne retire ni
/// l'accès ni le revenu, décision §4.D du suivi). Comparaison insensible à la
/// casse : les lignes héritées portent des casses mixtes.
///
/// Usage dans une requête EF : <c>PaidOrderStatus.All.Contains(o.Status.ToLower())</c>
/// (traduit en SQL). Usage en mémoire : <c>PaidOrderStatus.IsPaid(o.Status)</c>.
/// </summary>
public static class PaidOrderStatus
{
    /// <summary>Statuts payés, en minuscules. Ne pas modifier le tableau.</summary>
    public static readonly string[] All = { "completed", "paid", "refund_requested" };

    public static bool IsPaid(string? status) =>
        status != null && All.Contains(status.ToLowerInvariant());
}

/// <summary>
/// Règle d'accès unique à un contenu payant (Module 17).
///
/// Elle était auparavant réimplémentée à chaque point de contrôle, ce qui a
/// produit trois variantes divergentes : la consultation en flux, le
/// téléchargement présigné (qui y perdait l'exemption « contenu assigné via
/// une classe »), et l'inscription, qui n'en appliquait aucune. Un seul
/// service porte désormais la règle, pour que corriger l'un corrige tous.
/// </summary>
public interface IContentAccessService
{
    /// <summary>
    /// Vrai si l'utilisateur dispose réellement d'un abonnement payant en
    /// cours. Le palier d'abonnement vit dans la table Subscriptions, jamais
    /// dans le rôle du compte (cause racine du mur payant inerte).
    /// </summary>
    Task<bool> HasActiveSubscriptionAsync(int userId);

    /// <summary>
    /// Vrai si l'utilisateur a droit d'accéder au contenu payant fourni.
    /// Un contenu gratuit n'a pas à passer par cette règle.
    /// </summary>
    Task<bool> HasPaidContentAccessAsync(int userId, Subject subject, bool isAdmin);
}

public class ContentAccessService : IContentAccessService
{
    private readonly ApplicationDbContext _context;

    public ContentAccessService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// ⚠ Dette connue, à lever au Module 21 : <c>Subscription.IsActive</c>
    /// n'est jamais réévalué à l'expiration, et la fenêtre de validité réelle
    /// (comparaison à EndDate) n'est donc pas fiable aujourd'hui. On s'appuie
    /// sur <c>Status == "active"</c>, seul champ réellement piloté
    /// (SubscriptionExpirationService le passe à "expired"), en plus de
    /// l'indicateur existant. Le jour où la fenêtre de validité sera corrigée,
    /// ce test devra intégrer la comparaison de dates.
    ///
    /// Le plan associé doit en outre être un plan <b>payant</b>
    /// (<c>PricingPlan.Price &gt; 0</c>). SubscriptionsController autorise en
    /// effet — et c'est voulu — la création directe d'un abonnement « active »
    /// sur un plan gratuit, sans le moindre paiement. Sans ce test, un tel
    /// abonnement gratuit ouvrait à lui seul tout le catalogue payant : il
    /// suffisait de s'abonner au plan à 0 F pour contourner le mur payant.
    ///
    /// <c>IgnoreQueryFilters()</c> neutralise le filtre global de suppression
    /// logique sur PricingPlan, pour qu'un plan payant archivé par un
    /// administrateur ne coupe pas l'accès des abonnés qui l'ont réellement
    /// payé ; le filtre sur Subscription reste appliqué explicitement via
    /// <c>!s.IsDeleted</c>.
    /// </summary>
    public async Task<bool> HasActiveSubscriptionAsync(int userId) =>
        await _context.Subscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(s => s.UserId == userId
                        && !s.IsDeleted
                        && s.IsActive
                        // Même règle de casse que pour les commandes
                        // (correction §7.2) : la base porte aussi bien
                        // "active" que "Active" sur les lignes héritées, et
                        // SubscriptionRepository a déjà été corrigé en ce sens.
                        && s.Status.ToLower() == "active"
                        // Un plan gratuit ne vaut pas abonnement payant.
                        && s.PricingPlan != null
                        && s.PricingPlan.Price > 0);

    /// <summary>
    /// Statuts de commande qui valent « encaissement acquis ».
    ///
    /// - <c>completed</c> : voie normale, posée par la confirmation de paiement.
    /// - <c>paid</c> : voie « acheter pour mon enfant » réglée en crédits
    ///   parent (ParentCreditsController), qui écrit ce statut en direct.
    /// - <c>refund_requested</c> : une demande de remboursement en cours ne
    ///   coupe pas l'accès, seule une décision administrateur explicite le
    ///   fait (décision §4.D du suivi, traitée en entier au Module 21).
    ///
    /// La comparaison est faite en minuscules (correction §7.2 du suivi) : le
    /// Module 19 normalise bien le statut à l'écriture
    /// (<c>OrderService.UpdateOrderStatusAsync</c>), mais les lignes déjà en
    /// base portent des casses mixtes (« completed », « Completed »), et une
    /// comparaison sensible à la casse refusait donc l'accès à des achats
    /// réellement payés.
    /// </summary>
    private static readonly string[] PaidOrderStatuses = PaidOrderStatus.All;

    public async Task<bool> HasPaidContentAccessAsync(int userId, Subject subject, bool isAdmin)
    {
        if (isAdmin) return true;
        if (subject.AuthorUserId == userId) return true;

        if (await HasActiveSubscriptionAsync(userId)) return true;

        // Achat confirmé par l'utilisateur lui-même. Cette voie n'exige pas
        // d'inscription : l'inscription est un acte postérieur à l'achat
        // (POST /api/enrollments exige justement d'avoir déjà accès), donc en
        // faire une condition ici rendrait toute inscription impossible.
        bool hasPurchased = await _context.OrderItems
            .AnyAsync(oi => oi.SubjectId == subject.Id
                         && oi.Order.UserId == userId
                         && PaidOrderStatuses.Contains(oi.Order.Status.ToLower()));
        if (hasPurchased) return true;

        // Achat réalisé par un tiers au bénéfice de cet utilisateur (parent
        // achetant pour son enfant) : la commande appartient au payeur, seule
        // l'inscription rattache le contenu au bénéficiaire.
        //
        // Correction §7.4 du suivi : l'inscription seule ne suffit plus. Elle
        // ne valait droit d'accès que par héritage de l'ancien endpoint
        // d'inscription ouvert (fermé par le Module 17), qui a laissé en base
        // des inscriptions sans le moindre paiement. Il faut désormais les
        // deux : une inscription ET une commande payée portant ce contenu,
        // passée par un parent réellement lié à cet utilisateur — c'est
        // exactement ce que produit le parcours « acheter pour mon enfant ».
        bool isEnrolled = await _context.Enrollments
            .AnyAsync(e => e.UserId == userId && e.SubjectId == subject.Id);

        if (isEnrolled)
        {
            bool paidByLinkedParent = await _context.OrderItems
                .AnyAsync(oi => oi.SubjectId == subject.Id
                             && PaidOrderStatuses.Contains(oi.Order.Status.ToLower())
                             && oi.Order.UserId != null
                             && _context.ParentStudentLinks.Any(l =>
                                    l.ParentId == oi.Order.UserId
                                 && l.StudentId == userId
                                 && l.Status == "accepted"));
            if (paidByLinkedParent) return true;
        }

        // Contenu assigné par un professeur à une classe dont l'élève est
        // membre (Module 2, US-CAT-04) : WinPlus a déjà débité le professeur
        // une seule fois, l'élève n'a rien à payer. Le professeur qui a payé
        // l'assignation y a évidemment accès aussi.
        return await _context.TeacherClassContents
            .AnyAsync(tcc => tcc.SubjectId == subject.Id &&
                (tcc.AssignedByUserId == userId ||
                 _context.TeacherClassStudents.Any(cs =>
                     cs.TeacherClassId == tcc.TeacherClassId && cs.StudentId == userId)));
    }
}
