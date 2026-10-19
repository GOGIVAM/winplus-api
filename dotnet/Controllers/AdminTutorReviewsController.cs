using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Avis de tuteur signalés (lot 9.7a) : le signalement existait
/// (TutorReview.IsReported) mais aucun écran ne permettait de le traiter.
///
///   GET  /api/admin/tutor-reviews/reported?status=pending|processed&amp;page=&amp;pageSize=
///   POST /api/admin/tutor-reviews/{id}/moderate   { decision: keep|remove, reason }
///
/// « remove » retire l'avis du profil et de la note moyenne (la ligne reste :
/// l'auteur ne peut pas renoter la séance) ; « keep » le conserve et clôt le
/// signalement. Décision journalisée, motif obligatoire.
/// </summary>
[ApiController]
[Route("api/admin/tutor-reviews")]
[Authorize(Policy = "AdminOnly")]
[Produces("application/json")]
public class AdminTutorReviewsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IAdminAuditService _audit;
    private readonly INtfyService _ntfy;

    public AdminTutorReviewsController(ApplicationDbContext db, IAdminAuditService audit, INtfyService ntfy)
    {
        _db = db;
        _audit = audit;
        _ntfy = ntfy;
    }

    public sealed record ModerateReviewRequest(string? Decision, string? Reason);

    [HttpGet("reported")]
    public async Task<IActionResult> Reported([FromQuery] string status = "pending", [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.TutorReviews.AsNoTracking().Where(r => r.IsReported || r.ModerationDecision != null);
        query = status == "processed" ? query.Where(r => r.ModerationDecision != null) : query.Where(r => r.ModerationDecision == null && r.IsReported);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(r => r.ReportedAt ?? r.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new
            {
                id = r.Id,
                rating = r.Rating,
                comment = r.Comment,
                tutorReply = r.TutorReply,
                reportReason = r.ReportReason,
                reportedAt = r.ReportedAt,
                reportedBy = _db.Users.Where(u => u.Id == r.ReportedByUserId).Select(u => u.Email).FirstOrDefault(),
                authorEmail = r.Student != null ? r.Student.Email : null,
                tutorProfileId = r.TutorProfileId,
                tutorName = r.TutorProfile != null && r.TutorProfile.User != null
                    ? (r.TutorProfile.User.FirstName + " " + r.TutorProfile.User.LastName).Trim() : null,
                bookingId = r.TutorBookingId,
                createdAt = r.CreatedAt,
                decision = r.ModerationDecision,
                isHidden = r.IsHiddenByModeration,
                moderatedAt = r.ModeratedAt,
                moderatedBy = _db.Users.Where(u => u.Id == r.ModeratedByUserId).Select(u => u.Email).FirstOrDefault(),
            })
            .ToListAsync();
        return Ok(new { success = true, data = new { items, total, page, pageSize } });
    }

    [HttpPost("{id:int}/moderate")]
    public async Task<IActionResult> Moderate(int id, [FromBody] ModerateReviewRequest request)
    {
        var decision = (request?.Decision ?? "").Trim().ToLowerInvariant();
        if (decision is not ("keep" or "remove"))
            return BadRequest(new { error = "Décision attendue : keep (conserver) ou remove (retirer)." });
        var reason = (request?.Reason ?? "").Trim();
        if (reason.Length < AdminUsersController.MinReasonLength)
            return BadRequest(new { code = "reason_required", error = $"Un motif d'au moins {AdminUsersController.MinReasonLength} caractères est obligatoire." });

        var review = await _db.TutorReviews.Include(r => r.TutorProfile).FirstOrDefaultAsync(r => r.Id == id);
        if (review == null) return NotFound(new { error = "Avis introuvable." });
        if (review.ModerationDecision != null)
            return Conflict(new { error = "Ce signalement a déjà été traité." });

        review.ModerationDecision = decision == "remove" ? "removed" : "kept";
        review.IsHiddenByModeration = decision == "remove";
        review.IsReported = false;
        review.ModeratedAt = DateTime.UtcNow;
        review.ModeratedByUserId = User.GetUserId();
        this.AuditAdd(_audit, AdminAuditActions.TutorReviewModerated, "TutorReview", id,
            after: new { decision = review.ModerationDecision, review.Rating, review.TutorProfileId }, reason: reason);
        await _db.SaveChangesAsync();

        if (review.ReportedByUserId.HasValue)
        {
            try
            {
                // Lot 10.6 : dans la langue de l'auteur du signalement (User.Locale).
                await _ntfy.PublishLocalizedAsync(_db, $"winplus-user-{review.ReportedByUserId}", new("Signalement d'avis traité", "Review report handled"),
                    decision == "remove"
                        ? new("L'avis que tu as signalé a été retiré après examen par l'équipe WinPlus.",
                              "The review you reported has been removed after review by the WinPlus team.")
                        : new("L'avis que tu as signalé a été examiné par l'équipe WinPlus et reste en ligne.",
                              "The review you reported has been examined by the WinPlus team and stays online."),
                    userId: review.ReportedByUserId, type: "TutorReview", relatedEntityType: "TutorReview", relatedEntityId: id);
            }
            catch { /* la décision est enregistrée ; la notification n'est qu'informative */ }
        }

        return Ok(new { success = true, decision = review.ModerationDecision });
    }
}
