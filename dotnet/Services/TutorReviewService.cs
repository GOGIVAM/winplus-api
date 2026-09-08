using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Avis élève sur une séance de cours particulier (US-REP-08). Alimente la
/// note affichée sur la fiche répétiteur et le tri par pertinence de
/// TutorProfileService.SearchAsync.
/// </summary>
public class TutorReviewService : ITutorReviewService
{
    private readonly ApplicationDbContext _context;
    private readonly INtfyService _ntfy;

    public TutorReviewService(ApplicationDbContext context, INtfyService ntfy)
    {
        _context = context;
        _ntfy = ntfy;
    }

    public async Task<TutorReviewDto> SubmitAsync(int studentUserId, SubmitTutorReviewRequestDto request)
    {
        if (request.Rating is < 1 or > 5)
            throw new InvalidOperationException("La note doit être comprise entre 1 et 5.");

        var booking = await _context.TutorBookings
            .Include(b => b.TutorProfile).ThenInclude(p => p!.User)
            .FirstOrDefaultAsync(b => b.Id == request.TutorBookingId)
            ?? throw new InvalidOperationException("Réservation introuvable.");

        if (booking.StudentUserId != studentUserId)
            throw new InvalidOperationException("Cette réservation ne t'appartient pas.");
        if (booking.Status is not ("completed" or "disputed"))
            throw new InvalidOperationException("Tu ne peux noter qu'une séance effectuée.");

        var already = await _context.TutorReviews.AnyAsync(r => r.TutorBookingId == booking.Id);
        if (already)
            throw new InvalidOperationException("Tu as déjà noté cette séance.");

        var review = new TutorReview
        {
            TutorBookingId = booking.Id,
            TutorProfileId = booking.TutorProfileId,
            StudentUserId = studentUserId,
            Rating = request.Rating,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : Truncate(request.Comment, 300),
        };
        _context.TutorReviews.Add(review);
        await _context.SaveChangesAsync();

        var tutorUserId = booking.TutorProfile?.UserId;
        if (tutorUserId.HasValue)
            await _ntfy.PublishAsync($"winplus-user-{tutorUserId.Value}", "Nouvel avis reçu",
                $"{review.Rating}/5 pour ta séance du {booking.SessionDate:dd/MM/yyyy}.", userId: tutorUserId.Value, type: "TutorReview");

        return await MapToDtoAsync(review);
    }

    public async Task<TutorReviewDto> ReplyAsync(int tutorUserId, int reviewId, string reply)
    {
        var review = await _context.TutorReviews
            .Include(r => r.TutorProfile)
            .Include(r => r.Student)
            .FirstOrDefaultAsync(r => r.Id == reviewId)
            ?? throw new InvalidOperationException("Avis introuvable.");

        if (review.TutorProfile?.UserId != tutorUserId)
            throw new InvalidOperationException("Cet avis ne concerne pas ton profil.");

        review.TutorReply = Truncate(reply, 500);
        review.TutorRepliedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return MapToDto(review);
    }

    public async Task<List<TutorReviewDto>> GetForTutorAsync(int tutorUserId, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var reviews = await _context.TutorReviews
            .Include(r => r.Student)
            .Where(r => r.TutorProfile!.UserId == tutorUserId)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return reviews.Select(MapToDto).ToList();
    }

    public async Task<(double? AverageRating, int ReviewCount)> GetAggregateAsync(int tutorProfileId)
    {
        var ratings = await _context.TutorReviews
            .Where(r => r.TutorProfileId == tutorProfileId)
            .Select(r => r.Rating)
            .ToListAsync();
        return ratings.Count == 0 ? (null, 0) : (ratings.Average(), ratings.Count);
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private static TutorReviewDto MapToDto(TutorReview r) => new()
    {
        Id = r.Id,
        TutorBookingId = r.TutorBookingId,
        StudentName = r.Student != null ? $"{r.Student.FirstName} {r.Student.LastName}".Trim() : null,
        StudentAvatarUrl = r.Student?.AvatarUrl,
        Rating = r.Rating,
        Comment = r.Comment,
        TutorReply = r.TutorReply,
        TutorRepliedAt = r.TutorRepliedAt,
        CreatedAt = r.CreatedAt,
    };

    private async Task<TutorReviewDto> MapToDtoAsync(TutorReview r)
    {
        if (r.Student == null)
            await _context.Entry(r).Reference(x => x.Student!).LoadAsync();
        return MapToDto(r);
    }
}
