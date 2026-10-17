using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Entitlements;

/// <summary>
/// Droit <c>teacher.max_published_courses</c> (étude 9.7b, section 1.5 :
/// Gratuit 1, Fondateur 3, Pro 10, Expert illimité) appliqué côté serveur.
///
/// Règles :
///   - compte : formations du professeur au statut « published », plus
///     celles « pending_review » à la soumission (une formation en attente
///     occupera une place dès son approbation) ;
///   - seules les <b>nouvelles</b> publications sont bloquées : une formation
///     déjà publiée au-delà de la limite (après une descente de plan) reste
///     publiée, elle n'est jamais dépubliée ;
///   - la formation concernée n'est jamais comptée contre elle-même ;
///   - les publications du catalogue (épreuves, livres) restent illimitées.
/// </summary>
public static class CoursePublicationLimits
{
    public const string PublishedStatus = "published";
    public const string PendingStatus = "pending_review";

    public sealed record Snapshot(long Max, int Published, int Pending)
    {
        public bool Unlimited => Max == EntitlementCatalog.Unlimited;
        /// <summary>Places restantes (null si illimité).</summary>
        public long? Remaining => Unlimited ? null : Math.Max(0, Max - Published - Pending);
    }

    public sealed record Verdict(bool Allowed, Snapshot Snapshot, string? Message)
    {
        public object Refusal => new
        {
            error = Message,
            code = "published_courses_limit",
            max = Snapshot.Max,
            published = Snapshot.Published,
            pending = Snapshot.Pending,
            upgradeRequired = true,
        };
    }

    public static async Task<Snapshot> GetAsync(ApplicationDbContext db, IEntitlementService entitlements,
        int teacherId, int? excludeCourseId = null, CancellationToken ct = default)
    {
        var ent = await entitlements.GetAsync(teacherId, ct);
        var max = ent.Int(EntitlementCatalog.TeacherMaxPublishedCourses);
        var rows = await db.Courses.AsNoTracking()
            .Where(c => c.InstructorId == teacherId
                     && (excludeCourseId == null || c.Id != excludeCourseId)
                     && (c.Status == PublishedStatus || c.Status == PendingStatus))
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return new Snapshot(max,
            rows.Where(r => r.Status == PublishedStatus).Sum(r => r.Count),
            rows.Where(r => r.Status == PendingStatus).Sum(r => r.Count));
    }

    /// <summary>
    /// Soumission par le professeur : formations publiées et en attente
    /// comptent toutes les deux.
    /// </summary>
    public static async Task<Verdict> CheckSubmitAsync(ApplicationDbContext db, IEntitlementService entitlements,
        int teacherId, int courseId, CancellationToken ct = default)
    {
        var snap = await GetAsync(db, entitlements, teacherId, courseId, ct);
        if (snap.Unlimited || snap.Published + snap.Pending < snap.Max) return new Verdict(true, snap, null);
        return new Verdict(false, snap, SubmitMessage(snap));
    }

    /// <summary>
    /// Approbation par l'administration : seules les formations déjà publiées
    /// comptent (celles encore en attente sont départagées par l'ordre des
    /// approbations). Une formation déjà publiée est toujours acceptée.
    /// </summary>
    public static async Task<Verdict> CheckApproveAsync(ApplicationDbContext db, IEntitlementService entitlements,
        int teacherId, int courseId, string? currentStatus, CancellationToken ct = default)
    {
        var snap = await GetAsync(db, entitlements, teacherId, courseId, ct);
        if (currentStatus == PublishedStatus || snap.Unlimited || snap.Published < snap.Max)
            return new Verdict(true, snap, null);
        return new Verdict(false, snap,
            $"Le plan de ce professeur permet {Plural(snap.Max)} publiée{(snap.Max > 1 ? "s" : "")} à la fois et il en a déjà {snap.Published}. "
            + "La formation ne peut pas être publiée tant qu'il n'a pas changé de plan ou retiré une formation.");
    }

    private static string SubmitMessage(Snapshot snap)
    {
        var used = snap.Pending > 0
            ? $"{snap.Published} publiée{(snap.Published > 1 ? "s" : "")} et {snap.Pending} en attente de validation"
            : $"{snap.Published} publiée{(snap.Published > 1 ? "s" : "")}";
        return $"Votre plan permet {Plural(snap.Max)} publiée{(snap.Max > 1 ? "s" : "")} à la fois ({used}). "
             + "Passez à un plan supérieur pour en publier davantage. Vos formations déjà publiées restent en ligne.";
    }

    private static string Plural(long n) => n == 1 ? "1 formation" : $"{n} formations";
}
